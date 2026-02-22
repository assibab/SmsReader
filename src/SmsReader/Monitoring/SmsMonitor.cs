using SmsReader.Configuration;
using SmsReader.Filtering;
using SmsReader.Language;
using SmsReader.Otp;
using SmsReader.Sms;
using Spectre.Console;

namespace SmsReader.Monitoring;

public sealed class SmsMonitor
{
    private readonly SmsFetcher _fetcher;
    private readonly SourceFilter _filter;
    private readonly OtpExtractor _otpExtractor;
    private readonly MonitoringSettings _monitorSettings;
    private readonly OtpSettings _otpSettings;

    private readonly HashSet<long> _knownMessageIds = [];
    private long _lastTimestamp;

    public SmsMonitor(
        SmsFetcher fetcher,
        SourceFilter filter,
        OtpExtractor otpExtractor,
        MonitoringSettings monitorSettings,
        OtpSettings otpSettings)
    {
        _fetcher = fetcher;
        _filter = filter;
        _otpExtractor = otpExtractor;
        _monitorSettings = monitorSettings;
        _otpSettings = otpSettings;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Initial load to populate known IDs
        AnsiConsole.MarkupLine("[grey]Loading existing messages...[/]");
        var initial = await FetchMessagesSince(0, ct);

        foreach (var msg in initial)
            _knownMessageIds.Add(msg.Id);

        if (initial.Count > 0)
            _lastTimestamp = initial.Max(m => m.Date.ToUnixTimeMilliseconds());

        AnsiConsole.MarkupLine($"[green]Loaded {initial.Count} existing messages. Monitoring for new SMS...[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");
        AnsiConsole.WriteLine();

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_monitorSettings.PollingIntervalMs));

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var sinceMs = _lastTimestamp > 0 ? _lastTimestamp - 5000 : 0; // 5s overlap for safety
                var messages = await FetchMessagesSince(sinceMs, ct);
                var newMessages = messages.Where(m => _knownMessageIds.Add(m.Id)).ToList();

                foreach (var msg in newMessages)
                {
                    _lastTimestamp = Math.Max(_lastTimestamp, msg.Date.ToUnixTimeMilliseconds());

                    if (!_filter.ShouldInclude(msg))
                        continue;

                    DisplayMessage(msg);

                    if (_otpSettings.Enabled)
                    {
                        var otp = _otpExtractor.Extract(msg);
                        if (otp != null)
                            DisplayOtp(otp);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Monitoring stopped.[/]");
    }

    private async Task<List<SmsMessage>> FetchMessagesSince(long epochMs, CancellationToken ct)
    {
        try
        {
            var raw = await _fetcher.ReadInboxAsync(epochMs, ct);
            return SmsParser.Parse(raw);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error reading SMS: {Markup.Escape(ex.Message)}[/]");
            return [];
        }
    }

    private void DisplayMessage(SmsMessage msg)
    {
        var timestamp = msg.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var label = _filter.GetMatchLabel(msg);
        var sourceInfo = label != null ? $" [cyan]({Markup.Escape(label)})[/]" : "";

        // Detect language and direction
        var language = LanguageDetector.Detect(msg.Body);
        var isRtl = LanguageDetector.GetDirection(language) == TextDirection.RightToLeft;
        var langTag = RtlFormatter.GetLanguageTag(language);

        // Format address — may contain Hebrew sender names
        var addressDisplay = LanguageDetector.IsRtl(msg.Address)
            ? RtlFormatter.ForceRtl(msg.Address)
            : msg.Address;

        AnsiConsole.MarkupLine(
            $"[grey]{timestamp}[/]  [blue]{Markup.Escape(addressDisplay)}[/]{sourceInfo}  [grey][[{langTag}]][/]");

        var direction = msg.Type == 2 ? "SENT" : "RECEIVED";
        var borderColor = msg.Type == 2 ? Color.Yellow : Color.Green;

        // Apply RTL formatting to message body
        var formattedBody = RtlFormatter.Format(msg.Body);

        var panel = new Panel(new Markup($"[white]{Markup.Escape(formattedBody)}[/]"))
        {
            Header = new PanelHeader($" {direction} "),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0),
            BorderStyle = new Style(borderColor)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private void DisplayOtp(OtpResult otp)
    {
        // OTP codes are always LTR (digits/alphanumeric)
        var code = RtlFormatter.ForceLtr(otp.Code);
        var color = otp.Confidence >= 0.8 ? "green" : otp.Confidence >= 0.6 ? "yellow" : "grey";
        AnsiConsole.MarkupLine(
            $"  [{color}]>>> OTP DETECTED: {Markup.Escape(code)}  " +
            $"(confidence: {otp.Confidence:P0}, pattern: {Markup.Escape(otp.PatternName)})[/{color}]");
        AnsiConsole.WriteLine();
    }
}
