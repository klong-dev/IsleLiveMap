namespace TheIsleOverlay.App;

internal static class TeamTelemetryPublishPolicy
{
    // Keep unchanged snapshots alive without creating a high-frequency relay stream.
    // New telemetry versions still publish as soon as the coordinator observes them.
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
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
