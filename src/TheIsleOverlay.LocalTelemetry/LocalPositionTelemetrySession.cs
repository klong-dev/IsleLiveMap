using System.Threading.Channels;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

public sealed class LocalPositionTelemetrySession : ITelemetrySession
{
    private static readonly TimeSpan FreshnessCheckInterval = TimeSpan.FromSeconds(1);

    private readonly ITelemetrySession? _remoteSession;
    private readonly ILocalMovementSource _localSource;
    private readonly IRemotePlayerTelemetrySource? _remotePlayerSource;
    private readonly string _sourceName;
    private readonly bool _enableLocalVitals;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _latestRemoteFrameGate = new();
    private readonly RemoteEntityLifecycleTracker _remoteLifecycle = new();
    private RemotePlayerTelemetryFrame? _latestRemoteFrame;
    private long? _lastLifecycleSequence;
    private int _watchStarted;
    private int _disposed;

    public LocalPositionTelemetrySession(
        ITelemetrySession? remoteSession = null,
        ILocalMovementSource? localSource = null,
        string sourceName = "LOCAL",
        IRemotePlayerTelemetrySource? remotePlayerSource = null,
        bool? enableLocalVitals = null)
    {
        _remoteSession = remoteSession;
        _localSource = localSource ?? new NpcapLocalMovementSource(trackIrisSequenceDiagnostics: false);
        _sourceName = sourceName;
        _remotePlayerSource = remotePlayerSource;
        _enableLocalVitals = enableLocalVitals
                              ?? (_localSource as ILocalVitalsFeatureSource)?.LocalVitalsEnabled
                              ?? LocalVitalsFeature.IsEnabled();
    }

