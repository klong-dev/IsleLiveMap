namespace TheIsleOverlay.Core;

public interface ITelemetryProvider
{
    Task<TelemetrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
