using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class UnrealMovementPacketDecoderTests
{
    private const string MovingPayload =
        "0000000000000000000000000000000000000000000000000000000000000000000000000022DA49434FFDA3CA0100A0D52153071F18365F8E0F5EE28FD60330";

    private const string StationaryPayload =
        "000000000000000000000000000000000000000000000000000000000000000000000000B79D23434900002A6D63F044B54C8970F9D713D44FBFB122080960";

    private const string CompetingCandidatePayload =
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000004ADC1A424168F9F4A4110BA14D30DE832407D71F0006";

    private readonly UnrealMovementPacketDecoder _decoder = new();

    [Fact]
    public void Decode_FindsRealMovementLocationAndControlYaw()
    {
        var candidates = _decoder.Decode(Convert.FromHexString(MovingPayload));

        var movement = Assert.Single(candidates, candidate =>
            Math.Abs(candidate.X - 153_610.82d) < 0.01d
            && Math.Abs(candidate.Y - 283_609.52d) < 0.01d);

        Assert.Equal(20_389.74d, movement.Z, precision: 2);
        Assert.Equal(172.71d, movement.UnrealYawDegrees, precision: 2);
        Assert.Equal(262.71d, movement.MapHeadingDegrees, precision: 2);
        Assert.Equal(380, movement.LocationBitOffset);
        Assert.Equal(26, movement.ComponentBitCount);
    }

    [Fact]
    public void Decode_RejectsOverlappingStableFalseVector()
    {
        var candidates = _decoder.Decode(Convert.FromHexString(StationaryPayload));

        Assert.Contains(candidates, candidate =>
            Math.Abs(candidate.X - 442_020.33d) < 0.01d
            && Math.Abs(candidate.Y + 162_148.37d) < 0.01d
            && Math.Abs(candidate.Z - 26_009.46d) < 0.01d);
        Assert.DoesNotContain(candidates, candidate =>
            Math.Abs(candidate.X - 180_719.49d) < 0.01d
            && Math.Abs(candidate.Y - 89_980.69d) < 0.01d);
    }

    [Fact]
    public void Decode_ReadsYawWhilePlayerIsStationary()
    {
        var movement = Assert.Single(
            _decoder.Decode(Convert.FromHexString(StationaryPayload)),
            candidate => Math.Abs(candidate.X - 442_020.33d) < 0.01d);

        Assert.Equal(60.62d, movement.UnrealYawDegrees, precision: 2);
        Assert.Equal(150.62d, movement.MapHeadingDegrees, precision: 2);
    }

    [Fact]
    public void Tracker_LocksAfterStableBootstrapWindow()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var payload = Convert.FromHexString(StationaryPayload);
        var startedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z");

        for (var index = 0; index < 12; index++)
        {
            Assert.False(tracker.TryTrack(
                payload,
                startedAt.AddMilliseconds(index * 50),
                out _));
        }

        Assert.True(tracker.TryTrack(payload, startedAt.AddMilliseconds(600), out var sample));
        Assert.Equal(442_020.33d, sample.X, precision: 2);
        Assert.Equal(-162_148.37d, sample.Y, precision: 2);
    }

    [Fact]
    public void Tracker_LocksRealWorldVectorFromLivePayload()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var payload = Convert.FromHexString(CompetingCandidatePayload);
        var decoded = _decoder.Decode(payload);
        Assert.Contains(decoded, candidate =>
            Math.Abs(candidate.X - 137_939.16d) < 0.01d
            && Math.Abs(candidate.Y - 285_822.42d) < 0.01d);
        var startedAt = DateTimeOffset.Parse("2026-08-24T00:00:00Z");
        UnrealMovementCandidate tracked = default;
        for (var index = 0; index <= 12; index++)
        {
            tracker.TryTrack(
                payload,
                startedAt.AddMilliseconds(index * 50),
                out tracked);
        }

        Assert.Equal(137_939.16d, tracked.X, precision: 2);
        Assert.Equal(285_822.42d, tracked.Y, precision: 2);
        Assert.Equal(22.38d, tracked.UnrealYawDegrees, precision: 2);
    }
}
