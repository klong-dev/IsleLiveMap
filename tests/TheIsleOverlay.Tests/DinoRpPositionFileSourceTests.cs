using System.IO;
using System.Text.Json;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class DinoRpPositionFileSourceTests
{
    [Fact]
    public void FreshVoicePosition_IsAcceptedWithOriginalTimestampAndCoordinates()
    {
        var now = DateTimeOffset.UtcNow;
        var path = WritePosition(now.AddMilliseconds(-400), 1024, -2048, 42, 135);
        try
        {
            Assert.True(DinoRpPositionFileSource.TryReadPosition(path, now, out var observedAt, out var movement));
            Assert.Equal(1024, movement.X);
            Assert.Equal(-2048, movement.Y);
            Assert.Equal(42, movement.Z);
            Assert.Equal(135, movement.UnrealYawDegrees);
            Assert.Equal(now.AddMilliseconds(-400), observedAt);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(-3, 0, 0)]
    [InlineData(0, 700000, 0)]
    [InlineData(0, 0, -700000)]
    public void StaleOrOutOfWorldPosition_IsRejected(int secondsOffset, double x, double y)
    {
        var now = DateTimeOffset.UtcNow;
        var path = WritePosition(now.AddSeconds(secondsOffset), x, y, 0, 0);
        try
        {
            Assert.False(DinoRpPositionFileSource.TryReadPosition(path, now, out _, out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OtherPublisher_IsNotAcceptedAsDinoRpBridge()
    {
        var path = WritePosition(DateTimeOffset.UtcNow, 1, 2, 3, 4, "OtherApp");
        try
        {
            Assert.False(DinoRpPositionFileSource.TryReadPosition(path, DateTimeOffset.UtcNow, out _, out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WaitingStatus_DoesNotClaimHubStatsOrGpsAreLive()
    {
        var snapshot = LocalPositionSnapshotMerger.Waiting("DINORP");
        Assert.False(snapshot.PlayerOnline);
        Assert.Contains("voice bridge", snapshot.StatusMessage);
        Assert.Contains("stats Hub chưa được tích hợp", snapshot.StatusMessage);
    }

    [Fact]
    public async Task FreshFile_IsNotPublishedUntilGameEndpointIsVerified()
    {
        var path = WritePosition(DateTimeOffset.UtcNow, 1200, -3400, 5, 30);
        var capture = new FakeCapture();
        await using var source = new DinoRpPositionFileSource(capture, path);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await using var reader = source.WatchAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        try
        {
            var pending = reader.MoveNextAsync().AsTask();
            await Task.Delay(350);
            Assert.False(pending.IsCompleted);
            capture.HasDinoRpTraffic = true;
            Assert.True(await pending);
            Assert.Equal(DinoRpPositionFileSource.ServerEndpoint, reader.Current.ServerEndpoint);
            Assert.Equal(1200, reader.Current.Movement.X);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PositionExpiry_RemovesLocalGpsRatherThanReusingOldPoint()
    {
        var now = DateTimeOffset.UtcNow;
        var local = new LocalMovementObservation(
            now,
            new UnrealMovementCandidate(1200, -3400, 5, 30, 0, 0, 0, 0),
            DinoRpPositionFileSource.ServerEndpoint);
        var live = LocalPositionSnapshotMerger.Merge(
            null, local, now, "DINORP", requireFreshLocalMovement: true);
        Assert.True(live.PlayerOnline);
        Assert.Equal(DinoRpPositionFileSource.ServerEndpoint, live.Player?.ServerEndpoint);
        Assert.Contains("GPS DINORP đang hoạt động", live.StatusMessage);

        var expired = LocalPositionSnapshotMerger.Merge(
            live, local, now.AddSeconds(3), "DINORP", requireFreshLocalMovement: true);
        // Hard-truth mode: expired GPS no longer strips the snapshot down to
        // the waiting state — the last player passes through.
        Assert.True(expired.PlayerOnline);
        Assert.NotNull(expired.Player);
    }

    [Fact]
    public void ReconnectToAnotherServer_InvalidatesOldEndpointEvidence()
    {
        var now = DateTimeOffset.UtcNow;
        var capture = new NpcapLocalMovementSource();
        capture.RecordCapturedOutboundEndpoint(DinoRpPositionFileSource.ServerEndpoint, now.AddSeconds(-2));
        Assert.True(capture.HasRecentOutboundTraffic(
            DinoRpPositionFileSource.ServerEndpoint, now, DinoRpPositionFileSource.EndpointFreshness));

        capture.RecordCapturedOutboundEndpoint("203.0.113.20:7777", now.AddSeconds(-1));
        Assert.False(capture.HasRecentOutboundTraffic(
            DinoRpPositionFileSource.ServerEndpoint, now, DinoRpPositionFileSource.EndpointFreshness));
    }

    private sealed class FakeCapture : ILocalMovementSource, IGameEndpointEvidenceSource
    {
        public bool HasDinoRpTraffic { get; set; }

        public bool HasRecentOutboundTraffic(string endpoint, DateTimeOffset now, TimeSpan maxAge) =>
            HasDinoRpTraffic && endpoint == DinoRpPositionFileSource.ServerEndpoint;

        public async IAsyncEnumerable<LocalMovementObservation> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static string WritePosition(
        DateTimeOffset observedAt, double x, double y, double z, double yaw, string source = "IsleVOIP")
    {
        var path = Path.Combine(Path.GetTempPath(), $"dinorp-position-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            version = 1,
            source,
            x,
            y,
            z,
            yaw,
            updatedAt = observedAt.ToString("O")
        }));
        return path;
    }
}
