using System.Threading.Channels;
using TheIsleOverlay.Core;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class PrewarmedRemotePlayerTelemetrySourceTests
{
    [Fact]
    public async Task WatchAsync_ReplaysNewestFrameCapturedBeforeMapSubscription()
    {
        var inner = new FakeRemotePlayerTelemetrySource();
        await using var source = new PrewarmedRemotePlayerTelemetrySource(inner);
        source.Start();

        inner.Publish(Frame(1));
        inner.Publish(Frame(2));
        await inner.WaitUntilReadAsync(2);

        await using var enumerator = source
            .WatchAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Sequence);
    }

    [Fact]
    public async Task WatchAsync_DropsOldUiFramesButKeepsAgentRunning()
    {
        var inner = new FakeRemotePlayerTelemetrySource();
        await using var source = new PrewarmedRemotePlayerTelemetrySource(inner);
        source.Start();

        inner.Publish(Frame(10));
        inner.Publish(Frame(20));
        inner.Publish(Frame(30));
        await inner.WaitUntilReadAsync(3);

        await using var enumerator = source
            .WatchAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(30, enumerator.Current.Sequence);
    }

    [Fact]
    public async Task CaptureHealth_ForwardsInnerSourceStateBeforeMapSubscription()
    {
        var inner = new FakeRemotePlayerTelemetrySource
        {
            CaptureHealth = new RemotePlayerCaptureHealth(
                RemotePlayerCaptureState.WaitingForPort,
                true,
                0,
                0,
                0,
                null,
                "waiting")
        };
        await using var source = new PrewarmedRemotePlayerTelemetrySource(inner);

        Assert.Equal(RemotePlayerCaptureState.WaitingForPort, source.CaptureHealth.State);
        Assert.True(source.CaptureHealth.GameProcessFound);
    }

    private static RemotePlayerTelemetryFrame Frame(long sequence) => new(
        sequence,
        DateTimeOffset.UtcNow,
        "127.0.0.1:7777",
        new WorldLocation { X = sequence, Y = 2d, Z = 3d },
        MapHeadingDegrees: 4d,
        RemoteEntities: []);

    private sealed class FakeRemotePlayerTelemetrySource
        : IRemotePlayerTelemetrySource, IRemotePlayerTelemetryHealthSource
    {
        private readonly Channel<RemotePlayerTelemetryFrame> _channel =
            Channel.CreateUnbounded<RemotePlayerTelemetryFrame>();
        private int _readCount;

        public RemotePlayerCaptureHealth CaptureHealth { get; set; } =
            RemotePlayerCaptureHealth.Starting;

        public void Publish(RemotePlayerTelemetryFrame frame) =>
            _channel.Writer.TryWrite(frame);

        public async Task WaitUntilReadAsync(int expected)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            while (Volatile.Read(ref _readCount) < expected)
            {
                await Task.Delay(5, timeout.Token);
            }
        }

        public async IAsyncEnumerable<RemotePlayerTelemetryFrame> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            await foreach (var frame in _channel.Reader
                               .ReadAllAsync(cancellationToken))
            {
                Interlocked.Increment(ref _readCount);
                yield return frame;
            }
        }

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

}