    public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("A telemetry session can only be watched once.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        // This is a fan-in for lanes with very different rates and authority.
        // Do not drop the oldest item here: a local movement sample can be
        // superseded safely, but evicting a low-rate remote snapshot (for
        // example Gacha stats) can erase the only state before the merger
        // observes it. High-rate sources already coalesce/drop upstream, so
        // waiting here applies bounded backpressure while preserving FIFO.
        // Every writer uses the linked cancellation token, therefore disposal
        // and a stopped consumer still unblock a pending write promptly.
        var channel = Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var tasks = new List<Task>
        {
            PumpLocalAsync(channel.Writer, linkedCancellation.Token),
            PumpTicksAsync(channel.Writer, linkedCancellation.Token)
        };
        if (_remoteSession is not null)
        {
            tasks.Add(PumpRemoteAsync(channel.Writer, linkedCancellation.Token));
        }
        if (_remotePlayerSource is not null)
        {
            tasks.Add(PumpRemotePlayersAsync(channel.Writer, linkedCancellation.Token));
        }

        TelemetrySnapshot? remote = null;
        TelemetrySnapshot? lastMergedSnapshot = null;
        LocalMovementObservation? local = null;
        RemotePlayerTelemetryFrame? remotePlayerFrame = null;
        RemotePlayerCaptureHealth? remotePlayerHealth =
            (_remotePlayerSource as IRemotePlayerTelemetryHealthSource)?.CaptureHealth;
        string? localError = null;
        try
        {
            yield return LocalPositionSnapshotMerger.Waiting(_sourceName);
            await foreach (var item in channel.Reader
                               .ReadAllAsync(linkedCancellation.Token)
                               .ConfigureAwait(false))
            {
                switch (item)
                {
                    case RemoteSnapshotEvent remoteEvent:
                        remote = remoteEvent.Snapshot;
                        break;
                    case LocalMovementEvent localEvent:
                        local = LocalMovementObservation.Coalesce(
                            local,
                            localEvent.Observation);
                        localError = null;
                        break;
                    case LocalFailureEvent failureEvent:
                        localError = failureEvent.Message;
                        break;
                    case RemotePlayersEvent remotePlayersEvent:
                        remotePlayerFrame = remotePlayersEvent.Frame;
                        break;
                    case RemotePlayersAvailableEvent:
                        lock (_latestRemoteFrameGate)
                        {
                            remotePlayerFrame = _latestRemoteFrame;
                        }
                        break;
                    case TickEvent:
                        // A dropped best-effort wake-up must not strand the
                        // latest remote frame behind the bounded event lane.
                        lock (_latestRemoteFrameGate)
                        {
                            if (_latestRemoteFrame is not null)
                            {
                                remotePlayerFrame = _latestRemoteFrame;
                            }
                        }
                        break;
                    case RemotePlayersFailureEvent failureEvent:
                        remotePlayerFrame = null;
                        remotePlayerHealth = new RemotePlayerCaptureHealth(
                            RemotePlayerCaptureState.Faulted,
                            remotePlayerHealth?.GameProcessFound == true,
                            remotePlayerHealth?.OwnedPortCount ?? 0,
                            remotePlayerHealth?.OpenedAdapterCount ?? 0,
                            remotePlayerHealth?.MatchedGamePackets ?? 0,
                            remotePlayerHealth?.LastGamePacketAt,
                            failureEvent.Message);
                        break;
                }

                var currentSourceHealth =
                    (_remotePlayerSource as IRemotePlayerTelemetryHealthSource)?.CaptureHealth;
                if (item is not RemotePlayersFailureEvent
                    || currentSourceHealth?.State == RemotePlayerCaptureState.Faulted)
                {
                    remotePlayerHealth = currentSourceHealth ?? remotePlayerHealth;
                }

                var now = DateTimeOffset.UtcNow;
                var usableRemotePlayerFrame = remotePlayerFrame is { } candidateFrame
                                              && LocalPositionSnapshotMerger.IsRemoteFrameFresh(
                                                  candidateFrame,
                                                  now)
                                              && IsRemoteFrameCompatibleWithLocal(
                                                  candidateFrame,
                                                  local,
                                                  now)
                    ? candidateFrame
                    : null;
                IReadOnlyList<VerifiedRemoteEntityTelemetry>? remotePlayers =
                    _remotePlayerSource is null
                        ? null
                        : usableRemotePlayerFrame is { } frame
                            ? frame.RemoteEntities
                            : null;
                var verifiedLocalSpeciesId = usableRemotePlayerFrame is { } localSpeciesFrame
                    ? localSpeciesFrame.LocalSpeciesId
                    : null;
                var previousMap = lastMergedSnapshot?.Map;
                var merged = LocalPositionSnapshotMerger.Merge(
                    remote ?? lastMergedSnapshot,
                    local,
                    now,
                    _sourceName,
                    remotePlayers,
                    verifiedLocalSpeciesId,
                    usableRemotePlayerFrame,
                    allowLocalVitals: _enableLocalVitals,
                    requireFreshLocalMovement: true);

                var lifecycle = usableRemotePlayerFrame is { } lifecycleFrame
                                && lifecycleFrame.Sequence != _lastLifecycleSequence
                    ? _remoteLifecycle.ApplyFrame(lifecycleFrame, now)
                    : _remoteLifecycle.AdvanceWithoutFrame(now);
                if (usableRemotePlayerFrame is { } appliedFrame)
                {
                    _lastLifecycleSequence = appliedFrame.Sequence;
                }
                if (merged.ProTrackingDiagnostics is { } trackingDiagnostics)
                {
                    merged = merged with
                    {
                        Map = ApplyRemoteLifecycleToMap(
                            merged.Map,
                            lifecycle,
                            previousMap,
                            preserveMissingFromNonEmptyFrame: remotePlayers is { Count: > 0 }),
                        ProPlayerTrackingActive = _remotePlayerSource is not null,
                        ProTrackingDiagnostics = trackingDiagnostics with
                        {
                            Lifecycle = lifecycle
                        }
                    };
                }
                else if (_remotePlayerSource is not null)
                {
                    merged = merged with
                    {
                        Map = ApplyRemoteLifecycleToMap(
                            merged.Map,
                            lifecycle,
                            previousMap,
                            preserveMissingFromNonEmptyFrame: remotePlayers is { Count: > 0 }),
                        ProPlayerTrackingActive = true,
                        ProTrackingDiagnostics = new RemoteTrackingDiagnostics
                        {
                            FrameState = "no-frame",
                            Lifecycle = lifecycle
                        }
                    };
                }
                if (remote is null
                    && local is null
                    && lastMergedSnapshot is null
                    && _remotePlayerSource is null
                    && !string.IsNullOrWhiteSpace(localError))
                {
                    merged = LocalPositionSnapshotMerger.Waiting(_sourceName, localError);
                }

                if (_remotePlayerSource is not null)
                {
                    merged = merged with
                    {
                        ProPlayerCaptureHealth = remotePlayerHealth
                    };
                }

                lastMergedSnapshot = merged;
                yield return merged;
            }
        }
        finally
        {
            linkedCancellation.Cancel();
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    internal static bool IsRemoteFrameCompatibleWithLocal(
        RemotePlayerTelemetryFrame frame,
        LocalMovementObservation? local,
        DateTimeOffset now)
    {
        if (local is not { } localObservation
            || !localObservation.HasMovement
            || now - localObservation.ObservedAt > LocalPositionSnapshotMerger.LocalFreshness
            || string.IsNullOrWhiteSpace(localObservation.ServerEndpoint))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(frame.ServerEndpoint)
               && string.Equals(
                   localObservation.ServerEndpoint.Trim(),
                   frame.ServerEndpoint.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        await _localSource.DisposeAsync().ConfigureAwait(false);
        if (_remoteSession is not null)
        {
            await _remoteSession.DisposeAsync().ConfigureAwait(false);
        }
        if (_remotePlayerSource is not null)
        {
            await _remotePlayerSource.DisposeAsync().ConfigureAwait(false);
        }
        _disposeCancellation.Dispose();
    }

    private async Task PumpRemoteAsync(
        ChannelWriter<SessionEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var snapshot in _remoteSession!
                               .WatchAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                await writer.WriteAsync(
                        new RemoteSnapshotEvent(snapshot),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PumpLocalAsync(
        ChannelWriter<SessionEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var observation in _localSource
                               .WatchAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                await writer.WriteAsync(
                        new LocalMovementEvent(observation),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (LocalPacketCaptureUnavailableException exception)
        {
            writer.TryWrite(new LocalFailureEvent(exception.Message));
        }
        catch (Exception)
        {
            writer.TryWrite(new LocalFailureEvent(
                "Không đọc được telemetry trực tiếp từ game."));
        }
    }

    private static async Task PumpTicksAsync(
        ChannelWriter<SessionEvent> writer,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(FreshnessCheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                writer.TryWrite(TickEvent.Instance);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PumpRemotePlayersAsync(
        ChannelWriter<SessionEvent> writer,
        CancellationToken cancellationToken)
    {
        try
        {
                await foreach (var frame in _remotePlayerSource!
                               .WatchAsync(cancellationToken)
                               .ConfigureAwait(false))
                {
                    // Remote position frames are latest-value data. A new
                    // frame must never wait behind stale local/server events
                    // in the shared FIFO. The periodic tick remains a
                    // fallback wake-up if this best-effort signal is dropped.
                    lock (_latestRemoteFrameGate)
                    {
                        _latestRemoteFrame = frame;
                    }
                    writer.TryWrite(RemotePlayersAvailableEvent.Instance);
                }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            writer.TryWrite(new RemotePlayersFailureEvent(
                string.Equals(
                    exception.GetType().Name,
                    "ProAgentException",
                    StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(exception.Message)
                    ? exception.Message
                    : "Pro Agent đã dừng; hãy mở lại Live Map để thử lại."));
        }
    }

    private abstract record SessionEvent;
    private sealed record RemoteSnapshotEvent(TelemetrySnapshot Snapshot) : SessionEvent;
    private sealed record LocalMovementEvent(LocalMovementObservation Observation) : SessionEvent;
    private sealed record LocalFailureEvent(string Message) : SessionEvent;
    private sealed record RemotePlayersEvent(RemotePlayerTelemetryFrame Frame) : SessionEvent;
    private sealed record RemotePlayersAvailableEvent : SessionEvent
    {
        public static RemotePlayersAvailableEvent Instance { get; } = new();
    }

    private static MapTelemetry? ApplyRemoteLifecycleToMap(
        MapTelemetry? map,
        IReadOnlyList<RemoteEntityLifecycleSnapshot> lifecycle,
        MapTelemetry? previousMap,
        bool preserveMissingFromNonEmptyFrame)
    {
        if (map is null || lifecycle.Count == 0)
        {
            return map;
        }

        var byTrack = lifecycle
            .GroupBy(item => (item.Kind, item.TrackId))
            .ToDictionary(group => group.Key, group => group.Last());
        var markers = map.Markers
            .Where(marker =>
            {
                if (marker.ProEntityKind is not { } kind
                    || !TryGetTrackId(marker.SteamId, out var trackId))
                {
                    return true;
                }

                return !byTrack.TryGetValue((kind, trackId), out var state)
                       || state.State != RemoteEntityLifecycleState.Removed;
            })
            .Select(marker =>
            {
                if (marker.ProEntityKind is not { } kind
                    || !TryGetTrackId(marker.SteamId, out var trackId)
                    || !byTrack.TryGetValue((kind, trackId), out var state))
                {
                    return marker;
                }

                return marker with
                {
                    // Preserve freshness loss reported by the merger even if
                    // the Agent presence frame itself is still arriving.
                    // Presence and location freshness are separate signals.
                    ProEntityIsStale = marker.ProEntityIsStale
                        || state.State is RemoteEntityLifecycleState.Stale
                            or RemoteEntityLifecycleState.TemporarilyMissing
                };
            })
            .ToArray();

        if (preserveMissingFromNonEmptyFrame && previousMap is not null)
        {
            var currentKeys = markers
                .Select(marker => marker.SteamId)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.Ordinal);
            var retained = previousMap.Markers
                .Where(marker =>
                    marker.SteamId is not null
                    && marker.SteamId.StartsWith("pro-entity:", StringComparison.Ordinal)
                    && !currentKeys.Contains(marker.SteamId)
                    && marker.ProEntityKind is { } kind
                    && TryGetTrackId(marker.SteamId, out var trackId)
                    && byTrack.TryGetValue((kind, trackId), out var state)
                    && state.State is RemoteEntityLifecycleState.TemporarilyMissing
                        or RemoteEntityLifecycleState.Stale)
                .Select(marker => marker with { ProEntityIsStale = true });
            markers = [.. markers, .. retained];
        }

        return map with { Markers = markers };
    }

    private static bool TryGetTrackId(string? steamId, out long trackId)
    {
        trackId = 0;
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return false;
        }

        var separator = steamId.LastIndexOf(':');
        return separator >= 0
               && long.TryParse(steamId[(separator + 1)..], out trackId);
    }
    private sealed record RemotePlayersFailureEvent(string Message) : SessionEvent;
    private sealed record TickEvent : SessionEvent
    {
        public static TickEvent Instance { get; } = new();
    }
}
