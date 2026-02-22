using System.Globalization;

namespace SmsReader.Language;

public enum TextDirection
{
    LeftToRight,
    RightToLeft
}

public enum DetectedLanguage
{
    Hebrew,
    Arabic,
    Other
}

public static class LanguageDetector
{
    // Unicode ranges for RTL scripts
    private const int HebrewStart = 0x0590;
    private const int HebrewEnd = 0x05FF;
    private const int ArabicStart = 0x0600;
    private const int ArabicEnd = 0x06FF;

    public static DetectedLanguage Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DetectedLanguage.Other;

        int hebrewCount = 0;
        int arabicCount = 0;
        int latinCount = 0;
        int totalLetters = 0;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            foreach (var ch in element)
            {
                if (!char.IsLetter(ch))
                    continue;

                totalLetters++;
                int codePoint = ch;

                if (codePoint >= HebrewStart && codePoint <= HebrewEnd)
                    hebrewCount++;
                else if (codePoint >= ArabicStart && codePoint <= ArabicEnd)
                    arabicCount++;
                else if (codePoint < 0x0250) // Basic Latin + Latin Extended
                    latinCount++;
            }
        }

        if (totalLetters == 0)
            return DetectedLanguage.Other;

        // A text is considered Hebrew/Arabic if the majority of letter characters
        // are from that script. This handles mixed Hebrew+English messages well.
        double hebrewRatio = (double)hebrewCount / totalLetters;
        double arabicRatio = (double)arabicCount / totalLetters;

        if (hebrewRatio > 0.3)
            return DetectedLanguage.Hebrew;

        if (arabicRatio > 0.3)
            return DetectedLanguage.Arabic;

        return DetectedLanguage.Other;
    }

    public static TextDirection GetDirection(string text)
    {
        var lang = Detect(text);
        return lang is DetectedLanguage.Hebrew or DetectedLanguage.Arabic
            ? TextDirection.RightToLeft
            : TextDirection.LeftToRight;
    }

    public static TextDirection GetDirection(DetectedLanguage language)
    {
        return language is DetectedLanguage.Hebrew or DetectedLanguage.Arabic
            ? TextDirection.RightToLeft
            : TextDirection.LeftToRight;
    }

    public static bool IsRtl(string text) => GetDirection(text) == TextDirection.RightToLeft;
}
