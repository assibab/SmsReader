namespace SmsReader.Sms;

public sealed record SmsMessage
{
    public long Id { get; init; }
    public string Address { get; init; } = "";
    public string Body { get; init; } = "";
    public DateTimeOffset Date { get; init; }
    public int Type { get; init; } // 1 = received, 2 = sent
    public bool Read { get; init; }

    public string TypeLabel => Type switch
    {
        1 => "Received",
        2 => "Sent",
        _ => $"Type({Type})"
    };
}
