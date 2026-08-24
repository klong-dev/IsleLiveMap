namespace TheIsleOverlay.LocalTelemetry;

public sealed class LocalMovementTracker
{
    private static readonly TimeSpan HypothesisLifetime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LockLifetime = TimeSpan.FromSeconds(2);
    private const int RequiredConsecutiveHits = 4;
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
            && observedAt - _lastLockUpdate <= LockLifetime
            && TryContinueTrack(candidates, current, observedAt, out sample))
        {
            _current = sample;
            _lastLockUpdate = observedAt;
            return true;
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
                _hypotheses[candidate.Layout] = new Hypothesis(candidate, observedAt, 1);
                continue;
            }

            hypothesis = hypothesis with
            {
                Candidate = candidate,
                LastSeen = observedAt,
                ConsecutiveHits = hypothesis.ConsecutiveHits + 1
            };
            _hypotheses[candidate.Layout] = hypothesis;
            if (hypothesis.ConsecutiveHits < RequiredConsecutiveHits)
            {
                continue;
            }

            _current = candidate;
            _lastLockUpdate = observedAt;
            sample = candidate;
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

    private bool TryContinueTrack(
        IReadOnlyList<UnrealMovementCandidate> candidates,
        UnrealMovementCandidate current,
        DateTimeOffset observedAt,
        out UnrealMovementCandidate sample)
    {
        var continuous = candidates
            .Where(candidate => IsContinuous(current, _lastLockUpdate, candidate, observedAt))
            .OrderByDescending(candidate => candidate.ClientTimestamp)
            .ThenBy(candidate => Distance(current, candidate))
            .ThenBy(candidate => candidate.LocationBitOffset)
            .ToArray();

        if (continuous.Length == 0)
        {
            sample = default;
            return false;
        }

        sample = continuous[0];
        return true;
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
        DateTimeOffset LastSeen,
        int ConsecutiveHits);
}
