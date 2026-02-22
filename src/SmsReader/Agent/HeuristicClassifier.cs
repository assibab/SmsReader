using SmsReader.Otp;

namespace SmsReader.Agent;

public static class HeuristicClassifier
{
    private static readonly string[] MarketingKeywords =
        ["sale", "offer", "discount", "unsubscribe", "promo", "deal", "coupon", "הנחה", "מבצע"];

    private static readonly string[] DeliveryKeywords =
        ["shipped", "delivered", "tracking", "package", "courier", "משלוח", "חבילה"];

    private static readonly string[] FinancialKeywords =
        ["transaction", "debited", "credited", "payment", "balance", "עסקה", "תשלום", "חיוב"];

    private static readonly string[] SpamKeywords =
        ["winner", "won", "lottery", "claim your", "free money", "זכית"];

    private static readonly string[] UrgentKeywords =
        ["urgent", "immediate", "alert", "warning", "דחוף", "אזהרה"];

    public static SmsClassification Classify(string body, OtpResult? otp)
    {
        if (otp != null && otp.Confidence >= 0.7)
        {
            return new SmsClassification(
                SmsCategory.Otp,
                $"OTP code: {otp.Code}",
                otp.Confidence);
        }

        var lower = body.ToLowerInvariant();

        if (MatchesAny(lower, body, SpamKeywords))
            return new SmsClassification(SmsCategory.Spam, "Suspected spam", 0.6);

        if (MatchesAny(lower, body, UrgentKeywords))
            return new SmsClassification(SmsCategory.Urgent, "Urgent message", 0.6);

        if (MatchesAny(lower, body, FinancialKeywords))
            return new SmsClassification(SmsCategory.Financial, "Financial notification", 0.6);

        if (MatchesAny(lower, body, DeliveryKeywords))
            return new SmsClassification(SmsCategory.Delivery, "Delivery update", 0.6);

        if (MatchesAny(lower, body, MarketingKeywords))
            return new SmsClassification(SmsCategory.Marketing, "Marketing message", 0.6);

        return new SmsClassification(SmsCategory.Unknown, "", 0.3);
    }

    private static bool MatchesAny(string lowerBody, string originalBody, string[] keywords)
    {
        foreach (var kw in keywords)
        {
            if (lowerBody.Contains(kw, StringComparison.Ordinal) ||
                originalBody.Contains(kw, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
