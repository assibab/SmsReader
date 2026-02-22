using System.Text.RegularExpressions;
using SmsReader.Sms;

namespace SmsReader.Otp;

public sealed class OtpExtractor
{
    private static readonly (string Name, string Pattern, double Confidence)[] OtpPatterns =
    [
        // Explicit OTP/code/PIN labels followed by digits (English + Hebrew)
        ("Labeled OTP 6-digit",  @"(?:OTP|otp|code|Code|CODE|PIN|pin|passcode|קוד|סיסמה)[:\s-]*(\d{6})\b",  0.95),
        ("Labeled OTP 4-digit",  @"(?:OTP|otp|code|Code|CODE|PIN|pin|passcode|קוד|סיסמה)[:\s-]*(\d{4})\b",  0.90),
        ("Labeled OTP 8-digit",  @"(?:OTP|otp|code|Code|CODE|PIN|pin|passcode|קוד|סיסמה)[:\s-]*(\d{8})\b",  0.90),

        // Hebrew verification patterns (e.g., "קוד האימות לגוביזיט 1928")
        ("Hebrew OTP 6-digit",  @"קוד[\s\p{L}]*\s(\d{6})\b",  0.90),
        ("Hebrew OTP 4-digit",  @"קוד[\s\p{L}]*\s(\d{4})\b",  0.85),

        // "is <digits>" pattern (e.g., "Your verification code is 123456")
        ("Is-pattern 6-digit",   @"\bis\s+(\d{6})\b",  0.85),
        ("Is-pattern 4-digit",   @"\bis\s+(\d{4})\b",  0.80),

        // Digits followed by label (e.g., "Use 482913 as your code")
        ("Postfix label 6-digit", @"\b(\d{6})\s+(?:is your|as your)",  0.85),
        ("Postfix label 4-digit", @"\b(\d{4})\s+(?:is your|as your)",  0.80),

        // Alphanumeric codes (e.g., "A1B2C3")
        ("Alphanumeric 6-char",  @"(?:code|Code|OTP)[:\s-]*([A-Z0-9]{6})\b",  0.75),

        // Standalone 6-digit number in a short message (low confidence)
        ("Standalone 6-digit",   @"\b(\d{6})\b",  0.50),
    ];

    public OtpResult? Extract(SmsMessage message)
    {
        foreach (var (name, pattern, baseConfidence) in OtpPatterns)
        {
            var match = Regex.Match(message.Body, pattern);
            if (!match.Success) continue;

            double confidence = baseConfidence;

            // Boost: message contains verification-related keywords
            if (Regex.IsMatch(message.Body, @"(?i)verif|authent|confirm|login|sign.in|2fa|two.factor|אימות|אישור"))
                confidence = Math.Min(1.0, confidence + 0.10);

            // Boost: message is short (< 160 chars) — likely automated
            if (message.Body.Length < 160)
                confidence = Math.Min(1.0, confidence + 0.05);

            // Boost: sender is alphanumeric (not a phone number) — likely a service
            if (!Regex.IsMatch(message.Address, @"^\+?\d+$"))
                confidence = Math.Min(1.0, confidence + 0.05);

            // Penalize: message is very long (likely not just an OTP)
            if (message.Body.Length > 300)
                confidence = Math.Max(0.1, confidence - 0.15);

            return new OtpResult(match.Groups[1].Value, confidence, name, message);
        }

        return null;
    }
}
