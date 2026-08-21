using System.Runtime.CompilerServices;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class AuthenticationInvalidatingTelemetrySessionTests
{
    [Fact]
    public async Task WatchAsync_InvalidatesCredentialsOnceAndKeepsPublishingTheSnapshot()
    {
        var inner = new FakeSession(
        [
            new TelemetrySnapshot { SessionState = TelemetrySessionState.Connecting },
            new TelemetrySnapshot { SessionState = TelemetrySessionState.AuthenticationRequired },
            new TelemetrySnapshot { SessionState = TelemetrySessionState.AuthenticationRequired }
        ]);
        var invalidationCalls = 0;
        await using var session = new AuthenticationInvalidatingTelemetrySession(
            inner,
            () => invalidationCalls++);

        var snapshots = new List<TelemetrySnapshot>();
        await foreach (var snapshot in session.WatchAsync(CancellationToken.None))
        {
            snapshots.Add(snapshot);
        }

        Assert.Equal(3, snapshots.Count);
        Assert.Equal(TelemetrySessionState.AuthenticationRequired, snapshots[^1].SessionState);
        Assert.Equal(1, invalidationCalls);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheInnerSession()
    {
        var inner = new FakeSession([]);
        var session = new AuthenticationInvalidatingTelemetrySession(inner, static () => { });

        await session.DisposeAsync();

        Assert.True(inner.Disposed);
    }

    private sealed class FakeSession(IReadOnlyList<TelemetrySnapshot> snapshots) : ITelemetrySession
    {
        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            foreach (var snapshot in snapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return snapshot;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
