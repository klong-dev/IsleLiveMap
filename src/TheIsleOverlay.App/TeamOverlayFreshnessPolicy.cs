namespace TheIsleOverlay.App;

internal static class TeamOverlayFreshnessPolicy
{
    internal static readonly TimeSpan MarkerLifetime = TimeSpan.FromSeconds(15);

    internal static bool IsFresh(DateTimeOffset? updatedAt, DateTimeOffset now) =>
        updatedAt is { } value
        && value != default
        && now >= value
        && now - value <= MarkerLifetime;
}
