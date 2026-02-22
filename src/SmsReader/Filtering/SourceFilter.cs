using System.Text.RegularExpressions;
using SmsReader.Configuration;
using SmsReader.Sms;

namespace SmsReader.Filtering;

public sealed class SourceFilter
{
    private readonly FilterSettings _settings;

    public SourceFilter(FilterSettings settings)
    {
        _settings = settings;
    }

    public bool ShouldInclude(SmsMessage message)
    {
        if (_settings.Mode.Equals("None", StringComparison.OrdinalIgnoreCase))
            return true;

        bool matchesAny = _settings.Sources.Any(s => Matches(message.Address, s));

        return _settings.Mode.Equals("Include", StringComparison.OrdinalIgnoreCase)
            ? matchesAny
            : !matchesAny;
    }

    public string? GetMatchLabel(SmsMessage message)
    {
        return _settings.Sources
            .FirstOrDefault(s => Matches(message.Address, s))
            ?.Label;
    }

    private static bool Matches(string address, SourceEntry entry) => entry.MatchType switch
    {
        "Exact" => address.Equals(entry.Value, StringComparison.OrdinalIgnoreCase),
        "Contains" => address.Contains(entry.Value, StringComparison.OrdinalIgnoreCase),
        "Regex" => Regex.IsMatch(address, entry.Value, RegexOptions.IgnoreCase),
        _ => false
    };
}
