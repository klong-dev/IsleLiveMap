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

        target = new WorldLocation
        {
            X = values[0],
            Y = values[1],
            Z = values[2]
        };
        return true;
    }

    [GeneratedRegex(@"[-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex CoordinateNumber();
}
