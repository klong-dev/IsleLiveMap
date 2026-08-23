using System.Globalization;
using System.Text.RegularExpressions;

namespace TheIsleOverlay.Core;

public static partial class NavigationTargetParser
{
    public static bool TryParse(string? text, out WorldLocation target)
    {
        target = new WorldLocation();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var matches = CoordinateNumber().Matches(text);
        if (matches.Count != 3)
        {
            return false;
        }

        var values = new double[3];
        for (var index = 0; index < matches.Count; index++)
        {
            var normalized = matches[index].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (!double.TryParse(
                    normalized,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out values[index]) ||
                !double.IsFinite(values[index]))
            {
                return false;
            }
        }

        var explicitlyUsesWorldAxes = ExplicitWorldAxes().IsMatch(text);
        target = new WorldLocation
        {
            // The Isle copies coordinates as Lat, Long, Alt. In Gateway data,
            // longitude is the east/west world X axis and latitude is world Y.
            // Preserve explicitly labelled X/Y input for developer diagnostics.
            X = explicitlyUsesWorldAxes ? values[0] : values[1],
            Y = explicitlyUsesWorldAxes ? values[1] : values[0],
            Z = values[2]
        };
        return true;
    }

    [GeneratedRegex(@"[-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinateNumber();

    [GeneratedRegex(@"\bX\b[\s\S]*\bY\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitWorldAxes();
}
