# SMS Agent — Architecture Summary

## Overview

SMS Agent is a .NET 9.0 console application that reads SMS messages from an Android phone over ADB (Wi-Fi), extracts OTP codes, classifies messages using an LLM or heuristic fallback, and auto-copies OTP codes to the clipboard.

It evolved from a deterministic **tool** (poll → parse → filter → display) into an **agent** with an optional LLM reasoning layer on top of the existing regex pipeline.

## Project Structure

```
src/SmsReader/
├── Program.cs                        # Entry point, wiring, startup banner, CLI modes
├── SmsReader.csproj                  # .NET 9.0, Spectre.Console, TextCopy
├── appsettings.json                  # All configuration
│
├── Adb/                              # Android Debug Bridge communication
│   ├── AdbClient.cs                  # Shell command execution via adb.exe
│   └── AdbConnectionManager.cs       # Connect/reconnect to device over TCP/IP
│
├── Sms/                              # SMS data layer
│   ├── SmsMessage.cs                 # Record: Id, Address, Body, Date, Type
│   ├── SmsParser.cs                  # Parses ADB `content query` output into SmsMessage list
│   └── SmsReader.cs (SmsFetcher)     # Queries content://sms via ADB shell
│
├── Filtering/
│   └── SourceFilter.cs               # Include/Exclude messages by sender (Exact, Contains, Regex)
│
├── Language/
│   ├── LanguageDetector.cs           # Detects Hebrew/Arabic/Other from Unicode ranges
│   └── RtlFormatter.cs              # Wraps RTL text in Unicode isolate markers
│
├── Otp/
│   ├── OtpExtractor.cs              # 11 regex patterns with confidence scoring + boosts
│   └── OtpResult.cs                 # Record: Code, Confidence, PatternName, SourceMessage
│
├── Monitoring/
│   └── SmsMonitor.cs                # Real-time polling loop with enriched display pipeline
│
├── Agent/                            # NEW — LLM reasoning layer
│   ├── SmsClassification.cs          # SmsCategory enum + classification record
│   ├── SmsAgent.cs                   # Claude API client, cost gating, prompt builder
│   ├── HeuristicClassifier.cs        # Keyword-based fallback classifier
│   └── ClipboardHelper.cs            # Auto-copy OTP to system clipboard
│
└── Configuration/
    └── AppSettings.cs                # All settings classes including AgentSettings
```

## Processing Pipeline

```
ADB Poll → Parse → Source Filter → Regex OTP → Classify → Enriched Display → Auto-Copy OTP
                                       │              │
                                       │         ┌────┴────┐
                                       │         │ LLM on? │
                                       │         └────┬────┘
                                       │          yes │ no
                                       │         ┌────┴────────┐
                                       │         │ Cost Gate?   │
                                       │         └────┬────────┘
                                       │      pass │     skip
                                       │      ┌────┴──┐  ┌──┴───────────┐
                                       │      │ Claude │  │ Heuristic    │
                                       │      │  API   │  │ (keywords)   │
                                       │      └────┬──┘  └──┬───────────┘
                                       │           └────┬────┘
                                       │                ▼
                                       │         SmsClassification
                                       │         (category, summary,
                                       │          confidence, otp?)
                                       ▼
                                   OtpResult
```

## Program.cs — Entry Point

**What it does:**
1. Configures UTF-8 encoding for Hebrew/Arabic support
2. Loads settings from `appsettings.json` + command-line overrides
3. Displays startup banner with device info, filter mode, agent status
4. Connects to Android device via ADB over TCP/IP
5. Builds all services: `SmsFetcher`, `SourceFilter`, `OtpExtractor`, `SmsAgent`
6. Routes to one of two modes:

| Mode | Command | Description |
|------|---------|-------------|
| **monitor** (default) | `--mode=monitor` | Real-time polling loop, shows new messages as they arrive |
| **list** | `--mode=list` | One-shot table of existing messages with Category column |

**Key CLI options:**
```
--Adb:DeviceIp=<ip>       Phone IP address
--Adb:Port=<port>         ADB port (default: 5555)
--Filters:Mode=<mode>     None, Include, or Exclude
--Agent:Enabled=true       Enable LLM classification
--Agent:ApiKey=<key>       Anthropic API key
```

## Agent Module

### SmsClassification.cs

Defines the classification output:

```
SmsCategory enum: Unknown, Otp, Marketing, Personal, Financial, Delivery, Urgent, Spam
```

Each category maps to a console color:
| Category | Color |
|----------|-------|
| Otp | Green |
| Spam | Red |
| Marketing | Yellow |
| Personal | Blue |
| Delivery | Cyan |
| Financial | Purple |
| Urgent | Bold Red |

