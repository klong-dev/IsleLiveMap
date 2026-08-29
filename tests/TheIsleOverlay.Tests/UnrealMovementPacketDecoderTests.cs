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

    [Fact]
    public void Tracker_RejectsRetransmittedMovementWithOlderClientTimestamp()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-24T00:00:01Z");
        var current = Candidate(100, 100, 100, timestamp: 40f);
        var retransmitted = Candidate(450, 100, 100, timestamp: 39.5f);

        var selected = LocalMovementTracker.TryContinueTrack(
            [retransmitted],
            current,
            observedAt.AddMilliseconds(-50),
            observedAt,
            out _);

        Assert.False(selected);
    }

    [Fact]
    public void Tracker_PrefersNewestForwardMovementOverOlderSavedMove()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-24T00:00:01Z");
        var current = Candidate(100, 100, 100, timestamp: 40f);
        var retransmitted = Candidate(450, 100, 100, timestamp: 39.5f);
        var newest = Candidate(130, 110, 100, timestamp: 40.05f);

        var selected = LocalMovementTracker.TryContinueTrack(
            [retransmitted, newest],
            current,
            observedAt.AddMilliseconds(-50),
            observedAt,
            out var sample);

        Assert.True(selected);
        Assert.Equal(newest, sample);
    }

    [Fact]
    public void Tracker_RejectsTimestampRegressionEvenAfterWallClockDelay()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-24T00:00:01Z");
        var current = Candidate(100, 100, 100, timestamp: 229_678f);
        var validPositionWithLowerTimestamp = Candidate(140, 120, 100, timestamp: 134f);

        var selected = LocalMovementTracker.TryContinueTrack(
            [validPositionWithLowerTimestamp],
            current,
            observedAt.AddMilliseconds(-300),
            observedAt,
            out var sample);

        Assert.False(selected);
    }

    [Fact]
    public void Tracker_RejectsImplausibleTimestampJump()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-24T00:00:01Z");
        var current = Candidate(100, 100, 100, timestamp: 134f);
        var validPositionWithPoisonedTimestamp = Candidate(140, 120, 100, timestamp: 229_678f);

        var selected = LocalMovementTracker.TryContinueTrack(
            [validPositionWithPoisonedTimestamp],
            current,
            observedAt.AddMilliseconds(-50),
            observedAt,
            out var sample);

        Assert.False(selected);
    }

    [Fact]
    public void Tracker_RejectsDifferentPositionWithDuplicateTimestamp()
    {
        var observedAt = DateTimeOffset.Parse("2026-08-27T04:00:01Z");
        var current = Candidate(138_577, -209_973, 26_537, timestamp: 333.630f);
        var duplicateMove = Candidate(138_673, -209_973, 26_537, timestamp: 333.630f);

        Assert.False(LocalMovementTracker.TryContinueTrack(
            [duplicateMove],
            current,
            observedAt.AddMilliseconds(-60),
            observedAt,
            out _));
    }

    [Fact]
    public void Tracker_DoesNotRebootstrapDistantLayoutAfterLongStationaryGap()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var startedAt = DateTimeOffset.Parse("2026-08-27T04:00:00Z");
        var initialPayload = Convert.FromHexString(MovingPayload);
        for (var index = 0; index <= 12; index++)
        {
            tracker.TryTrack(
                initialPayload,
                startedAt.AddMilliseconds(index * 50),
                out _);
        }

        var distantPayload = Convert.FromHexString(StationaryPayload);
        for (var index = 0; index <= 12; index++)
        {
            Assert.False(tracker.TryTrack(
                distantPayload,
                startedAt.AddSeconds(31).AddMilliseconds(index * 50),
                out _));
        }
    }

    [Fact]
    public void Tracker_ReacquiresStableNearbyCanonicalMovementAfterLosingContinuity()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var current = Candidate(152_631, -255_094, 37_011, timestamp: 40f);
        var startedAt = DateTimeOffset.Parse("2026-08-27T06:00:00Z");

        for (var index = 0; index < 12; index++)
        {
            var candidate = Candidate(
                159_018 + index * 40,
                -271_844 + index * 50,
                33_040,
                timestamp: 172f + index * 0.05f);
            Assert.False(tracker.TryRecoverTrack(
                [candidate],
                current,
                startedAt.AddMilliseconds(index * 50),
                out _));
        }

        var recovered = Candidate(
            159_498,
            -271_244,
            33_040,
            timestamp: 172.6f);
        Assert.True(tracker.TryRecoverTrack(
            [recovered],
            current,
            startedAt.AddMilliseconds(600),
            out var sample));
        Assert.Equal(recovered, sample);
    }

    [Fact]
    public void Tracker_DoesNotRecoverFromStableLowBitOverlap()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var current = Candidate(138_577, -209_973, 26_537, timestamp: 336.745f);
        var startedAt = DateTimeOffset.Parse("2026-08-27T06:00:00Z");

        for (var index = 0; index <= 20; index++)
        {
            var overlap = Candidate(
                137_000 + index,
                -208_000 + index,
                4_085,
                timestamp: 337f + index * 0.05f,
                componentBits: 20);
            Assert.False(tracker.TryRecoverTrack(
                [overlap],
                current,
                startedAt.AddMilliseconds(index * 50),
                out _));
        }
    }

    [Fact]
    public void Tracker_ReplacesDistantStaleLockAfterSustainedCanonicalChain()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var wrongLock = Candidate(
            34_079,
            -28_844,
            4_085,
            timestamp: 40f,
            componentBits: 23);
        var startedAt = DateTimeOffset.Parse("2026-08-27T06:00:00Z");
        UnrealMovementCandidate recovered = default;
        var didRecover = false;

        for (var index = 0; index < 24; index++)
        {
            var candidate = Candidate(
                111_344.3,
                -246_597.7,
                30_289.4,
                timestamp: 159f + index * 0.075f);
            didRecover = tracker.TryRecoverDistantTrack(
                [candidate],
                wrongLock,
                startedAt,
                startedAt.AddSeconds(2).AddMilliseconds(index * 75),
                out recovered);
            if (index < 23)
            {
                Assert.False(didRecover);
            }
        }

        Assert.True(didRecover);
        Assert.Equal(111_344.3, recovered.X, precision: 1);
        Assert.Equal(-246_597.7, recovered.Y, precision: 1);
    }

    [Fact]
    public void Tracker_ReacquiresLowerBitCanonicalLayoutAfterRespawn()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var oldPawn = Candidate(
            140_424.3,
            -210_803.2,
            26_262.5,
            timestamp: 900f,
            componentBits: 26);
        var lastOldMoveAt = DateTimeOffset.Parse("2026-08-28T07:12:00Z");
        UnrealMovementCandidate recovered = default;
        var didRecover = false;

        // Captured after an actual respawn: the new pawn used a 24-bit layout
        // at (53330, -32144, 38832) with a steadily advancing timestamp.
        for (var index = 0; index < 24; index++)
        {
            var newPawn = Candidate(
                53_330.12,
                -32_143.69,
                38_831.91,
                timestamp: 65.62f + index * 0.21f,
                componentBits: 24);
            didRecover = tracker.TryRecoverDistantTrack(
                [newPawn],
                oldPawn,
                lastOldMoveAt,
                lastOldMoveAt.AddSeconds(2).AddMilliseconds(index * 75),
                out recovered);
            if (index < 23)
            {
                Assert.False(didRecover);
            }
        }

        Assert.True(didRecover);
        Assert.Equal(53_330.12, recovered.X, precision: 1);
        Assert.Equal(-32_143.69, recovered.Y, precision: 1);
        Assert.Equal(24, recovered.ComponentBitCount);
    }

    [Fact]
    public void Tracker_DoesNotReplaceDistantLockWhileItIsStillFresh()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var current = Candidate(140_424, -210_803, 26_263, timestamp: 900f);
        var observedAt = DateTimeOffset.Parse("2026-08-28T07:12:01Z");

        Assert.False(tracker.TryRecoverDistantTrack(
            [Candidate(53_330, -32_144, 38_832, timestamp: 65.62f, componentBits: 24)],
            current,
            observedAt.AddMilliseconds(-500),
            observedAt,
            out _));
    }

    [Fact]
    public void Tracker_DoesNotReplaceDistantLockFromReplayedDuplicatePacket()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var current = Candidate(140_424, -210_803, 26_263, timestamp: 900f);
        var lastOldMoveAt = DateTimeOffset.Parse("2026-08-28T07:12:00Z");
        var duplicate = Candidate(
            53_330,
            -32_144,
            38_832,
            timestamp: 65.62f,
            componentBits: 24);

        for (var index = 0; index < 40; index++)
        {
            Assert.False(tracker.TryRecoverDistantTrack(
                [duplicate],
                current,
                lastOldMoveAt,
                lastOldMoveAt.AddSeconds(2).AddMilliseconds(index * 75),
                out _));
        }
    }

    [Fact]
    public void Tracker_DoesNotReplaceDistantLockFromShortOrLowBitChain()
    {
        var tracker = new LocalMovementTracker(_decoder);
        var current = Candidate(
            111_344,
            -246_598,
            30_289,
            timestamp: 159f,
            componentBits: 23);
        var startedAt = DateTimeOffset.Parse("2026-08-27T06:00:00Z");
        for (var index = 0; index < 40; index++)
        {
            var componentBits = index < 16 ? 26 : 20;
            var overlap = Candidate(
                34_079,
                -28_844,
                4_085,
                timestamp: 160f + index * 0.075f,
                componentBits: componentBits);
            Assert.False(tracker.TryRecoverDistantTrack(
                [overlap],
                current,
                startedAt,
                startedAt.AddSeconds(2).AddMilliseconds(index * 75),
                out _));
        }
    }

    private static UnrealMovementCandidate Candidate(
        double x,
        double y,
        double z,
        float timestamp,
        int componentBits = 26) => new(
        x,
        y,
        z,
        0d,
        timestamp,
        65,
        380,
        componentBits);
}
