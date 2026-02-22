using Microsoft.Extensions.Configuration;
using SmsReader.Adb;
using SmsReader.Configuration;
using SmsReader.Filtering;
using SmsReader.Monitoring;
using SmsReader.Otp;
using SmsReader.Language;
using SmsReader.Sms;
using Spectre.Console;

// Ensure UTF-8 output for Hebrew and other non-ASCII characters
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

// Parse args for --help early
if (args.Any(a => a is "--help" or "-h"))
{
    PrintHelp();
    return 0;
}

// Build configuration
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddCommandLine(args)
    .Build();

var settings = new AppSettings();
config.Bind(settings);

// Startup banner
AnsiConsole.Write(new FigletText("SMS Reader").Color(Color.Blue));
AnsiConsole.MarkupLine($"[grey]Device: {Markup.Escape(settings.Adb.DeviceIp)}:{settings.Adb.Port}[/]");
AnsiConsole.MarkupLine($"[grey]Filter mode: {Markup.Escape(settings.Filters.Mode)}[/]");
if (settings.Filters.Sources.Count > 0)
{
    foreach (var source in settings.Filters.Sources)
    {
        AnsiConsole.MarkupLine($"[grey]  - {Markup.Escape(source.Label)}: {Markup.Escape(source.Value)} ({Markup.Escape(source.MatchType)})[/]");
    }
}
AnsiConsole.MarkupLine($"[grey]OTP extraction: {(settings.Otp.Enabled ? "enabled" : "disabled")}[/]");
AnsiConsole.WriteLine();

// Validate device IP
if (string.IsNullOrWhiteSpace(settings.Adb.DeviceIp))
{
    AnsiConsole.MarkupLine("[red]Error: Device IP not configured.[/]");
    AnsiConsole.MarkupLine("[yellow]Set it in appsettings.json or pass --Adb:DeviceIp=<ip>[/]");
    return 1;
}

// Set up ADB
var deviceSerial = $"{settings.Adb.DeviceIp}:{settings.Adb.Port}";
var adbClient = new AdbClient(settings.Adb.Path, deviceSerial);
var connectionManager = new AdbConnectionManager(adbClient, settings.Adb);

// Connect to device
if (!await connectionManager.EnsureConnectedAsync())
{
    AnsiConsole.MarkupLine("[red]Could not connect to device. Ensure:[/]");
    AnsiConsole.MarkupLine("[yellow]  1. ADB is installed and in PATH[/]");
    AnsiConsole.MarkupLine("[yellow]  2. USB debugging is enabled on your phone[/]");
    AnsiConsole.MarkupLine("[yellow]  3. Run 'adb tcpip 5555' with USB connected first[/]");
    AnsiConsole.MarkupLine("[yellow]  4. Phone and PC are on the same Wi-Fi network[/]");
    AnsiConsole.MarkupLine($"[yellow]  5. Device IP is correct: {Markup.Escape(settings.Adb.DeviceIp)}[/]");
    return 1;
}

AnsiConsole.WriteLine();

// Build services
var fetcher = new SmsFetcher(adbClient);
var filter = new SourceFilter(settings.Filters);
var otpExtractor = new OtpExtractor();

// Determine mode
var mode = args.FirstOrDefault(a => a.StartsWith("--mode="))?.Split('=')[1] ?? "monitor";

// Set up Ctrl+C handling
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

switch (mode.ToLowerInvariant())
{
    case "list":
        await ListMessagesAsync(fetcher, filter, otpExtractor, settings);
        break;

    case "monitor":
    default:
        var monitor = new SmsMonitor(fetcher, filter, otpExtractor, settings.Monitoring, settings.Otp);
        await monitor.RunAsync(cts.Token);
        break;
}

return 0;

// --- Local functions ---

