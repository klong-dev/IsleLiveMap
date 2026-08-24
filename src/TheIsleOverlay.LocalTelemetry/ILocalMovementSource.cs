namespace TheIsleOverlay.LocalTelemetry;

public interface ILocalMovementSource : IAsyncDisposable
{
    IAsyncEnumerable<LocalMovementObservation> WatchAsync(
        CancellationToken cancellationToken = default);
}

public readonly record struct LocalMovementObservation(
    DateTimeOffset ObservedAt,
    UnrealMovementCandidate Movement);
