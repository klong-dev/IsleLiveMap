using System.Runtime.CompilerServices;

namespace TheIsleOverlay.Core;

public sealed class PollingTelemetrySession : ITelemetrySession
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly ITelemetryProvider _provider;
    private readonly TimeSpan _interval;
    private readonly string _source;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _disposed;

    public PollingTelemetrySession(
        ITelemetryProvider provider,
        TimeSpan? interval = null,
        string source = "Unknown")
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _interval = interval ?? DefaultInterval;
        _source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;

        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Polling interval must be positive.");
        }
    }

    public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        var linkedToken = linkedCancellation.Token;
        TelemetrySnapshot? lastSnapshot = null;

        while (!linkedToken.IsCancellationRequested)
        {
            TelemetrySnapshot snapshot;
            var stopAfterSnapshot = false;
            try
            {
                var current = await _provider.GetSnapshotAsync(linkedToken);
                snapshot = current with
                {
                    SessionState = TelemetrySessionState.Polling,
                    LiveDataStale = false,
                    StatusMessage = null
                };
                lastSnapshot = snapshot;
            }
            catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (TelemetryAuthenticationException)
            {
                snapshot = CreateStatusSnapshot(
                    lastSnapshot,
                    TelemetrySessionState.AuthenticationRequired,
                    "Authentication required.",
                    forceFailure: true);
                stopAfterSnapshot = true;
            }
            catch (Exception)
            {
                snapshot = CreateStatusSnapshot(
                    lastSnapshot,
                    TelemetrySessionState.Reconnecting,
                    "Telemetry source unavailable.");
            }

            yield return snapshot;
            if (stopAfterSnapshot)
            {
                yield break;
            }

            try
            {
                await Task.Delay(_interval, linkedToken);
            }
            catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
            {
                yield break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _disposeCancellation.Cancel();
        }

        return ValueTask.CompletedTask;
    }

    private TelemetrySnapshot CreateStatusSnapshot(
        TelemetrySnapshot? lastSnapshot,
        TelemetrySessionState state,
        string message,
        bool forceFailure = false)
    {
        var snapshot = lastSnapshot ?? new TelemetrySnapshot
        {
            Source = _source,
            Success = false,
            ServerOnline = false,
            PlayerOnline = false
        };

        return snapshot with
        {
            Success = forceFailure ? false : snapshot.Success,
            SessionState = state,
            LiveDataStale = false,
            StatusMessage = message
        };
    }
}
