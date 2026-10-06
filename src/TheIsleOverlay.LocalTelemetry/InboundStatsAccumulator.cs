using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>Internal in-map trial. A unique candidate is not structural local-owner proof.</summary>
public sealed class InboundStatsAccumulator
{
    private readonly ExperimentalVitalsDecoder _decoder = new();
    private readonly Dictionary<ulong, Dictionary<string, VitalsFieldEvidence>> _owners = [];
    private readonly InboundVitalsDeltaState _state = new();
    private DateTimeOffset _lastPresence;
    private DateTimeOffset _lastPublished;
    private string? _flow;
    private DateTimeOffset _lastAt;
    private readonly InboundStatsRestartCache? _restartCache;
    private bool _restoreAttempted;
    public InboundStatsAccumulator(InboundStatsRestartCache? restartCache = null) => _restartCache = restartCache;

    public bool TryTrack(byte[] payload, DateTimeOffset at, string flow, out LocalDinosaurVitalsObservation observation)
    {
        observation = default;
        if (_flow != flow) { Reset(); _flow = flow; }
        if (at < _lastAt) return false;
        _lastAt = at;
        var updates = _decoder.Observe(payload, at, flow);
        foreach (var owner in _owners.Where(p => at - p.Value.Values.Max(v => v.ObservedAt) > TimeSpan.FromSeconds(15)).Select(p => p.Key).ToArray())
            _owners.Remove(owner);
        foreach (var value in updates)
        {
            if (!_owners.TryGetValue(value.Owner, out var fields))
            {
                if (_owners.Count >= 512) continue;
                _owners[value.Owner] = fields = [];
            }
            fields[value.Field] = value;
        }
        if (_state.Owner is { } known && _decoder.LastPacketOwners.Contains(known)) _lastPresence = at;
        if (updates.Count == 0)
        {
            if (_state.Owner is not { } owner || _lastPresence != at || at - _lastPublished < TimeSpan.FromSeconds(1)) return false;
            var retained = _state.Apply(flow, owner, []);
            observation = new(at, FreshVitals(retained, at), owner) { ExperimentalEvidence = retained };
            _lastPublished = at;
            _restartCache?.Save(flow, owner, at, retained);
            return true;
        }
        if (_owners.Count != 1)
        {
            // Publish an explicit empty trial state so ambiguity never chooses
            // the first/nearest candidate or retains the previously selected one.
            observation = new(at, new ExactVitals(), 0) { ExperimentalEvidence = [] };
            _state.Clear();
            return true;
        }
        var entry = _owners.Single();
        if (!_restoreAttempted && _restartCache is not null)
        {
            _restoreAttempted = true;
            var restored = _restartCache.Restore(flow, entry.Key, at, entry.Value.Values.ToArray());
            _state.Apply(flow, entry.Key, restored);
        }
        var evidence = _state.Apply(flow, entry.Key, entry.Value.Values);
        _restartCache?.Save(flow, entry.Key, at, evidence);
        _lastPresence = at;
        _lastPublished = at;
        observation = new(at, FreshVitals(evidence, at), entry.Key) { ExperimentalEvidence = evidence };
        return true;
    }

    public static ExactVitals FreshVitals(IReadOnlyList<VitalsFieldEvidence> evidence, DateTimeOffset now)
        => ReadVitals(evidence, now, onlyFresh: true);

    public static ExactVitals LastKnownVitals(IReadOnlyList<VitalsFieldEvidence> evidence, DateTimeOffset now)
        => ReadVitals(evidence, now, onlyFresh: false);

    private static ExactVitals ReadVitals(IReadOnlyList<VitalsFieldEvidence> evidence, DateTimeOffset now, bool onlyFresh)
    {
        double? Read(string name) => evidence.FirstOrDefault(e => e.Field == name
            && e.ObservedAt <= now && (!onlyFresh || now - e.ObservedAt <= LocalPositionSnapshotMerger.LocalVitalsFreshness))?.Value;
        return new ExactVitals
        {
            Growth = Read("GrowthCandidate"), Health = Read("HealthCandidate"), MaxHealth = Read("MaxHealthCandidate"),
            Stamina = Read("StaminaCandidate"), MaxStamina = Read("MaxStaminaCandidate"),
            Hunger = Read("HungerCandidate"), MaxHunger = Read("MaxHungerCandidate"),
            Thirst = Read("ThirstCandidate"),
            // Protocol constant, not an observed update and never a synthetic timestamp.
            MaxThirst = Read("ThirstCandidate") is not null ? InboundVitalsDeltaState.ProtocolMaxWater : null
        };
    }
    public void Reset() { _decoder.Reset(); _owners.Clear(); _state.Clear(); _flow = null; _lastAt = default; _lastPresence = default; _lastPublished = default; _restoreAttempted = false; }
}
