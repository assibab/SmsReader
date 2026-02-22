using SmsReader.Agent;
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
    private readonly SmsAgent _agent;
    private readonly MonitoringSettings _monitorSettings;
    private readonly OtpSettings _otpSettings;
    private readonly AgentSettings _agentSettings;

    private readonly HashSet<long> _knownMessageIds = [];
    private long _lastTimestamp;

    public SmsMonitor(
        SmsFetcher fetcher,
        SourceFilter filter,
        OtpExtractor otpExtractor,
        SmsAgent agent,
        MonitoringSettings monitorSettings,
        OtpSettings otpSettings,
        AgentSettings agentSettings)
    {
        _fetcher = fetcher;
        _filter = filter;
        _otpExtractor = otpExtractor;
        _agent = agent;
        _monitorSettings = monitorSettings;
        _otpSettings = otpSettings;
        _agentSettings = agentSettings;
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

                    // Pipeline: regex OTP → classify → enriched display → auto-copy
                    OtpResult? otp = _otpSettings.Enabled ? _otpExtractor.Extract(msg) : null;

                    var classification = await _agent.ClassifyAsync(msg.Body, msg.Address, otp);

                    DisplayMessage(msg, classification);

                    if (otp != null)
                        DisplayOtp(otp);

                    // If LLM detected an OTP that regex missed
                    if (otp == null && classification.DetectedOtp != null)
                    {
                        AnsiConsole.MarkupLine(
                            $"  [green]>>> OTP (LLM): {Markup.Escape(classification.DetectedOtp)}[/]");
                    }

                    // Auto-copy to clipboard: OTP code if detected, otherwise message body
                    var otpCode = otp?.Code ?? classification.DetectedOtp;
                    if (otpCode != null)
                        ClipboardHelper.CopyToClipboard(otpCode);
                    else
                        ClipboardHelper.CopyToClipboard(msg.Body);
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

    private void DisplayMessage(SmsMessage msg, SmsClassification classification)
    {
        var timestamp = msg.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var label = _filter.GetMatchLabel(msg);
        var sourceInfo = label != null ? $" [cyan]({Markup.Escape(label)})[/]" : "";

        // Detect language and direction
        var language = LanguageDetector.Detect(msg.Body);
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

        // Show classification line
        if (classification.Category != SmsCategory.Unknown)
        {
            var catColor = classification.CategoryColor;
            var summary = !string.IsNullOrEmpty(classification.Summary)
                ? $" {Markup.Escape(classification.Summary)}"
                : "";
            AnsiConsole.MarkupLine(
                $"  [{catColor}][[{classification.Category.ToString().ToUpperInvariant()}]]{summary} ({classification.Confidence:P0})[/]");
        }
    }

    private static void DisplayOtp(OtpResult otp)
    {
        // OTP codes are always LTR (digits/alphanumeric)
        var code = RtlFormatter.ForceLtr(otp.Code);
        var color = otp.Confidence >= 0.8 ? "green" : otp.Confidence >= 0.6 ? "yellow" : "grey";
        var pct = $"{otp.Confidence * 100:F0}%";
        AnsiConsole.MarkupLine(
            $"  [{color}]>>> OTP: {Markup.Escape(code)}  " +
            $"(confidence: {pct}, {Markup.Escape(otp.PatternName)})[/]");
    }
}
