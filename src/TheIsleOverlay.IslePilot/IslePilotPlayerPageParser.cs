using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace TheIsleOverlay.IslePilot;

public sealed record IslePilotPlayerPage(
    string? Species,
    bool Online,
    double? GrowthPercent,
    double? Health,
    double? MaxHealth,
    double? Hunger,
    double? MaxHunger,
    double? Thirst,
    double? MaxThirst);

public static partial class IslePilotPlayerPageParser
{
    public static IslePilotPlayerPage Parse(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        var species = Decode(SpeciesRegex().Match(html).Groups["value"].Value);
        var growth = ParsePercent(ReadLabelValue(html, "Growth"));
        var (health, maxHealth) = ParsePair(ReadLabelValue(html, "Health"));
        var (hunger, maxHunger) = ParsePair(ReadLabelValue(html, "Hunger"));
        var (thirst, maxThirst) = ParsePair(ReadLabelValue(html, "Thirst"));

        return new IslePilotPlayerPage(
            EmptyToNull(species),
            OnlineRegex().IsMatch(html),
            growth,
            health,
            maxHealth,
            hunger,
            maxHunger,
            thirst,
            maxThirst);
    }

    private static string? ReadLabelValue(string html, string label)
    {
        var pattern = $@">\s*{Regex.Escape(label)}\s*</span>\s*<span\b[^>]*>\s*(?<value>[^<]+?)\s*</span>";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return match.Success ? Decode(match.Groups["value"].Value) : null;
    }

    private static (double? Current, double? Maximum) ParsePair(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, null);
        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return (null, null);
        return (ParseNumber(parts[0]), ParseNumber(parts[1]));
    }

    private static double? ParsePercent(string? value) =>
        ParseNumber(value?.Replace("%", string.Empty, StringComparison.Ordinal));

    private static double? ParseNumber(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string Decode(string value) => WebUtility.HtmlDecode(value).Trim();

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex("<h1\\b[^>]*>\\s*(?<value>[^<]+?)\\s*</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SpeciesRegex();

    [GeneratedRegex(">\\s*Online\\s*</span>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnlineRegex();
}
