namespace TheIsleOverlay.App;

internal static class TeamTelemetryPublishPolicy
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan MaximumSnapshotSilence = TimeSpan.FromSeconds(10);

    internal static bool ShouldPublish(
        bool hasSnapshot,
        long version,
        long publishedVersion,
        DateTimeOffset receivedAt,
        DateTimeOffset lastPublishedAt,
        DateTimeOffset now)
    {
        if (version != publishedVersion)
            return true;
        if (!hasSnapshot
            || receivedAt == default
            || now < receivedAt
            || now - receivedAt > MaximumSnapshotSilence)
            return false;
        return lastPublishedAt == default
               || now - lastPublishedAt >= RefreshInterval;
    }
}
