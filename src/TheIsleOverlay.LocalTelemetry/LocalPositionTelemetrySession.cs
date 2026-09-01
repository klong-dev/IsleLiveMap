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
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _watchStarted;
    private int _disposed;

    public LocalPositionTelemetrySession(
        ITelemetrySession? remoteSession = null,
        ILocalMovementSource? localSource = null,
        string sourceName = "LOCAL",
        IRemotePlayerTelemetrySource? remotePlayerSource = null)
    {
        _remoteSession = remoteSession;
        _localSource = localSource ?? new NpcapLocalMovementSource();
        _sourceName = sourceName;
        _remotePlayerSource = remotePlayerSource;
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
        var channel = Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
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
        LocalMovementObservation? local = null;
        RemotePlayerTelemetryFrame? remotePlayerFrame = null;
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
                        local = localEvent.Observation;
                        localError = null;
                        break;
                    case LocalFailureEvent failureEvent:
                        localError = failureEvent.Message;
                        break;
                    case RemotePlayersEvent remotePlayersEvent:
                        remotePlayerFrame = remotePlayersEvent.Frame;
                        break;
                    case RemotePlayersFailureEvent:
                        remotePlayerFrame = null;
                        break;
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
                            : [];
                var verifiedLocalSpeciesId = usableRemotePlayerFrame is { } localSpeciesFrame
                    ? localSpeciesFrame.LocalSpeciesId
                    : null;
                var merged = LocalPositionSnapshotMerger.Merge(
                    remote,
                    local,
                    now,
                    _sourceName,
                    remotePlayers,
                    verifiedLocalSpeciesId,
                    usableRemotePlayerFrame);
                if (remote is null
                    && local is null
                    && !string.IsNullOrWhiteSpace(localError))
                {
                    merged = LocalPositionSnapshotMerger.Waiting(_sourceName, localError);
                }

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
                await writer.WriteAsync(
                        new RemotePlayersEvent(frame),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            writer.TryWrite(RemotePlayersFailureEvent.Instance);
        }
    }

    private abstract record SessionEvent;
    private sealed record RemoteSnapshotEvent(TelemetrySnapshot Snapshot) : SessionEvent;
    private sealed record LocalMovementEvent(LocalMovementObservation Observation) : SessionEvent;
    private sealed record LocalFailureEvent(string Message) : SessionEvent;
    private sealed record RemotePlayersEvent(RemotePlayerTelemetryFrame Frame) : SessionEvent;
    private sealed record RemotePlayersFailureEvent : SessionEvent
    {
        public static RemotePlayersFailureEvent Instance { get; } = new();
    }
    private sealed record TickEvent : SessionEvent
    {
        public static TickEvent Instance { get; } = new();
    }
}
