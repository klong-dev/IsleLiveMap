namespace TheIsleOverlay.LocalTelemetry;

public sealed class LocalMovementTracker
{
    private static readonly TimeSpan HypothesisLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumBootstrapDuration = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan MaximumReadyAge = TimeSpan.FromMilliseconds(250);
    private const int RequiredConsecutiveHits = 8;
    private const float TimestampRegressionTolerance = 0.002f;
    private const float TimestampAdvanceAllowanceSeconds = 2f;
    private const float TimestampWallClockMultiplier = 4f;
    private const double MaximumBaseDelta = 5_000d;
    private const double MaximumUnitsPerSecond = 100_000d;
    private const double MaximumSameTimestampDelta = 1d;
    private const double MaximumRecoveryDelta = 100_000d;
    private const int MinimumRecoveryComponentBits = 23;
    private static readonly TimeSpan MaximumContinuityElapsed = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RecoveryLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumRecoveryDuration = TimeSpan.FromMilliseconds(600);
    private const int RequiredRecoveryHits = 8;
    private static readonly TimeSpan DistantRecoveryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DistantHypothesisLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumDistantRecoveryDuration = TimeSpan.FromMilliseconds(1_500);
    private static readonly TimeSpan MaximumDistantReadyAge = TimeSpan.FromMilliseconds(250);
    private const int RequiredDistantRecoveryHits = 24;

    private readonly UnrealMovementPacketDecoder _decoder;
    private readonly Dictionary<MovementLayout, Hypothesis> _hypotheses = [];
    private UnrealMovementCandidate? _current;
    private RecoveryHypothesis? _recovery;
    private readonly Dictionary<MovementLayout, Hypothesis> _distantHypotheses = [];
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
        if (_current is { } current)
        {
            if (TryRecoverDistantTrack(
                    candidates,
                    current,
                    _lastLockUpdate,
                    observedAt,
                    out sample))
            {
                _current = sample;
                _lastLockUpdate = observedAt;
                _recovery = null;
                _distantHypotheses.Clear();
                return true;
            }

            if (TryContinueTrack(candidates, current, _lastLockUpdate, observedAt, out sample))
            {
                _current = sample;
                _lastLockUpdate = observedAt;
                _recovery = null;
                return true;
            }

            // A timestamp discontinuity or a short capture gap can leave the
            // lock behind the real player. Recover only from a stable nearby
            // chain of canonical 23+ bit samples. The recurring live false
            // cluster was 18-22 bits and roughly 252k units away, so it cannot
            // qualify for this path.
            if (TryRecoverTrack(candidates, current, observedAt, out sample))
            {
                _current = sample;
                _lastLockUpdate = observedAt;
                _recovery = null;
                return true;
            }

            sample = default;
            return false;
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
            _recovery = null;
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
        _recovery = null;
        _distantHypotheses.Clear();
        _hypotheses.Clear();
        _lastLockUpdate = default;
    }

    internal bool TryRecoverTrack(
        IReadOnlyList<UnrealMovementCandidate> candidates,
        UnrealMovementCandidate current,
        DateTimeOffset observedAt,
        out UnrealMovementCandidate sample)
    {
        var eligible = candidates
            .Where(candidate =>
                candidate.ComponentBitCount >= MinimumRecoveryComponentBits
                && candidate.ClientTimestamp > 0f
                && Distance(current, candidate) <= MaximumRecoveryDelta)
            .ToArray();
        if (eligible.Length == 0)
        {
            if (_recovery is { } stale
                && observedAt - stale.LastSeen > RecoveryLifetime)
            {
                _recovery = null;
            }

            sample = default;
            return false;
        }

        UnrealMovementCandidate selected;
        if (_recovery is { } recovery
            && observedAt - recovery.LastSeen <= RecoveryLifetime)
        {
            var elapsedSeconds = Math.Max(0d, (observedAt - recovery.LastSeen).TotalSeconds);
            var continuing = eligible
                .Where(candidate =>
                    IsContinuous(recovery.Candidate, recovery.LastSeen, candidate, observedAt)
                    && IsPlausibleForwardTimestamp(
                        recovery.Candidate,
                        candidate,
                        elapsedSeconds)
                    && !IsSameTimestampMovement(recovery.Candidate, candidate))
                .OrderByDescending(candidate => candidate.ClientTimestamp)
                .ThenBy(candidate => Distance(recovery.Candidate, candidate))
                .ThenByDescending(candidate => candidate.ComponentBitCount)
                .ToArray();
            if (continuing.Length > 0)
            {
                selected = continuing[0];
                recovery = recovery with
                {
                    Candidate = selected,
                    LastSeen = observedAt,
                    ConsecutiveHits = recovery.ConsecutiveHits + 1
                };
                _recovery = recovery;
                if (recovery.ConsecutiveHits >= RequiredRecoveryHits
                    && observedAt - recovery.FirstSeen >= MinimumRecoveryDuration)
                {
                    sample = selected;
                    return true;
                }

                sample = default;
                return false;
            }
        }

        selected = eligible
            .OrderByDescending(candidate => candidate.ComponentBitCount)
            .ThenBy(candidate => Distance(current, candidate))
            .ThenByDescending(candidate => candidate.ClientTimestamp)
            .First();
        _recovery = new RecoveryHypothesis(
            selected,
            observedAt,
            observedAt,
            1);
        sample = default;
        return false;
    }

