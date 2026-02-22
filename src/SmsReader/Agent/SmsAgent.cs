using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmsReader.Configuration;
using SmsReader.Otp;
using Spectre.Console;

namespace SmsReader.Agent;

public sealed class SmsAgent : IDisposable
{
    private readonly AgentSettings _settings;
    private readonly HttpClient? _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public SmsAgent(AgentSettings settings)
    {
        _settings = settings;

        if (IsEnabled)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.anthropic.com/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _settings.ApiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
    }

    /// <summary>
    /// Classify an SMS message. Uses LLM when cost-gating allows, falls back to heuristic.
    /// </summary>
    public async Task<SmsClassification> ClassifyAsync(string body, string sender, OtpResult? otp)
    {
        // Cost gating: skip LLM when we can classify locally
        if (!ShouldCallLlm(body, otp))
            return HeuristicClassifier.Classify(body, otp);

        try
        {
            return await CallLlmAsync(body, sender, otp);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"  [grey]LLM error, using heuristic: {Markup.Escape(ex.Message)}[/]");
            return HeuristicClassifier.Classify(body, otp);
        }
    }

    private bool ShouldCallLlm(string body, OtpResult? otp)
    {
        if (!IsEnabled)
            return false;

        // High-confidence regex OTP — no need to call the LLM
        if (otp != null && otp.Confidence >= 0.9)
            return false;

        // Message too short to classify meaningfully
        if (body.Length < 10)
            return false;

        return true;
    }

    private async Task<SmsClassification> CallLlmAsync(string body, string sender, OtpResult? otp)
    {
        var prompt = BuildPrompt(body, sender, otp);

        var request = new
        {
            model = _settings.Model,
            max_tokens = _settings.MaxTokens,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var response = await _httpClient!.PostAsJsonAsync("v1/messages", request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        var textBlock = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        return ParseLlmResponse(textBlock, otp);
    }

    private static string BuildPrompt(string body, string sender, OtpResult? otp)
    {
        var otpHint = otp != null
            ? $"\nRegex already extracted OTP: {otp.Code} (confidence: {otp.Confidence:P0})"
            : "";

        return $$"""
            Classify this SMS. Respond with ONLY a JSON object, no markdown.
            {"category":"<otp|marketing|personal|financial|delivery|urgent|spam>","summary":"<1-line summary in the message's language>","confidence":<0.0-1.0>,"otp":"<code or null>"}

            From: {{sender}}{{otpHint}}
            Message: {{body}}
            """;
    }

    private static SmsClassification ParseLlmResponse(string text, OtpResult? existingOtp)
    {
        try
        {
            // Strip markdown fencing if present
            var json = text.Trim();
            if (json.StartsWith("```"))
            {
                var start = json.IndexOf('{');
                var end = json.LastIndexOf('}');
                if (start >= 0 && end > start)
                    json = json[start..(end + 1)];
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var categoryStr = root.GetProperty("category").GetString() ?? "unknown";
            var summary = root.GetProperty("summary").GetString() ?? "";
            var confidence = root.GetProperty("confidence").GetDouble();
            var otpCode = root.TryGetProperty("otp", out var otpProp) && otpProp.ValueKind == JsonValueKind.String
                ? otpProp.GetString()
                : null;

            var category = categoryStr.ToLowerInvariant() switch
            {
                "otp" => SmsCategory.Otp,
                "marketing" => SmsCategory.Marketing,
                "personal" => SmsCategory.Personal,
                "financial" => SmsCategory.Financial,
                "delivery" => SmsCategory.Delivery,
                "urgent" => SmsCategory.Urgent,
                "spam" => SmsCategory.Spam,
                _ => SmsCategory.Unknown
            };

            // If LLM found an OTP that regex missed, include it
            var detectedOtp = !string.IsNullOrWhiteSpace(otpCode) && otpCode != "null"
                ? otpCode
                : null;

            return new SmsClassification(category, summary, confidence, detectedOtp);
        }
        catch
        {
            // Failed to parse LLM response, fall back to heuristic
            return HeuristicClassifier.Classify("", existingOtp);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
