using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotRealtimeSession : ITelemetrySession
{
    private readonly IIslePilotOverlayApiClient _apiClient;
    private readonly IslePilotOverlayOptions _options;
    private readonly Func<IIslePilotOverlayWebSocket> _socketFactory;
    private readonly IslePilotReconnectBackoff _backoff;
    private readonly Func<TimeSpan, CancellationToken, Task> _reconnectDelay;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IslePilotOverlayStateReducer _reducer;
    private readonly Channel<TelemetrySnapshot> _snapshots;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _stateGate = new();

    private Task? _runTask;
    private int _watchStarted;
    private int _disposed;

    public IslePilotRealtimeSession(
        IIslePilotOverlayApiClient apiClient,
        IslePilotOverlayOptions options,
        Func<IIslePilotOverlayWebSocket>? socketFactory = null)
        : this(
            apiClient,
            options,
            socketFactory ?? (() => new IslePilotOverlayWebSocket()),
            new IslePilotReconnectBackoff(),
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            static () => DateTimeOffset.UtcNow)
    {
    }

    internal IslePilotRealtimeSession(
        IIslePilotOverlayApiClient apiClient,
        IslePilotOverlayOptions options,
        Func<IIslePilotOverlayWebSocket> socketFactory,
        IslePilotReconnectBackoff backoff,
        Func<TimeSpan, CancellationToken, Task> reconnectDelay,
        Func<DateTimeOffset> utcNow)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _backoff = backoff ?? throw new ArgumentNullException(nameof(backoff));
        _reconnectDelay = reconnectDelay ?? throw new ArgumentNullException(nameof(reconnectDelay));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));

        ValidateOptions(options);
        _reducer = new IslePilotOverlayStateReducer(options.LiveDataLifetime);
        _snapshots = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("An IslePilot telemetry session can only be watched once.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        _runTask = RunAsync(linkedCancellation.Token);

        try
        {
            while (true)
            {
                bool canRead;
                try
                {
                    canRead = await _snapshots.Reader.WaitToReadAsync(linkedCancellation.Token);
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                    break;
                }

                if (!canRead)
                {
                    break;
                }

                while (_snapshots.Reader.TryRead(out var snapshot))
                {
                    yield return snapshot;
                }
            }
        }
        finally
        {
            linkedCancellation.Cancel();
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _disposeCancellation.Cancel();
        var runTask = Volatile.Read(ref _runTask);
        if (runTask is not null)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
            {
            }
        }

        _disposeCancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Exception? completionError = null;
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.Connecting));
            await BootstrapAsync(runCancellation.Token);

            var tasks = new[]
            {
                RunGuardedAsync(PollMeAsync, runCancellation),
                RunGuardedAsync(PollMapAsync, runCancellation),
                RunGuardedAsync(RunWebSocketAsync, runCancellation),
                RunGuardedAsync(MonitorStaleDataAsync, runCancellation)
            };

            await Task.WhenAll(tasks);
        }
        catch (TelemetryAuthenticationException)
        {
            runCancellation.Cancel();
            UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.AuthenticationRequired));
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            completionError = exception;
        }
        finally
        {
            runCancellation.Cancel();
            _snapshots.Writer.TryComplete(completionError);
        }
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _apiClient.GetMeAsync(cancellationToken);
            UpdateState(reducer => reducer.ApplyMe(me, _utcNow()));
        }
        catch (TelemetryAuthenticationException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableRestFailure(exception, cancellationToken))
        {
        }

        try
        {
            var map = await _apiClient.GetMapAsync(cancellationToken);
            UpdateState(reducer => reducer.ApplyMap(map, _utcNow()));
        }
        catch (TelemetryAuthenticationException)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableRestFailure(exception, cancellationToken))
        {
        }
    }

    private async Task PollMeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(_options.MeRefreshInterval, cancellationToken);
            try
            {
                var me = await _apiClient.GetMeAsync(cancellationToken);
                UpdateState(reducer => reducer.ApplyMe(me, _utcNow()));
            }
            catch (TelemetryAuthenticationException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableRestFailure(exception, cancellationToken))
            {
            }
        }
    }

    private async Task PollMapAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(_options.MapRefreshInterval, cancellationToken);
            try
            {
                var map = await _apiClient.GetMapAsync(cancellationToken);
                UpdateState(reducer => reducer.ApplyMap(map, _utcNow()));
            }
            catch (TelemetryAuthenticationException)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableRestFailure(exception, cancellationToken))
            {
            }
        }
    }

    private async Task RunWebSocketAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var socket = _socketFactory();
                await socket.ConnectAsync(_options.OverlayToken, cancellationToken);
                _backoff.Reset();
                await socket.SendHelloAsync(ReadPersonaName(), cancellationToken);

                await foreach (var live in socket.ReadLiveAsync(cancellationToken))
                {
                    UpdateState(reducer => reducer.ApplyLive(live, _utcNow()));
                }

                throw new WebSocketException("The IslePilot WebSocket closed.");
            }
            catch (TelemetryAuthenticationException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableSocketFailure(exception))
            {
                UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.Reconnecting));
            }

            await _reconnectDelay(_backoff.NextDelay(), cancellationToken);
        }
    }

    private async Task MonitorStaleDataAsync(CancellationToken cancellationToken)
    {
        var intervalMilliseconds = Math.Clamp(
            _options.LiveDataLifetime.TotalMilliseconds / 4d,
            100d,
            1000d);
        var interval = TimeSpan.FromMilliseconds(intervalMilliseconds);

        while (true)
        {
            await Task.Delay(interval, cancellationToken);
            PublishSnapshot();
        }
    }

    private async Task RunGuardedAsync(
        Func<CancellationToken, Task> action,
        CancellationTokenSource runCancellation)
    {
        try
        {
            await action(runCancellation.Token);
        }
        catch
        {
            runCancellation.Cancel();
            throw;
        }
    }

    private void UpdateState(Action<IslePilotOverlayStateReducer> update)
    {
        lock (_stateGate)
        {
            update(_reducer);
            _snapshots.Writer.TryWrite(_reducer.BuildSnapshot(_utcNow()));
        }
    }

    private void PublishSnapshot()
    {
        lock (_stateGate)
        {
            _snapshots.Writer.TryWrite(_reducer.BuildSnapshot(_utcNow()));
        }
    }

    private string? ReadPersonaName()
    {
        lock (_stateGate)
        {
            return _reducer.PersonaName;
        }
    }

    private static bool IsRecoverableRestFailure(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException or InvalidDataException or JsonException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static bool IsRecoverableSocketFailure(Exception exception) =>
        exception is WebSocketException or HttpRequestException or IOException or InvalidDataException or JsonException;

    private static void ValidateOptions(IslePilotOverlayOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OverlayToken) ||
            options.OverlayToken.Contains('\r') || options.OverlayToken.Contains('\n'))
        {
            throw new ArgumentException("The IslePilot overlay token is invalid.", nameof(options));
        }

        if (options.MeRefreshInterval <= TimeSpan.Zero ||
            options.MapRefreshInterval <= TimeSpan.Zero ||
            options.LiveDataLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Overlay intervals must be positive.");
        }
    }
}
