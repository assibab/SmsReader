namespace SmsReader.Agent;

public enum SmsCategory
{
    Unknown,
    Otp,
    Marketing,
    Personal,
    Financial,
    Delivery,
    Urgent,
    Spam
}

public sealed record SmsClassification(
    SmsCategory Category,
    string Summary,
    double Confidence,
    string? DetectedOtp = null)
{
    /// <summary>
    /// Returns the Spectre.Console color markup name for this category.
    /// </summary>
    public string CategoryColor => Category switch
    {
        SmsCategory.Otp => "green",
        SmsCategory.Spam => "red",
        SmsCategory.Marketing => "yellow",
        SmsCategory.Personal => "blue",
        SmsCategory.Delivery => "cyan",
        SmsCategory.Financial => "purple",
        SmsCategory.Urgent => "bold red",
        _ => "grey"
    };
}
