namespace TheIsleOverlay.Core;

public interface IRemotePlayerTelemetrySource : IAsyncDisposable
{
    IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
        CancellationToken cancellationToken = default);
}

public interface IRemotePlayerTelemetryHealthSource
{
    RemotePlayerCaptureHealth CaptureHealth { get; }
}

public enum RemotePlayerCaptureState
{
    Starting = 0,
    WaitingForGame = 1,
    WaitingForPort = 2,
    OpeningAdapters = 3,
    Capturing = 4,
    Receiving = 5,
    Faulted = 6
}

public sealed record RemotePlayerCaptureHealth(
    RemotePlayerCaptureState State,
    bool GameProcessFound,
    int OwnedPortCount,
    int OpenedAdapterCount,
    long MatchedGamePackets,
    DateTimeOffset? LastGamePacketAt,
    string? Message = null)
{
    public static RemotePlayerCaptureHealth Starting { get; } = new(
        RemotePlayerCaptureState.Starting,
        false,
        0,
        0,
        0,
        null);
}

public sealed record RemotePlayerTelemetryFrame(
    long Sequence,
    DateTimeOffset ObservedAt,
    string? ServerEndpoint,
    WorldLocation LocalLocation,
    double MapHeadingDegrees,
    IReadOnlyList<VerifiedRemoteEntityTelemetry> RemoteEntities,
    string? LocalSpeciesId = null,
    string? LocalSpeciesShortName = null,
    // Capture time can trail wall clock while the stateful Iris decoder
    // drains a burst. Receipt time proves IPC liveness without pretending the
    // underlying GPS or entity samples were captured more recently.
    DateTimeOffset? ReceivedAt = null,
    RemotePlayerSyncState? PlayerSync = null);

public sealed record RemotePlayerSyncState(
    bool IsSynchronizing,
    int VerifiedPlayers,
    int ProvisionalPlayers,
    int CandidateActors,
    int SpeciesEvidenceActors,
    int LocatedActors,
    long QueueDroppedPackets,
    int QueueDepth);

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
    DateTimeOffset ObservedAt,
    bool IsProvisional = false);
