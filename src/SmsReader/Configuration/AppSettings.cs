namespace SmsReader.Configuration;

public sealed class AppSettings
{
    public AdbSettings Adb { get; set; } = new();
    public MonitoringSettings Monitoring { get; set; } = new();
    public FilterSettings Filters { get; set; } = new();
    public OtpSettings Otp { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}

public sealed class AdbSettings
{
    public string Path { get; set; } = "adb";
    public string DeviceIp { get; set; } = "";
    public int Port { get; set; } = 5555;
    public int CommandTimeoutMs { get; set; } = 10000;
}

public sealed class MonitoringSettings
{
    public int PollingIntervalMs { get; set; } = 5000;
    public int MaxMessagesToDisplay { get; set; } = 50;
}

public sealed class FilterSettings
{
    public string Mode { get; set; } = "None";
    public List<SourceEntry> Sources { get; set; } = [];
}

public sealed class SourceEntry
{
    public string Value { get; set; } = "";
    public string MatchType { get; set; } = "Exact";
    public string Label { get; set; } = "";
}

public sealed class OtpSettings
{
    public bool Enabled { get; set; } = true;
    public double HighlightThreshold { get; set; } = 0.7;
}

public sealed class AgentSettings
{
    public bool Enabled { get; set; } = false;
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 256;
    public bool AutoCopyOtp { get; set; } = true;
    public double AutoCopyMinConfidence { get; set; } = 0.85;
}
