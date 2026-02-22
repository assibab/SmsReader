using SmsReader.Adb;

namespace SmsReader.Sms;

public sealed class SmsFetcher
{
    private readonly AdbClient _adbClient;

    public SmsFetcher(AdbClient adbClient)
    {
        _adbClient = adbClient;
    }

    public async Task<string> ReadInboxAsync(long sinceEpochMs = 0, int timeoutMs = 10000)
    {
        return await QuerySmsAsync("content://sms", sinceEpochMs, timeoutMs);
    }

    public async Task<string> ReadInboxAsync(long sinceEpochMs, CancellationToken ct)
    {
        return await QuerySmsAsync("content://sms", sinceEpochMs, ct);
    }

    private static string BuildQuery(string uri, string projection, long sinceEpochMs)
    {
        // Wrap the entire content query as a single shell command.
        // Use single quotes for --where and --sort values to prevent the Android shell
        // from interpreting > as a file redirect.
        var query = sinceEpochMs > 0
            ? $"content query --uri {uri} --projection {projection} --where 'date>{sinceEpochMs}' --sort 'date DESC'"
            : $"content query --uri {uri} --projection {projection} --sort 'date DESC'";

        return $"shell \"{query}\"";
    }

    private async Task<string> QuerySmsAsync(string uri, long sinceEpochMs, int timeoutMs)
    {
        var args = BuildQuery(uri, "_id:address:date:read:type:body", sinceEpochMs);
        var result = await _adbClient.ExecuteAsync(args, timeoutMs);
        return HandleResult(result);
    }

    private async Task<string> QuerySmsAsync(string uri, long sinceEpochMs, CancellationToken ct)
    {
        var args = BuildQuery(uri, "_id:address:date:read:type:body", sinceEpochMs);
        var result = await _adbClient.ExecuteAsync(args, ct);
        return HandleResult(result);
    }

    private static string HandleResult(AdbResult result)
    {
        if (result.Output.Contains("No result found", StringComparison.OrdinalIgnoreCase))
            return "";

        if (!result.Success)
        {
            if (result.Error.Contains("No result found", StringComparison.OrdinalIgnoreCase))
                return "";

            throw new InvalidOperationException(
                $"ADB SMS query failed: {result.Error.Trim()} (exit code: {result.ExitCode})");
        }

        return result.Output;
    }
}
