using System.Threading.Channels;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class LocalPositionTelemetrySessionHealthTests
{
    [Fact]
    public async Task WatchAsync_PublishesProCaptureHealthWithoutTelemetryFrame()
    {
        var local = new FakeLocalSource();
        var remotePlayers = new SilentRemotePlayerSource
        {
            CaptureHealth = new RemotePlayerCaptureHealth(
                RemotePlayerCaptureState.WaitingForPort,
                true,
                0,
                0,
                0,
                null,
                "waiting for game port")
        };
        await using var session = new LocalPositionTelemetrySession(
            localSource: local,
            remotePlayerSource: remotePlayers);
        using var timeout = new CancellationTokenSource();
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await using var watch = session.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        Assert.True(await watch.MoveNextAsync());
        local.Publish(new LocalMovementObservation(
            DateTimeOffset.UtcNow,
            new UnrealMovementCandidate(100, 200, 30, 40, 5, 64, 380, 26),
            "127.0.0.1:7777"));

        Assert.True(await watch.MoveNextAsync());
        Assert.NotNull(watch.Current.ProPlayerCaptureHealth);
        Assert.Equal(
            RemotePlayerCaptureState.WaitingForPort,
            watch.Current.ProPlayerCaptureHealth!.State);
        Assert.True(watch.Current.ProPlayerCaptureHealth.GameProcessFound);
    }

    [Fact]
    public async Task WatchAsync_VitalsOnlyUpdateDoesNotRefreshStaleMovement()
    {
        var local = new FakeLocalSource();
        await using var session = new LocalPositionTelemetrySession(
            localSource: local,
            enableLocalVitals: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var watch = session.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await watch.MoveNextAsync());

        var staleMovementAt = DateTimeOffset.UtcNow
            .Subtract(LocalPositionSnapshotMerger.LocalFreshness)
            .Subtract(TimeSpan.FromSeconds(1));
        local.Publish(new LocalMovementObservation(
            staleMovementAt,
            new UnrealMovementCandidate(100, 200, 30, 40, 5, 64, 380, 26),
            "127.0.0.1:7777"));
        Assert.True(await watch.MoveNextAsync());

        var vitalsAt = DateTimeOffset.UtcNow;
        local.Publish(LocalMovementObservation.VitalsOnly(
            new LocalDinosaurVitalsObservation(
                vitalsAt,
                new ExactVitals { Health = 75, MaxHealth = 100 },
                42),
            "127.0.0.1:7777"));
        Assert.True(await watch.MoveNextAsync());

        Assert.Equal("LocalIris", watch.Current.Player?.ExactVitalsSource);
        Assert.Null(watch.Current.Player?.Location);
        Assert.Equal(vitalsAt, watch.Current.UpdatedAt);
    }

    [Fact]
    public async Task WatchAsync_CanaryOffDoesNotExposeLocalVitals()
    {
        var local = new FakeLocalSource();
        await using var session = new LocalPositionTelemetrySession(
            localSource: local,
            enableLocalVitals: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var watch = session.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await watch.MoveNextAsync());

        var observedAt = DateTimeOffset.UtcNow;
        local.Publish(new LocalMovementObservation(
            observedAt,
            new UnrealMovementCandidate(100, 200, 30, 40, 5, 64, 380, 26),
            "127.0.0.1:7777",
            new LocalDinosaurVitalsObservation(
                observedAt,
                new ExactVitals { Health = 75, MaxHealth = 100 },
                42)));
        Assert.True(await watch.MoveNextAsync());

        Assert.Null(watch.Current.Player?.ExactVitals);
        Assert.Null(watch.Current.Player?.ExactVitalsSource);
        Assert.Equal(100, watch.Current.Player?.Location?.X);
    }

    [Fact]
    public async Task WatchAsync_LocalCaptureFailureDoesNotEraseRemoteFrame()
    {
        var remote = new SingleRemotePlayerSource();
        await using var session = new LocalPositionTelemetrySession(
            remoteSession: new StaticRemoteSession(),
            localSource: new FailingLocalSource(),
            remotePlayerSource: remote);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var watch = session.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await watch.MoveNextAsync());

        TelemetrySnapshot? withRemote = null;
        for (var attempt = 0; attempt < 8 && withRemote is null; attempt++)
        {
            Assert.True(await watch.MoveNextAsync());
            if (watch.Current.ProTrackingDiagnostics?.RenderedCount > 0)
            {
                withRemote = watch.Current;
            }
        }

        Assert.NotNull(withRemote);
        Assert.Contains(
            withRemote!.Map!.Markers,
            marker => marker.SteamId == "pro-entity:player:77");

        // The local lane has already failed. A subsequent tick must keep the
        // remote frame/map alive instead of replacing it with local Waiting.
        Assert.True(await watch.MoveNextAsync());
        Assert.Contains(
            watch.Current.Map!.Markers,
            marker => marker.SteamId == "pro-entity:player:77");
    }

    [Fact]
    public async Task WatchAsync_ProviderSnapshotDoesNotEraseAdmittedProMarkerHistory()
    {
        var source = new ControlledRemotePlayerSource();
        await using var session = new LocalPositionTelemetrySession(
            remoteSession: new StaticRemoteSession(), localSource: new FakeLocalSource(),
            remotePlayerSource: source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var watch = session.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        Assert.True(await watch.MoveNextAsync());
        while (watch.Current.Source != "TEST") Assert.True(await watch.MoveNextAsync());

        // Start just inside the initial position admission window. One later
        // heartbeat should retain the already admitted marker, not read the
        // provider's empty map as though this player was never rendered.
        var start = DateTimeOffset.UtcNow;
        var entity = new VerifiedRemoteEntityTelemetry(77, RemoteEntityKind.Player,
            null, "rex", "Rex", CreatureDiet.Carnivore, null,
            new WorldLocation { X = 100, Y = 100, Z = 0 }, 0, 4, start,
            LocationObservedAt: start - VerifiedRemoteEntityTelemetry.InitialPositionAdmission
                + TimeSpan.FromSeconds(1), ActorNetRefHandle: 77,
            PlayerStateNetRefHandle: 78, PawnNetRefHandle: 80);
        RemotePlayerTelemetryFrame Frame(long sequence, string sessionId) => new(
            sequence, DateTimeOffset.UtcNow, "server:7777", new WorldLocation(), 0,
            [entity with { ObservedAt = DateTimeOffset.UtcNow }],
            ReceivedAt: DateTimeOffset.UtcNow, SessionId: sessionId);
        source.Publish(Frame(1, "a"));
        while (watch.Current.ProPlayerSequence != 1) Assert.True(await watch.MoveNextAsync());
        Assert.Contains(watch.Current.Map!.Markers, m => m.SteamId == "pro-entity:player:77");
        await Task.Delay(1500, timeout.Token);
        source.Publish(Frame(2, "a"));
        while (watch.Current.ProPlayerSequence != 2) Assert.True(await watch.MoveNextAsync());
        var retained = Assert.Single(watch.Current.Map!.Markers, m => m.SteamId == "pro-entity:player:77");
        Assert.True(retained.ProEntityIsStale);
        Assert.Equal(entity.Location, retained.Location);

        // The same numeric id in another Agent session has no admission history.
        source.Publish(Frame(1, "b"));
        while (watch.Current.ProPlayerSessionId != "b") Assert.True(await watch.MoveNextAsync());
        Assert.DoesNotContain(watch.Current.Map?.Markers ?? [], m => m.SteamId == "pro-entity:player:77");
    }

    [Theory]
    [InlineData("a", "server:7777", true)]
    [InlineData("b", "server:7777", false)]
    [InlineData("a", "other:7777", false)]
    [InlineData(null, "server:7777", false)]
    public void PrepareMergeInput_PreservesProviderDataAndScopesHistory(string? sessionId, string endpoint, bool keep)
    {
        var providerMarker = new MapMarkerTelemetry { SteamId = "provider", Label = "Team" };
        var proMarker = new MapMarkerTelemetry { SteamId = "pro-entity:player:77" };
        var provider = new TelemetrySnapshot { Source = "TEST", Player = new PlayerTelemetry { Name = "latest stats" },
            Map = new MapTelemetry { Markers = [providerMarker], PlayerHeatmapEnabled = true } };
        var previous = new TelemetrySnapshot { ProPlayerSessionId = "a", ProPlayerServerEndpoint = "server:7777",
            Map = new MapTelemetry { Markers = [proMarker] } };
        var frame = new RemotePlayerTelemetryFrame(1, DateTimeOffset.UtcNow, endpoint, new WorldLocation(), 0, [], SessionId: sessionId);
        var input = LocalPositionTelemetrySession.PrepareMergeInput(provider, previous, frame)!;
        Assert.Same(provider.Player, input.Player);
        Assert.True(input.Map!.PlayerHeatmapEnabled);
        Assert.Contains(providerMarker, input.Map.Markers);
        Assert.Equal(keep, input.Map.Markers.Contains(proMarker));
        var cleared = LocalPositionSnapshotMerger.Merge(input, null, DateTimeOffset.UtcNow, remotePlayers: []);
        Assert.DoesNotContain(cleared.Map!.Markers, m => m.SteamId == proMarker.SteamId);
        Assert.Contains(providerMarker, cleared.Map.Markers);
    }

    private sealed class ControlledRemotePlayerSource : IRemotePlayerTelemetrySource
    {
        private readonly Channel<RemotePlayerTelemetryFrame> _frames = Channel.CreateUnbounded<RemotePlayerTelemetryFrame>();
        public void Publish(RemotePlayerTelemetryFrame frame) => _frames.Writer.TryWrite(frame);
        public async IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken)) yield return frame;
        }
        public ValueTask DisposeAsync() { _frames.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }

    private sealed class FakeLocalSource : ILocalMovementSource
    {
        private readonly Channel<LocalMovementObservation> _items =
            Channel.CreateUnbounded<LocalMovementObservation>();

        public void Publish(LocalMovementObservation item) => _items.Writer.TryWrite(item);

        public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await foreach (var item in _items.Reader.ReadAllAsync(cancellationToken))
            {
                yield return item;
            }
        }

        public ValueTask DisposeAsync()
        {
            _items.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingLocalSource : ILocalMovementSource
    {
        public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw new LocalPacketCaptureUnavailableException("local capture failed");
            #pragma warning disable CS0162
            yield break;
            #pragma warning restore CS0162
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StaticRemoteSession : ITelemetrySession
    {
        public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            yield return new TelemetrySnapshot
            {
                Source = "TEST",
                Success = true,
                ServerOnline = true,
                PlayerOnline = true
            };
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SingleRemotePlayerSource : IRemotePlayerTelemetrySource
    {
        public async IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            yield return new RemotePlayerTelemetryFrame(
                1,
                DateTimeOffset.UtcNow,
                "server:7777",
                new WorldLocation { X = 0, Y = 0, Z = 0 },
                0,
                [new VerifiedRemoteEntityTelemetry(
                    77,
                    RemoteEntityKind.Player,
                    "proof",
                    "rex",
                    "Rex",
                    CreatureDiet.Carnivore,
                    null,
                    new WorldLocation { X = 100, Y = 100, Z = 0 },
                    100,
                    1,
                    DateTimeOffset.UtcNow,
                    ActorNetRefHandle: 77,
                    PlayerStateNetRefHandle: 78,
                    PawnNetRefHandle: 79)]);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentRemotePlayerSource :
        IRemotePlayerTelemetrySource,
        IRemotePlayerTelemetryHealthSource
    {
        public RemotePlayerCaptureHealth CaptureHealth { get; init; } =
            RemotePlayerCaptureHealth.Starting;

        public async IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