static async Task ListMessagesAsync(SmsFetcher fetcher, SourceFilter filter, OtpExtractor otpExtractor, AppSettings settings)
{
    AnsiConsole.MarkupLine("[grey]Fetching SMS messages...[/]");

    var raw = await fetcher.ReadInboxAsync();
    var messages = SmsParser.Parse(raw);

    if (messages.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No messages found.[/]");
        return;
    }

    var filtered = messages.Where(filter.ShouldInclude).Take(settings.Monitoring.MaxMessagesToDisplay).ToList();

    AnsiConsole.MarkupLine($"[green]Found {messages.Count} total messages, showing {filtered.Count} after filters.[/]");
    AnsiConsole.WriteLine();

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn(new TableColumn("[bold]Time[/]").NoWrap())
        .AddColumn(new TableColumn("[bold]From[/]").NoWrap())
        .AddColumn(new TableColumn("[bold]Message[/]"))
        .AddColumn(new TableColumn("[bold]Lang[/]").NoWrap())
        .AddColumn(new TableColumn("[bold]OTP[/]").NoWrap());

    foreach (var msg in filtered)
    {
        var timestamp = msg.Date.ToLocalTime().ToString("MM-dd HH:mm");
        var label = filter.GetMatchLabel(msg);

        // Apply RTL formatting to address if it contains Hebrew
        var addressDisplay = LanguageDetector.IsRtl(msg.Address)
            ? RtlFormatter.ForceRtl(msg.Address)
            : msg.Address;

        var from = label != null
            ? $"[blue]{Markup.Escape(addressDisplay)}[/]\n[cyan]{Markup.Escape(label)}[/]"
            : $"[blue]{Markup.Escape(addressDisplay)}[/]";

        // Detect language and apply RTL formatting to body
        var language = LanguageDetector.Detect(msg.Body);
        var langTag = RtlFormatter.GetLanguageTag(language);
        var truncatedBody = msg.Body.Length > 100 ? msg.Body[..100] + "..." : msg.Body;
        var formattedBody = RtlFormatter.Format(truncatedBody);
        var body = Markup.Escape(formattedBody);

        var langColor = language == DetectedLanguage.Hebrew ? "cyan" : "grey";
        var langDisplay = $"[{langColor}]{langTag}[/]";

        var otpText = "";
        if (settings.Otp.Enabled)
        {
            var otp = otpExtractor.Extract(msg);
            if (otp != null)
            {
                var code = RtlFormatter.ForceLtr(otp.Code);
                var color = otp.Confidence >= 0.8 ? "green" : otp.Confidence >= 0.6 ? "yellow" : "grey";
                otpText = $"[{color}]{Markup.Escape(code)}[/]\n[grey]{otp.Confidence:P0}[/]";
            }
        }

        table.AddRow(
            $"[grey]{timestamp}[/]",
            from,
            body,
            langDisplay,
            otpText);
    }

    AnsiConsole.Write(table);
}

static void PrintHelp()
{
    AnsiConsole.Write(new FigletText("SMS Reader").Color(Color.Blue));
    AnsiConsole.MarkupLine("[bold]Usage:[/] SmsReader [options]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Modes:[/]");
    AnsiConsole.MarkupLine("  --mode=monitor    [grey](default)[/] Watch for new SMS in real-time");
    AnsiConsole.MarkupLine("  --mode=list       List existing SMS messages");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Options:[/]");
    AnsiConsole.MarkupLine("  --Adb:DeviceIp=<ip>       Android device IP address");
    AnsiConsole.MarkupLine("  --Adb:Port=<port>         ADB port (default: 5555)");
    AnsiConsole.MarkupLine("  --Monitoring:PollingIntervalMs=<ms>  Poll interval (default: 5000)");
    AnsiConsole.MarkupLine("  --Filters:Mode=<mode>     None, Include, or Exclude");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Configuration:[/]");
    AnsiConsole.MarkupLine("  Edit appsettings.json to configure filters, OTP settings, and more.");
    AnsiConsole.MarkupLine("  See README.md for setup instructions.");
}
