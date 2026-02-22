using System.Globalization;

namespace SmsReader.Sms;

public static class SmsParser
{
    public static List<SmsMessage> Parse(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return [];

        var messages = new List<SmsMessage>();
        var lines = rawOutput.Split('\n');
        SmsMessage? current = null;
        string? currentBodyExtra = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            if (line.StartsWith("Row:", StringComparison.Ordinal))
            {
                // Flush previous message
                if (current != null)
                {
                    if (currentBodyExtra != null)
                        current = current with { Body = current.Body + "\n" + currentBodyExtra };
                    messages.Add(current);
                }

                current = ParseRow(line);
                currentBodyExtra = null;
            }
            else if (current != null && !string.IsNullOrWhiteSpace(line))
            {
                // Multi-line SMS body continuation
                currentBodyExtra = currentBodyExtra == null ? line : currentBodyExtra + "\n" + line;
            }
        }

        // Flush last message
        if (current != null)
        {
            if (currentBodyExtra != null)
                current = current with { Body = current.Body + "\n" + currentBodyExtra };
            messages.Add(current);
        }

        return messages;
    }

    private static SmsMessage? ParseRow(string line)
    {
        // Format: "Row: N _id=123, address=+1234567890, date=1708617600000, read=1, type=1, body=Hello world"
        // We parse known fields from left and right, treating body as everything after "body="

        try
        {
            var afterRow = line;
            var firstSpace = line.IndexOf(' ');
            if (firstSpace < 0) return null;

            // Find each field marker
            var idStart = line.IndexOf("_id=", StringComparison.Ordinal);
            var addressStart = line.IndexOf("address=", StringComparison.Ordinal);
            var dateStart = line.IndexOf("date=", StringComparison.Ordinal);
            var readStart = line.IndexOf("read=", StringComparison.Ordinal);
            var typeStart = line.IndexOf("type=", StringComparison.Ordinal);
            var bodyStart = line.IndexOf("body=", StringComparison.Ordinal);

            if (idStart < 0 || addressStart < 0 || dateStart < 0 || bodyStart < 0)
                return null;

            var id = ExtractLong(line, idStart + 4, addressStart);
            var address = ExtractString(line, addressStart + 8, dateStart);
            var dateMs = ExtractLong(line, dateStart + 5, readStart >= 0 ? readStart : typeStart >= 0 ? typeStart : bodyStart);
            var read = readStart >= 0 ? ExtractInt(line, readStart + 5, typeStart >= 0 ? typeStart : bodyStart) : 0;
            var type = typeStart >= 0 ? ExtractInt(line, typeStart + 5, bodyStart) : 1;
            var body = line[(bodyStart + 5)..];

            var date = DateTimeOffset.FromUnixTimeMilliseconds(dateMs);

            return new SmsMessage
            {
                Id = id,
                Address = address,
                Body = body,
                Date = date,
                Type = type,
                Read = read == 1
            };
        }
        catch
        {
            return null;
        }
    }

    private static long ExtractLong(string line, int start, int nextFieldStart)
    {
        var value = ExtractString(line, start, nextFieldStart);
        return long.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static int ExtractInt(string line, int start, int nextFieldStart)
    {
        var value = ExtractString(line, start, nextFieldStart);
        return int.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static string ExtractString(string line, int start, int nextFieldStart)
    {
        if (nextFieldStart <= start || nextFieldStart > line.Length)
            return line[start..].Trim().TrimEnd(',');

        return line[start..nextFieldStart].Trim().TrimEnd(',').TrimEnd();
    }
}
