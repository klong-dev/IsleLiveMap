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
