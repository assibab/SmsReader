namespace SmsReader.Language;

public static class RtlFormatter
{
    // Unicode directional markers
    private const char RLM = '\u200F';  // Right-to-Left Mark
    private const char LRM = '\u200E';  // Left-to-Right Mark
    private const char RLE = '\u202B';  // Right-to-Left Embedding
    private const char LRE = '\u202A';  // Left-to-Right Embedding
    private const char PDF = '\u202C';  // Pop Directional Formatting
    private const char RLO = '\u202E';  // Right-to-Left Override
    private const char RLI = '\u2067';  // Right-to-Left Isolate
    private const char LRI = '\u2066';  // Left-to-Right Isolate
    private const char PDI = '\u2069';  // Pop Directional Isolate

    /// <summary>
    /// Wraps text with appropriate Unicode directional markers based on detected language.
    /// For RTL text, adds RLI/PDI isolates around the text so terminal renderers
    /// handle the bidirectional text correctly.
    /// </summary>
    public static string Format(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var direction = LanguageDetector.GetDirection(text);

        return direction == TextDirection.RightToLeft
            ? FormatRtl(text)
            : text;
    }

    /// <summary>
    /// Formats RTL text by processing each line individually and adding directional markers.
    /// </summary>
    private static string FormatRtl(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Use Right-to-Left Isolate to wrap each line.
            // This tells the Unicode Bidirectional Algorithm to treat
            // the content as an RTL paragraph without affecting surrounding text.
            lines[i] = $"{RLI}{line}{PDI}";
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Wraps a specific string as RTL regardless of detection.
    /// Useful for known-Hebrew fields like sender names.
    /// </summary>
    public static string ForceRtl(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return $"{RLI}{text}{PDI}";
    }

    /// <summary>
    /// Wraps a specific string as LTR regardless of detection.
    /// Useful for ensuring numbers, timestamps, and codes render correctly
    /// within an RTL context.
    /// </summary>
    public static string ForceLtr(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return $"{LRI}{text}{PDI}";
    }

    /// <summary>
    /// Returns a language label string for display purposes.
    /// </summary>
    public static string GetLanguageTag(DetectedLanguage language) => language switch
    {
        DetectedLanguage.Hebrew => "he",
        DetectedLanguage.Arabic => "ar",
        _ => "en"
    };
}
