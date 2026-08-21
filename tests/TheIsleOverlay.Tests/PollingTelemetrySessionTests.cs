using TheIsleOverlay.Core;

namespace TheIsleOverlay.Tests;

public sealed class PollingTelemetrySessionTests
{
    [Fact]
    public async Task WatchAsync_EmitsProviderSnapshotsInOrder()
    {
        var provider = new SequenceProvider(
            Snapshot("first"),
            Snapshot("second"));
        await using var session = new PollingTelemetrySession(
            provider,
            TimeSpan.FromMilliseconds(1),
            "Legacy");

        var snapshots = await TakeAsync(session, 2);

        Assert.Equal(["first", "second"], snapshots.Select(item => item.Source));
        Assert.All(snapshots, item => Assert.Equal(TelemetrySessionState.Polling, item.SessionState));
    }

    [Fact]
    public async Task WatchAsync_KeepsLastSnapshotWhileProviderReconnects()
    {
        var provider = new SequenceProvider(
            Snapshot("DinoVietNam"),
            new HttpRequestException("request failed with secret-token-value"));
        await using var session = new PollingTelemetrySession(
            provider,
            TimeSpan.FromMilliseconds(1),
            "DinoVietNam");

        var snapshots = await TakeAsync(session, 2);
        var reconnecting = snapshots[1];

        Assert.Equal(TelemetrySessionState.Reconnecting, reconnecting.SessionState);
        Assert.Equal("Utahraptor", reconnecting.Player?.Class);
        Assert.True(reconnecting.Success);
        Assert.DoesNotContain("secret-token-value", reconnecting.StatusMessage ?? string.Empty);
    }

    [Fact]
    public async Task WatchAsync_AuthenticationFailurePublishesLoginRequiredThenStops()
    {
        var provider = new SequenceProvider(new TestAuthenticationException("expired"));
        await using var session = new PollingTelemetrySession(
            provider,
            TimeSpan.FromMilliseconds(1),
            "EraGaming");

        var snapshots = await TakeAsync(session, 2);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("EraGaming", snapshot.Source);
        Assert.Equal(TelemetrySessionState.AuthenticationRequired, snapshot.SessionState);
        Assert.False(snapshot.Success);
    }

    private static TelemetrySnapshot Snapshot(string source) => new()
    {
        Source = source,
        Success = true,
        ServerOnline = true,
        PlayerOnline = true,
        Player = new PlayerTelemetry { Class = "Utahraptor" }
    };

    private static async Task<IReadOnlyList<TelemetrySnapshot>> TakeAsync(
        ITelemetrySession session,
        int count)
    {
        var snapshots = new List<TelemetrySnapshot>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var snapshot in session.WatchAsync(timeout.Token))
        {
            snapshots.Add(snapshot);
            if (snapshots.Count == count)
            {
                break;
            }
        }

        return snapshots;
    }

    private sealed class SequenceProvider(params object[] results) : ITelemetryProvider
    {
        private int _index;

        public Task<TelemetrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = results[Math.Min(_index++, results.Length - 1)];
            return result switch
            {
                TelemetrySnapshot snapshot => Task.FromResult(snapshot),
                Exception exception => Task.FromException<TelemetrySnapshot>(exception),
                _ => throw new InvalidOperationException()
            };
        }
    }

    private sealed class TestAuthenticationException(string message)
        : TelemetryAuthenticationException(message);
}
