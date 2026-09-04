using System.Threading.Channels;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.ProClient;

/// <summary>
/// Starts the signed Pro Agent as soon as Home has verified an active Pro
/// entitlement, then hands the same live stream to the map. Iris sends actor
/// creation and identity proof only once, so delaying the Agent until the map
/// window opens permanently loses bootstrap evidence for already-rendered
/// players.
/// </summary>
public sealed class PrewarmedRemotePlayerTelemetrySource : IRemotePlayerTelemetrySource
{
    private readonly IRemotePlayerTelemetrySource _inner;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<RemotePlayerTelemetryFrame> _updates =
        Channel.CreateBounded<RemotePlayerTelemetryFrame>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
    private readonly object _latestGate = new();
    private RemotePlayerTelemetryFrame? _latest;
    private Task? _pumpTask;
    private int _started;
    private int _watchStarted;
    private int _disposed;

    public PrewarmedRemotePlayerTelemetrySource(
        IRemotePlayerTelemetrySource inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(
                nameof(PrewarmedRemotePlayerTelemetrySource));
        }

        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _pumpTask = PumpAsync(_shutdown.Token);
        }
    }

    public async IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException(
                "A prewarmed Pro Agent source can only be watched once.");
        }

        Start();
        RemotePlayerTelemetryFrame? replay;
        lock (_latestGate)
        {
            replay = _latest;
        }

        if (replay is { } current)
        {
            yield return current;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        await foreach (var frame in _updates.Reader
                           .ReadAllAsync(linkedCancellation.Token)
                           .ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        await _inner.DisposeAsync().ConfigureAwait(false);
        if (_pumpTask is not null)
        {
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _updates.Writer.TryComplete();
        _shutdown.Dispose();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _inner
                               .WatchAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                lock (_latestGate)
                {
                    _latest = frame;
                }

                _updates.Writer.TryWrite(frame);
            }

            _updates.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _updates.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            _updates.Writer.TryComplete(exception);
        }
    }
}
