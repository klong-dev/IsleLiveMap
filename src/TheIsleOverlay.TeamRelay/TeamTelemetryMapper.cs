using TheIsleOverlay.Core;

namespace TheIsleOverlay.TeamRelay;

public static class TeamTelemetryMapper
{
    public static TeamTelemetryUpdate Create(
        TelemetrySnapshot? snapshot,
        long sequence,
        double? fallbackHeadingDegrees = null)
    {
        if (snapshot is not { Success: true, ServerOnline: true, PlayerOnline: true, Player: { } player })
        {
            return new TeamTelemetryUpdate
            {
                Sequence = sequence,
                Source = Trim(snapshot?.Source, 32)
            };
        }

        var exact = player.ExactVitals;
        var worldPoint = FinitePair(player.Location?.X, player.Location?.Y);
        var mapPoint = FinitePair(player.MapLocation?.Left, player.MapLocation?.Top);
        var heading = player.ExactMapHeadingDegrees ?? fallbackHeadingDegrees;
        if (heading is { } value)
        {
            heading = double.IsFinite(value) ? MapHeading.Normalize(value) : null;
        }

        return new TeamTelemetryUpdate
        {
            Sequence = sequence,
            Source = Trim(snapshot.Source, 32),
            ServerKey = Trim(player.Server, 128),
            ServerName = Trim(player.Server, 128),
            MapId = player.Location is not null || player.MapLocation is not null ? "gateway" : null,
            Species = Trim(player.Class, 64),
            HealthPercent = PercentOrNull(exact?.Health, exact?.MaxHealth, player.HealthPercent),
            HungerPercent = PercentOrNull(exact?.Hunger, exact?.MaxHunger, player.HungerPercent),
            ThirstPercent = PercentOrNull(exact?.Thirst, exact?.MaxThirst, player.ThirstPercent),
            WorldX = worldPoint?.First,
            WorldY = worldPoint?.Second,
            MapLeft = mapPoint?.First,
            MapTop = mapPoint?.Second,
            HeadingDegrees = heading
        };
    }

    private static double? PercentOrNull(double? current, double? maximum, double? fallback)
    {
        if (current is null && maximum is null && fallback is null)
        {
            return null;
        }

        return VitalMath.Percent(current, maximum, fallback);
    }

    private static (double First, double Second)? FinitePair(double? first, double? second) =>
        first is { } firstValue
        && second is { } secondValue
        && double.IsFinite(firstValue)
        && double.IsFinite(secondValue)
            ? (firstValue, secondValue)
            : null;

    private static string? Trim(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
