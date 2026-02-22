# SmsReader

A .NET console app that connects to an Android phone over ADB Wi-Fi and reads SMS messages in real-time. Supports OTP extraction, source filtering, and Hebrew RTL text.

## Features

- **ADB over Wi-Fi** — No USB cable needed after initial setup
- **Real-time monitoring** — Polls for new SMS every 5 seconds
- **OTP extraction** — Detects verification codes with confidence scoring (English + Hebrew patterns)
- **Source filtering** — Include/exclude by phone number or sender name (Exact, Contains, Regex)
- **Hebrew & RTL support** — Automatic language detection with Unicode directional formatting
- **Rich console output** — Formatted tables and panels via Spectre.Console

## Prerequisites

- .NET 8.0+ SDK
- Android phone with USB debugging enabled
- ADB (Android Debug Bridge) — included in `platform-tools/` or install via `scoop install adb`

## Setup

### 1. Enable USB debugging on your phone

Settings → Developer Options → USB Debugging → On

> If you don't see Developer Options, go to Settings → About Phone and tap "Build Number" 7 times.

### 2. First-time ADB pairing (USB required once)

Connect your phone via USB, then run:

```bash
adb tcpip 5555
```

This switches the phone to TCP/IP mode. You can disconnect USB after this.

### 3. Find your phone's IP

Settings → Wi-Fi → tap your network → IP address (e.g. `192.168.1.100`)

### 4. Configure

Edit `src/SmsReader/appsettings.json`:

```json
{
  "Adb": {
    "Path": "adb",
    "DeviceIp": "192.168.1.100",
    "Port": 5555
  }
}
```

If ADB isn't in your PATH, use the full path:

```json
"Path": "C:\\path\\to\\platform-tools\\adb.exe"
```

### 5. Run

```bash
# Monitor mode (default) — watch for new SMS in real-time
dotnet run --project src/SmsReader

# List mode — display existing SMS messages
dotnet run --project src/SmsReader -- --mode=list
```

## Configuration

All settings are in `src/SmsReader/appsettings.json`. You can also override via command line:

```bash
dotnet run --project src/SmsReader -- --Adb:DeviceIp=192.168.1.50
```

### Source Filtering

Filter SMS by sender. Modes: `None` (show all), `Include` (whitelist), `Exclude` (blacklist).

```json
"Filters": {
  "Mode": "Include",
  "Sources": [
    { "Value": "+1234567890", "MatchType": "Exact", "Label": "My Bank" },
    { "Value": "AMAZON",      "MatchType": "Exact", "Label": "Amazon" },
    { "Value": "verif",       "MatchType": "Contains", "Label": "Verification services" },
    { "Value": "^\\+44",      "MatchType": "Regex", "Label": "UK numbers" }
  ]
}
```

### OTP Settings

```json
"Otp": {
  "Enabled": true,
  "HighlightThreshold": 0.7
}
```

### Monitoring

```json
"Monitoring": {
  "PollingIntervalMs": 5000,
  "MaxMessagesToDisplay": 50
}
```

## Project Structure

```
src/SmsReader/
├── Program.cs                    # Entry point, CLI modes
├── appsettings.json              # Configuration
├── Adb/
│   ├── AdbClient.cs              # ADB process wrapper
│   └── AdbConnectionManager.cs   # Wi-Fi connection lifecycle
├── Sms/
│   ├── SmsMessage.cs             # Data model
│   ├── SmsReader.cs              # ADB content provider queries
│   └── SmsParser.cs              # Raw output parser
├── Otp/
│   ├── OtpExtractor.cs           # Regex-based OTP detection
│   └── OtpResult.cs              # Extraction result
├── Filtering/
│   └── SourceFilter.cs           # Include/exclude filtering
├── Language/
│   ├── LanguageDetector.cs       # Hebrew/Arabic detection
│   └── RtlFormatter.cs           # RTL Unicode formatting
└── Monitoring/
    └── SmsMonitor.cs             # Real-time polling loop
```

## Notes

- The phone remembers TCP/IP mode until reboot. After a reboot, reconnect USB and run `adb tcpip 5555` again.
- ADB shell has read access to `content://sms` on most Android versions without root.
- Some manufacturer ROMs may restrict SMS content provider access from ADB.
- For best Hebrew/RTL rendering, use Windows Terminal rather than legacy `cmd.exe`.
