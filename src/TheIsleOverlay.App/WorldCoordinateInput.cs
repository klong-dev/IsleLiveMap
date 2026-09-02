using System.Globalization;
using System.Text.RegularExpressions;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal static class WorldCoordinateInput
{
    private static readonly Regex NumberPattern = new(
        @"(?<![\d.])[-+]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?(?![\d.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? input, out WorldLocation location)
    {
        location = new WorldLocation();
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var matches = NumberPattern.Matches(input);
        if (matches.Count != 3 || !ContainsOnlyCoordinateSeparators(input, matches))
        {
            return false;
        }

        Span<double> values = stackalloc double[3];
        for (var index = 0; index < matches.Count; index++)
        {
            var normalized = matches[index].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (!double.TryParse(
                    normalized,
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out values[index])
                || !double.IsFinite(values[index]))
            {
                return false;
            }
        }

        location = new WorldLocation
        {
            X = values[0],
            Y = values[1],
            Z = values[2]
        };
        return true;
    }

    public static bool TryProjectToGateway(WorldLocation location, out MapPoint point)
    {
        ArgumentNullException.ThrowIfNull(location);
        point = default;
        if (!double.IsFinite(location.X)
            || !double.IsFinite(location.Y)
            || location.Z is not { } z
            || !double.IsFinite(z))
        {
            return false;
        }

        var projected = GatewayMapProjection.ProjectUnclamped(location);
        if (!double.IsFinite(projected.Left)
            || !double.IsFinite(projected.Top)
            || projected.Left is < 0d or > 1d
            || projected.Top is < 0d or > 1d)
        {
            return false;
        }

        point = projected;
        return true;
    }

    private static bool ContainsOnlyCoordinateSeparators(string input, MatchCollection matches)
    {
        var cursor = 0;
        foreach (Match match in matches)
        {
            if (!IsCoordinateSeparator(input.AsSpan(cursor, match.Index - cursor)))
            {
                return false;
            }
            cursor = match.Index + match.Length;
        }

        return IsCoordinateSeparator(input.AsSpan(cursor));
    }

    private static bool IsCoordinateSeparator(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character)
                && character is not ',' and not ';' and not ':' and not '=' and not '|'
                    and not '/' and not '(' and not ')' and not '[' and not ']'
                    and not '{' and not '}' and not 'x' and not 'X' and not 'y' and not 'Y'
                    and not 'z' and not 'Z')
            {
                return false;
            }
        }

        return true;
    }
}
