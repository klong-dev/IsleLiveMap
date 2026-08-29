using System.Globalization;

namespace TheIsleOverlay.Core;

public static class CreatureMarkerLabelFormatter
{
    public static string Format(string speciesShortName, double? massKg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speciesShortName);
        var species = speciesShortName.Trim();
        if (!IsUsableMass(massKg))
        {
            return species;
        }

        var mass = massKg!.Value;
        var compact = mass >= 1_000d
            ? FormatNumber(mass / 1_000d) + "T"
            : FormatNumber(mass) + "K";
        return $"{species} {compact}";
    }

    private static string FormatNumber(double value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return rounded.ToString(
            Math.Abs(rounded - Math.Round(rounded)) < 0.000_001d ? "0" : "0.#",
            CultureInfo.InvariantCulture);
    }

    private static bool IsUsableMass(double? massKg) =>
        massKg is > 0d and < 100_000d && double.IsFinite(massKg.Value);
}
