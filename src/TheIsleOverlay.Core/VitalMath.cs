namespace TheIsleOverlay.Core;

public static class VitalMath
{
    public static double Percent(double? current, double? maximum, double? fallback = null)
    {
        if (current is not null && maximum is > 0)
        {
            return Math.Clamp(current.Value / maximum.Value * 100d, 0d, 100d);
        }

        if (fallback is null)
        {
            return 0d;
        }

        var value = fallback.Value;
        if (value is >= 0d and <= 1d)
        {
            value *= 100d;
        }

        return Math.Clamp(value, 0d, 100d);
    }
}