    internal bool TryRecoverDistantTrack(
        IReadOnlyList<UnrealMovementCandidate> candidates,
        UnrealMovementCandidate current,
        DateTimeOffset lastLockUpdate,
        DateTimeOffset observedAt,
        out UnrealMovementCandidate sample)
    {
        // A respawn can change both the movement layout and its component bit
        // count. Do not compare the new layout's precision with the old pawn:
        // a real 24-bit respawn must be able to replace a stale 26-bit lock.
        // Instead, require the old lock to have gone quiet and make the new
        // canonical layout prove a sustained timestamp-forward chain.
        if (observedAt < lastLockUpdate
            || observedAt - lastLockUpdate < DistantRecoveryDelay)
        {
            _distantHypotheses.Clear();
            sample = default;
            return false;
        }

        foreach (var layout in _distantHypotheses
                     .Where(pair => observedAt < pair.Value.LastSeen
                                    || observedAt - pair.Value.LastSeen
                                    > DistantHypothesisLifetime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _distantHypotheses.Remove(layout);
        }

        foreach (var candidate in candidates.Where(candidate =>
                     candidate.ComponentBitCount >= MinimumRecoveryComponentBits
                     && candidate.ClientTimestamp > 0f
                     && Distance(current, candidate) > MaximumRecoveryDelta))
        {
            if (!_distantHypotheses.TryGetValue(candidate.Layout, out var hypothesis)
                || observedAt - hypothesis.LastSeen > DistantHypothesisLifetime
                || !IsContinuous(hypothesis.Candidate, hypothesis.LastSeen, candidate, observedAt)
                || !IsStrictlyForwardTimestamp(
                    hypothesis.Candidate,
                    candidate,
                    Math.Max(0d, (observedAt - hypothesis.LastSeen).TotalSeconds)))
            {
                _distantHypotheses[candidate.Layout] = new Hypothesis(
                    candidate,
                    observedAt,
                    observedAt,
                    1);
                continue;
            }

            _distantHypotheses[candidate.Layout] = hypothesis with
            {
                Candidate = candidate,
                LastSeen = observedAt,
                ConsecutiveHits = hypothesis.ConsecutiveHits + 1
            };
        }

        var ready = _distantHypotheses.Values
            .Where(hypothesis =>
                hypothesis.ConsecutiveHits >= RequiredDistantRecoveryHits
                && observedAt - hypothesis.FirstSeen >= MinimumDistantRecoveryDuration
                && observedAt - hypothesis.LastSeen <= MaximumDistantReadyAge)
            .OrderByDescending(hypothesis => hypothesis.ConsecutiveHits)
            .ThenByDescending(hypothesis =>
                Math.Abs(hypothesis.Candidate.X) + Math.Abs(hypothesis.Candidate.Y))
            .ThenByDescending(hypothesis => hypothesis.Candidate.ClientTimestamp)
            .FirstOrDefault();
        if (ready is null)
        {
            sample = default;
            return false;
        }

        sample = ready.Candidate;
        return true;
    }

    internal static bool TryContinueTrack(
        IReadOnlyList<UnrealMovementCandidate> candidates,
        UnrealMovementCandidate current,
        DateTimeOffset lastUpdate,
        DateTimeOffset observedAt,
        out UnrealMovementCandidate sample)
    {
        var elapsedSeconds = Math.Max(0d, (observedAt - lastUpdate).TotalSeconds);
        var continuous = candidates
            .Where(candidate => IsContinuous(current, lastUpdate, candidate, observedAt))
            .Select(candidate => new TimestampRankedCandidate(
                candidate,
                IsPlausibleForwardTimestamp(current, candidate, elapsedSeconds)))
            .Where(item => item.TimestampPlausible
                           && !IsSameTimestampMovement(current, item.Candidate))
            .OrderByDescending(item => item.Candidate.ClientTimestamp)
            .ThenBy(item => Distance(current, item.Candidate))
            .ThenBy(candidate => candidate.LocationBitOffset)
            .ToArray();

        if (continuous.Length == 0)
        {
            sample = default;
            return false;
        }

        sample = continuous[0].Candidate;
        return true;
    }

    private static bool IsSameTimestampMovement(
        UnrealMovementCandidate current,
        UnrealMovementCandidate candidate) =>
        Math.Abs(candidate.ClientTimestamp - current.ClientTimestamp)
        <= TimestampRegressionTolerance
        && Distance(current, candidate) > MaximumSameTimestampDelta;

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

    private static bool IsStrictlyForwardTimestamp(
        UnrealMovementCandidate current,
        UnrealMovementCandidate candidate,
        double elapsedSeconds)
    {
        var delta = candidate.ClientTimestamp - current.ClientTimestamp;
        return delta > TimestampRegressionTolerance
               && IsPlausibleForwardTimestamp(current, candidate, elapsedSeconds);
    }

    private static bool IsContinuous(
        UnrealMovementCandidate previous,
        DateTimeOffset previousAt,
        UnrealMovementCandidate current,
        DateTimeOffset currentAt)
    {
        var elapsedSeconds = Math.Max(
            0d,
            Math.Min(
                (currentAt - previousAt).TotalSeconds,
                MaximumContinuityElapsed.TotalSeconds));
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

    private sealed record RecoveryHypothesis(
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
