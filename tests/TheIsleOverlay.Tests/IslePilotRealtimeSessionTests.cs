using System.Runtime.CompilerServices;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotRealtimeSessionTests
{
    [Fact]
    public async Task WatchAsync_BootstrapsRestAndPublishesLiveSnapshot()
    {
        var api = new FakeApiClient();
        var socket = new FakeWebSocket(
        [
            new IslePilotOverlayLiveDataDto
            {
                HasDino = true,
                Health = 8,
                Position = new IslePilotOverlayPositionDto { X = 25, Y = 50, Yaw = 90 }
            }
        ]);
        await using var session = CreateSession(api, () => socket);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.Live,
            timeout.Token);

        Assert.Equal(1, api.MeCalls);
        Assert.Equal(1, api.MapCalls);
        Assert.Equal("overlay-token", socket.ConnectedToken);
        Assert.Equal("Player", socket.HelloName);
        Assert.Equal(8, snapshot.Player?.ExactVitals?.Health);
        var mapLocation = Assert.IsType<MapPoint>(snapshot.Player?.MapLocation);
        Assert.Equal(0.25, mapLocation.Left, precision: 8);
        Assert.Equal(0.5, mapLocation.Top, precision: 8);
    }

    [Fact]
    public async Task SocketFailure_KeepsLatestSnapshotAndReconnects()
    {
        var api = new FakeApiClient();
        var first = new FakeWebSocket(
            [new IslePilotOverlayLiveDataDto { HasDino = true, Health = 8 }],
            new IOException("network lost"));
        var second = new FakeWebSocket([]);
        var sockets = new Queue<FakeWebSocket>([first, second]);
        var requestedDelays = new List<TimeSpan>();
        await using var session = new IslePilotRealtimeSession(
            api,
            Options(),
            () => sockets.Dequeue(),
            new IslePilotReconnectBackoff(() => 0.5),
            (delay, _) =>
            {
                requestedDelays.Add(delay);
                return Task.CompletedTask;
            },
            () => DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var reconnecting = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.Reconnecting,
            timeout.Token);
        await second.Connected.WaitAsync(timeout.Token);

        Assert.Equal(8, reconnecting.Player?.ExactVitals?.Health);
        Assert.Equal([TimeSpan.FromSeconds(1)], requestedDelays);
        Assert.True(first.Disposed);
    }

    [Fact]
    public async Task AuthenticationFailure_StopsBeforeWebSocketAndRequiresLogin()
    {
        var api = new FakeApiClient
        {
            Failure = new IslePilotOverlayAuthenticationException("expired")
        };
        var socketFactoryCalls = 0;
        await using var session = CreateSession(api, () =>
        {
            socketFactoryCalls++;
            return new FakeWebSocket([]);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.AuthenticationRequired,
            timeout.Token);

        Assert.Equal("PHIÊN ĐÃ HẾT HẠN", snapshot.StatusMessage);
        Assert.Equal(0, socketFactoryCalls);
        Assert.False(await snapshots.MoveNextAsync());
    }

    [Fact]
    public async Task WebSocketAuthenticationFailure_DoesNotReconnect()
    {
        var api = new FakeApiClient();
        var socket = new FakeWebSocket(
            [],
            connectFailure: new IslePilotOverlayAuthenticationException("expired"));
        var socketFactoryCalls = 0;
        var reconnectDelayCalls = 0;
        await using var session = new IslePilotRealtimeSession(
            api,
            Options(),
            () =>
            {
                socketFactoryCalls++;
                return socket;
            },
            new IslePilotReconnectBackoff(() => 0.5),
            (_, _) =>
            {
                reconnectDelayCalls++;
                return Task.CompletedTask;
            },
            () => DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        _ = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.AuthenticationRequired,
            timeout.Token);

        Assert.Equal(1, socketFactoryCalls);
        Assert.Equal(0, reconnectDelayCalls);
        Assert.True(socket.Disposed);
    }

    private static IslePilotRealtimeSession CreateSession(
        IIslePilotOverlayApiClient api,
        Func<IIslePilotOverlayWebSocket> socketFactory) => new(
            api,
            Options(),
            socketFactory);

    private static IslePilotOverlayOptions Options() => new()
    {
        OverlayToken = "overlay-token",
        MeRefreshInterval = TimeSpan.FromHours(1),
        MapRefreshInterval = TimeSpan.FromHours(1)
    };

    private static async Task<TelemetrySnapshot> ReadUntilAsync(
        IAsyncEnumerator<TelemetrySnapshot> snapshots,
        Func<TelemetrySnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        while (await snapshots.MoveNextAsync().AsTask().WaitAsync(cancellationToken))
        {
            if (predicate(snapshots.Current))
            {
                return snapshots.Current;
            }
        }

        throw new InvalidOperationException("The telemetry session ended before the expected snapshot.");
    }

    private sealed class FakeApiClient : IIslePilotOverlayApiClient
    {
        public Exception? Failure { get; init; }
        public bool Online { get; init; }
        public int MeCalls { get; private set; }
        public int MapCalls { get; private set; }

        public Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default)
        {
            MeCalls++;
            if (Failure is not null)
            {
                return Task.FromException<IslePilotOverlayMeDto>(Failure);
            }

            return Task.FromResult(new IslePilotOverlayMeDto
            {
                HasData = true,
                Online = Online,
                SteamId = "76561198000000000",
                PersonaName = "Player",
                Species = "Utahraptor",
                Server = "IslePilot Server",
                MaxHealth = 20
            });
        }

        public Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default)
        {
            MapCalls++;
            if (Failure is not null)
            {
                return Task.FromException<IslePilotOverlayMapDto>(Failure);
            }

            return Task.FromResult(new IslePilotOverlayMapDto
            {
                Allowed = true,
                Calibration = new IslePilotMapCalibrationDto
                {
                    A = new IslePilotMapCalibrationPointDto { WorldX = 0, WorldY = 0, U = 0, V = 0 },
                    B = new IslePilotMapCalibrationPointDto { WorldX = 100, WorldY = 100, U = 1, V = 1 }
                }
            });
        }
    }

    private sealed class FakeWebSocket(
        IReadOnlyList<IslePilotOverlayLiveDataDto> frames,
        Exception? failure = null,
        Exception? connectFailure = null) : IIslePilotOverlayWebSocket
    {
        private readonly TaskCompletionSource _connected = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }
        public string? ConnectedToken { get; private set; }
        public string? HelloName { get; private set; }
        public Task Connected => _connected.Task;

        public Task ConnectAsync(string overlayToken, CancellationToken cancellationToken = default)
        {
            if (connectFailure is not null)
            {
                return Task.FromException(connectFailure);
            }

            ConnectedToken = overlayToken;
            IsConnected = true;
            _connected.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SendHelloAsync(string? personaName, CancellationToken cancellationToken = default)
        {
            HelloName = personaName;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<IslePilotOverlayLiveDataDto> ReadLiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var frame in frames)
            {
                yield return frame;
            }

            if (failure is not null)
            {
                throw failure;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
