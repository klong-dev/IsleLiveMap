using System.Runtime.CompilerServices;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotStatsRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrExpiredGpsDoesNotEraseProviderStats(bool expired)
    {
        var now = DateTimeOffset.UtcNow;
        var remote = ProviderSnapshot(now);
        LocalMovementObservation? gps = expired ? Observation(now.AddSeconds(-10)) : null;
        var result = LocalPositionSnapshotMerger.Merge(remote, gps, now, requireFreshLocalMovement: true);

        Assert.True(result.PlayerOnline);
        Assert.Equal(remote.Player!.ExactVitals, result.Player!.ExactVitals);
        Assert.Equal(remote.Player.Prime, result.Player.Prime);
        Assert.Equal(remote.UpdatedAt, result.UpdatedAt);
        Assert.Null(result.Player.Location);
        Assert.Null(result.Player.MapLocation);
        Assert.Null(result.Player.ExactMapHeadingDegrees);
    }

    [Fact]
    public void MissingGpsDoesNotMaskExpiredIslePilotAuthentication()
    {
        var remote = ProviderSnapshot(DateTimeOffset.UtcNow) with
        {
            Success = false,
            SessionState = TelemetrySessionState.AuthenticationRequired
        };
        foreach (var gps in new LocalMovementObservation?[] { null, Observation(DateTimeOffset.UtcNow) })
        {
            var result = LocalPositionSnapshotMerger.Merge(remote, gps, DateTimeOffset.UtcNow, requireFreshLocalMovement: true);
            Assert.Equal(TelemetrySessionState.AuthenticationRequired, result.SessionState);
            Assert.False(result.Success);
        }
    }

    [Fact]
    public void ProviderStaleFlagSurvivesGpsLoss()
    {
        var remote = ProviderSnapshot(DateTimeOffset.UtcNow) with
        { SessionState = TelemetrySessionState.Stale, LiveDataStale = true };
        var result = LocalPositionSnapshotMerger.Merge(remote, null, DateTimeOffset.UtcNow, requireFreshLocalMovement: true);
        Assert.Equal(remote.Player!.ExactVitals, result.Player!.ExactVitals);
        Assert.True(result.LiveDataStale);
        Assert.Equal(TelemetrySessionState.Stale, result.SessionState);
    }

    [Fact]
    public void MissingGpsDoesNotReviveOfflineOrLocalOnlyPlayer()
    {
        foreach (var remote in new[]
        {
            ProviderSnapshot(DateTimeOffset.UtcNow) with { PlayerOnline = false },
            new TelemetrySnapshot { Source = "PRO", Player = new PlayerTelemetry { Location = new WorldLocation { X = 100, Y = 200 } } }
        })
        {
            var result = LocalPositionSnapshotMerger.Merge(remote, null, DateTimeOffset.UtcNow, requireFreshLocalMovement: true);
            Assert.False(result.PlayerOnline);
            Assert.Null(result.Player);
        }
    }

    [Fact]
    public async Task RealFanInSessionPublishesIslePilotStatsWithoutAnyGpsPackets()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var remote = ProviderSnapshot(DateTimeOffset.UtcNow);
        await using var session = new LocalPositionTelemetrySession(new ProviderSession(remote), new SilentGps(), "ISLEPILOT", enableLocalVitals: false);
        await using var reader = session.WatchAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        while (await reader.MoveNextAsync())
        {
            if (reader.Current.UpdatedAt != remote.UpdatedAt) continue;
            Assert.True(reader.Current.PlayerOnline);
            Assert.Equal(750, reader.Current.Player?.ExactVitals?.Health);
            Assert.Equal(remote.Player!.Prime, reader.Current.Player?.Prime);
            Assert.Null(reader.Current.Player?.Location);
            return;
        }
        Assert.Fail("Provider snapshot was not published.");
    }

    private static TelemetrySnapshot ProviderSnapshot(DateTimeOffset now) => new()
    {
        Source = "IslePilot", Success = true, ServerOnline = true, PlayerOnline = true,
        SessionState = TelemetrySessionState.Live, UpdatedAt = now,
        Player = new PlayerTelemetry
        {
            Name = "fixture", Class = "carnotaurus", ExactVitalsSource = "IslePilotOverlayV2",
            ExactVitals = new ExactVitals { Health = 750, MaxHealth = 1000, Hunger = 250, MaxHunger = 400, Thirst = 350, MaxThirst = 500, Stamina = 90, MaxStamina = 100 },
            Prime = new PrimeTelemetry { Done = 2, Required = 5 },
            Location = new WorldLocation { X = 10, Y = 20 }, MapLocation = new MapPoint(0.1, 0.2), ExactMapHeadingDegrees = 120
        }
    };

    private static LocalMovementObservation Observation(DateTimeOffset now) => new(
        now, new UnrealMovementCandidate(100, 200, 300, 30, 1f, 64, 380, 26), "server:7777");

    private sealed class ProviderSession(TelemetrySnapshot snapshot) : ITelemetrySession
    {
        public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return snapshot;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentGps : ILocalMovementSource
    {
        public async IAsyncEnumerable<LocalMovementObservation> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
