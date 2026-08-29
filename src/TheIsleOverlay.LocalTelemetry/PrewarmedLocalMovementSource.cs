using System.Threading.Channels;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Starts packet capture before the overlay is opened and replays the newest
/// observation to its single consumer. This prevents the initial GAS snapshot
/// sent during spawn/reconnect from being lost while the user is still on Home.
/// </summary>
public sealed class PrewarmedLocalMovementSource : ILocalMovementSource
{
    private readonly ILocalMovementSource _inner;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Channel<LocalMovementObservation> _updates =
        Channel.CreateBounded<LocalMovementObservation>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
    private readonly object _latestGate = new();
    private LocalMovementObservation? _latest;
    private Task? _pumpTask;
    private int _started;
    private int _watchStarted;
    private int _disposed;

    public PrewarmedLocalMovementSource(ILocalMovementSource? inner = null)
    {
        _inner = inner ?? new NpcapLocalMovementSource();
    }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(PrewarmedLocalMovementSource));
        }

        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _pumpTask = PumpAsync(_shutdown.Token);
        }
    }

    public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("A prewarmed local source can only be watched once.");
        }

        Start();
        LocalMovementObservation? replay;
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
        await foreach (var observation in _updates.Reader
                           .ReadAllAsync(linkedCancellation.Token)
                           .ConfigureAwait(false))
        {
            yield return observation;
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
            await foreach (var observation in _inner
                               .WatchAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                lock (_latestGate)
                {
                    _latest = observation;
                }

                _updates.Writer.TryWrite(observation);
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
