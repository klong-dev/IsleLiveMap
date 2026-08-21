namespace TheIsleOverlay.Core;

public interface ITelemetrySession : IAsyncDisposable
{
    IAsyncEnumerable<TelemetrySnapshot> WatchAsync(CancellationToken cancellationToken = default);
}
