namespace TheIsleOverlay.Core;

public interface IRemotePlayerTelemetrySource : IAsyncDisposable
{
    IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
        CancellationToken cancellationToken = default);
}

public sealed record RemotePlayerTelemetryFrame(
    long Sequence,
    DateTimeOffset ObservedAt,
    string? ServerEndpoint,
    WorldLocation LocalLocation,
    double MapHeadingDegrees,
    IReadOnlyList<VerifiedRemoteEntityTelemetry> RemoteEntities,
    string? LocalSpeciesId = null,
    string? LocalSpeciesShortName = null);

public enum RemoteEntityKind
{
    Player = 1,
    Ai = 2
}

public enum CreatureDiet
{
    Unknown = 0,
    Carnivore = 1,
    Herbivore = 2,
    Omnivore = 3
}

public sealed record VerifiedRemoteEntityTelemetry(
    long TrackId,
    RemoteEntityKind Kind,
    string? PlayerProofName,
    string SpeciesId,
    string SpeciesShortName,
    CreatureDiet Diet,
    double? MassKg,
    WorldLocation Location,
    double DistanceFromLocal,
    int ConfirmationHits,
    DateTimeOffset ObservedAt);
