using SmsReader.Sms;

namespace SmsReader.Otp;

public sealed record OtpResult(
    string Code,
    double Confidence,
    string PatternName,
    SmsMessage SourceMessage);
