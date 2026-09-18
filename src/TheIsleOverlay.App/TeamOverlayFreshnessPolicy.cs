namespace TheIsleOverlay.App;

internal static class TeamOverlayFreshnessPolicy
{
    internal static readonly TimeSpan MarkerLifetime = TimeSpan.FromSeconds(15);
    internal static bool IsFresh(
        DateTimeOffset? updatedAt,
        DateTimeOffset now)
    {
        if (updatedAt is not { } value || value == default)
            return false;
        if (value > now) return false;
        return now - value <= MarkerLifetime;
    }
}