### SmsAgent.cs — Claude API Integration

The core agent class. Manages an `HttpClient` pointed at `https://api.anthropic.com/v1/messages`.

**Cost Gating** (`ShouldCallLlm`): Most messages never hit the API.

| Condition | Action |
|-----------|--------|
| Agent disabled or no API key | Skip LLM |
| Regex OTP confidence >= 0.9 | Skip LLM (clearly an OTP) |
| Message body < 10 chars | Skip LLM (nothing to classify) |
| Otherwise | Call LLM |

**LLM Prompt** (~200 input tokens):
```
Classify this SMS. Respond with ONLY a JSON object, no markdown.
{"category":"<otp|marketing|personal|financial|delivery|urgent|spam>",
 "summary":"<1-line summary in the message's language>",
 "confidence":<0.0-1.0>,
 "otp":"<code or null>"}

From: <sender>
Message: <body>
```

**Response parsing:**
- Strips markdown fencing if present
- Parses JSON into `SmsClassification`
- Falls back to `HeuristicClassifier` on any parse failure

**Fallback chain:** LLM error → Heuristic. Always returns a classification, never throws.

### HeuristicClassifier.cs — Keyword Fallback

Used when the LLM is disabled or skipped by cost gating. Matches keywords in English and Hebrew:

| Keywords | Category |
|----------|----------|
| sale, offer, discount, unsubscribe, promo | Marketing |
| shipped, delivered, tracking, package | Delivery |
| transaction, debited, credited, payment | Financial |
| winner, won, lottery, claim your | Spam |
| urgent, immediate, alert, warning | Urgent |
| Regex OTP with confidence >= 0.7 | Otp |
| No match | Unknown (30% confidence) |

### ClipboardHelper.cs — Auto-Copy

Wraps `TextCopy.ClipboardService.SetText()`. Automatically copies any detected OTP code to the system clipboard. Gracefully handles clipboard unavailability.

## SmsMonitor.cs — Real-Time Pipeline

The monitor loop runs every 5 seconds (configurable). For each new message:

1. **Regex OTP extraction** — fast path, ~11 patterns with confidence scoring
2. **Classification** — `SmsAgent.ClassifyAsync()` (LLM or heuristic)
3. **Enriched display** — message panel + colored category tag + summary
4. **OTP display** — if regex found one, show code + confidence + pattern name
5. **LLM OTP fallback** — if regex missed but LLM found an OTP, display it
6. **Auto-copy** — any detected OTP is automatically copied to clipboard

### Console Output Example

```
2026-02-22 18:27:08  GoVisit (Darkon)  [he]
╭─ RECEIVED ────────────────────────────────────────╮
│ קוד האימות לגוביזיט 1928.קוד זה תקף ל-5 דקות     │
╰───────────────────────────────────────────────────╯
  [OTP] GoVisit verification code (95%)
  >>> OTP: 1928 (confidence: 100%, Hebrew OTP 4-digit)
  >> Copied to clipboard!
```

## Configuration Reference

```json
{
  "Adb": {
    "Path": "path/to/adb.exe",
    "DeviceIp": "192.168.10.134",
    "Port": 5555,
    "CommandTimeoutMs": 10000
  },
  "Monitoring": {
    "PollingIntervalMs": 5000,
    "MaxMessagesToDisplay": 50
  },
  "Filters": {
    "Mode": "None",
    "Sources": [
      { "Value": "GoVisit", "MatchType": "Exact", "Label": "Darkon" }
    ]
  },
  "Otp": {
    "Enabled": true,
    "HighlightThreshold": 0.7
  },
  "Agent": {
    "Enabled": false,
    "ApiKey": "",
    "Model": "claude-sonnet-4-20250514",
    "MaxTokens": 256,
    "AutoCopyOtp": true,
    "AutoCopyMinConfidence": 0.85
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Agent:Enabled` | `false` | Enable LLM classification |
| `Agent:ApiKey` | `""` | Anthropic API key (required when enabled) |
| `Agent:Model` | `claude-sonnet-4-20250514` | Claude model to use |
| `Agent:MaxTokens` | `256` | Max response tokens per API call |
| `Agent:AutoCopyOtp` | `true` | Auto-copy detected OTP to clipboard |
| `Agent:AutoCopyMinConfidence` | `0.85` | Minimum confidence to trigger auto-copy |

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.Extensions.Configuration | 8.0.0 | Settings binding |
| Spectre.Console | 0.49.1 | Rich console output (tables, panels, colors) |
| TextCopy | 6.2.1 | Cross-platform clipboard access |
