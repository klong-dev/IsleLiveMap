namespace TheIsleOverlay.LocalTelemetry;

public sealed class LocalMovementTracker
{
    private static readonly TimeSpan HypothesisLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LockLifetime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumBootstrapDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan MaximumReadyAge = TimeSpan.FromMilliseconds(250);
    private const int RequiredConsecutiveHits = 8;
    private const float TimestampRegressionTolerance = 0.002f;
    private const float TimestampAdvanceAllowanceSeconds = 2f;
    private const float TimestampWallClockMultiplier = 4f;
    private static readonly TimeSpan TimestampRecoveryAge = TimeSpan.FromMilliseconds(250);
    private const double MaximumBaseDelta = 5_000d;
    private const double MaximumUnitsPerSecond = 100_000d;

    private readonly UnrealMovementPacketDecoder _decoder;
    private readonly Dictionary<MovementLayout, Hypothesis> _hypotheses = [];
    private UnrealMovementCandidate? _current;
    private DateTimeOffset _lastLockUpdate;

    public LocalMovementTracker(UnrealMovementPacketDecoder? decoder = null)
    {
        _decoder = decoder ?? new UnrealMovementPacketDecoder();
    }

    public bool TryTrack(
        ReadOnlySpan<byte> payload,
        DateTimeOffset observedAt,
        out UnrealMovementCandidate sample)
    {
        var candidates = _decoder.Decode(payload);
        if (_current is { } current
            && observedAt - _lastLockUpdate <= LockLifetime)
        {
            if (TryContinueTrack(candidates, current, _lastLockUpdate, observedAt, out sample))
            {
                _current = sample;
                _lastLockUpdate = observedAt;
                return true;
            }

            // UE may resend saved moves while airborne. Their positions are
            // spatially plausible but older than the sample already rendered;
            // never let the bootstrap hypotheses reacquire one of them.
            if (candidates.Any(candidate =>
                    IsContinuous(current, _lastLockUpdate, candidate, observedAt)))
            {
                sample = default;
                return false;
            }
        }

        if (_current is not null && observedAt - _lastLockUpdate > LockLifetime)
        {
            _current = null;
            _hypotheses.Clear();
        }

        PruneHypotheses(observedAt);
        foreach (var candidate in candidates)
        {
            if (!_hypotheses.TryGetValue(candidate.Layout, out var hypothesis)
                || observedAt - hypothesis.LastSeen > HypothesisLifetime
                || !IsContinuous(hypothesis.Candidate, hypothesis.LastSeen, candidate, observedAt))
            {
                _hypotheses[candidate.Layout] = new Hypothesis(
                    candidate,
                    observedAt,
                    observedAt,
                    1);
                continue;
            }

            hypothesis = hypothesis with
            {
                Candidate = candidate,
                LastSeen = observedAt,
                ConsecutiveHits = hypothesis.ConsecutiveHits + 1
            };
            _hypotheses[candidate.Layout] = hypothesis;
        }

        var ready = _hypotheses.Values
            .Where(hypothesis =>
                hypothesis.ConsecutiveHits >= RequiredConsecutiveHits
                && observedAt - hypothesis.FirstSeen >= MinimumBootstrapDuration
                && observedAt - hypothesis.LastSeen <= MaximumReadyAge)
            .OrderByDescending(hypothesis => hypothesis.ConsecutiveHits)
            .ThenByDescending(hypothesis => hypothesis.Candidate.ComponentBitCount)
            .ThenByDescending(hypothesis =>
                Math.Abs(hypothesis.Candidate.X) + Math.Abs(hypothesis.Candidate.Y))
            .FirstOrDefault();
        if (ready is not null)
        {
            _current = ready.Candidate;
            _lastLockUpdate = observedAt;
            _hypotheses.Clear();
            sample = ready.Candidate;
            return true;
        }

        sample = default;
        return false;
    }

    public void Reset()
    {
        _current = null;
        _hypotheses.Clear();
        _lastLockUpdate = default;
    }

    internal static bool TryContinueTrack(
        IReadOnlyList<UnrealMovementCandidate> candidates,
        UnrealMovementCandidate current,
        DateTimeOffset lastUpdate,
        DateTimeOffset observedAt,
        out UnrealMovementCandidate sample)
    {
        var elapsed = observedAt - lastUpdate;
        var elapsedSeconds = Math.Max(0d, elapsed.TotalSeconds);
        var continuous = candidates
            .Where(candidate => IsContinuous(current, lastUpdate, candidate, observedAt))
            .Select(candidate => new TimestampRankedCandidate(
                candidate,
                IsPlausibleForwardTimestamp(current, candidate, elapsedSeconds)))
            .Where(item =>
                item.TimestampPlausible
                || item.Candidate.ClientTimestamp + TimestampRegressionTolerance >= current.ClientTimestamp
                || elapsed >= TimestampRecoveryAge)
            .OrderByDescending(item => item.TimestampPlausible)
            .ThenByDescending(item => item.TimestampPlausible
                ? item.Candidate.ClientTimestamp
                : float.MinValue)
            .ThenBy(item => Distance(current, item.Candidate))
            .ThenBy(candidate => candidate.LocationBitOffset)
            .ToArray();

        if (continuous.Length == 0)
        {
            sample = default;
            return false;
        }

        var selected = continuous[0];
        sample = selected.TimestampPlausible
            ? selected.Candidate
            : selected.Candidate with
            {
                ClientTimestamp = current.ClientTimestamp + (float)elapsedSeconds
            };
        return true;
    }

    private static bool IsPlausibleForwardTimestamp(
        UnrealMovementCandidate current,
        UnrealMovementCandidate candidate,
        double elapsedSeconds)
    {
        var delta = candidate.ClientTimestamp - current.ClientTimestamp;
        var maximumAdvance = TimestampAdvanceAllowanceSeconds
                             + (float)elapsedSeconds * TimestampWallClockMultiplier;
        return delta >= -TimestampRegressionTolerance && delta <= maximumAdvance;
    }

    private static bool IsContinuous(
        UnrealMovementCandidate previous,
        DateTimeOffset previousAt,
        UnrealMovementCandidate current,
        DateTimeOffset currentAt)
    {
        var elapsedSeconds = Math.Max(0d, (currentAt - previousAt).TotalSeconds);
        var maximumDelta = MaximumBaseDelta + MaximumUnitsPerSecond * elapsedSeconds;
        return Distance(previous, current) <= maximumDelta;
    }

    private static double Distance(UnrealMovementCandidate left, UnrealMovementCandidate right)
    {
        var deltaX = right.X - left.X;
        var deltaY = right.Y - left.Y;
        var deltaZ = right.Z - left.Z;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ);
    }

    private void PruneHypotheses(DateTimeOffset observedAt)
    {
        foreach (var layout in _hypotheses
                     .Where(pair => observedAt - pair.Value.LastSeen > HypothesisLifetime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _hypotheses.Remove(layout);
        }
    }

    private sealed record Hypothesis(
        UnrealMovementCandidate Candidate,
        DateTimeOffset FirstSeen,
        DateTimeOffset LastSeen,
        int ConsecutiveHits);

    private readonly record struct TimestampRankedCandidate(
        UnrealMovementCandidate Candidate,
        bool TimestampPlausible)
    {
        public int LocationBitOffset => Candidate.LocationBitOffset;
    }
}
