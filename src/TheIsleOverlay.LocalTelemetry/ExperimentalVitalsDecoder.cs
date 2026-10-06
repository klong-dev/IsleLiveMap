namespace TheIsleOverlay.LocalTelemetry;

/// <summary>Opt-in lab decoder. Never used for production/local-owner admission.</summary>
public sealed class ExperimentalVitalsDecoder
{
    private readonly UnrealIrisPacketParser _parser = new();
    private readonly Dictionary<(ulong, string), (DateTimeOffset At, double Value, int Hits)> _candidates = [];
    private string? _flow;
    private DateTimeOffset _lastAt;
    private int? _lastSequence;
    public IReadOnlySet<ulong> LastPacketOwners { get; private set; } = new HashSet<ulong>();

    public IReadOnlyList<VitalsFieldEvidence> Observe(byte[] payload, DateTimeOffset at, string flow)
    {
        LastPacketOwners = new HashSet<ulong>();
        if (flow != _flow) { Reset(); _flow = flow; }
        if (at < _lastAt || !_parser.TryParse(payload, out var packet) || !packet.IsComplete) return [];
        if (_lastSequence is { } last)
        {
            var delta = (packet.PacketSequence - last + 16384) % 16384;
            if (delta == 0 || delta >= 8192) return [];
        }
        _lastAt = at;
        _lastSequence = packet.PacketSequence;
        LastPacketOwners = packet.Batches.Select(b => b.NetRefHandle).Where(h => h != 0).ToHashSet();
        foreach (var key in _candidates.Where(p => at - p.Value.At > TimeSpan.FromSeconds(15)).Select(p => p.Key).ToArray())
            _candidates.Remove(key);
        var result = new List<VitalsFieldEvidence>();
        foreach (var batch in packet.Batches)
        {
            if (!batch.HasOwnerData || batch.NetRefHandle == 0 || batch.DataBitOffset < 0
                || batch.DataBitCount < 198 || batch.DataBitOffset > payload.Length * 8 - batch.DataBitCount) continue;
            if (UnrealDinosaurVitalsTracker.TryDecodeReconnectAttributeFrame(payload, batch, out var full))
            {
                Add("GrowthCandidate", full.Growth!.Value, 822, "measured-1482");
                Add("HealthCandidate", full.Health!.Value, 888, "measured-1482");
                Add("MaxHealthCandidate", full.MaxHealth!.Value, 954, "measured-1482");
                Add("StaminaCandidate", full.Stamina!.Value, 1020, "measured-1482");
                Add("MaxStaminaCandidate", full.MaxStamina!.Value, 1086, "measured-1482");
                Add("MaxHungerCandidate", full.MaxHunger!.Value, 228, "measured-1482");
                continue;
            }
            // Existing measured legacy full-frame layouts, exact sizes only.
            // Independent field observations; no local owner claim or cached denominators.
            var shift = batch.DataBitCount switch { 1391 => -100, 1491 => 0, _ => int.MinValue };
            if (shift != int.MinValue
                && Pair(payload, batch, 831 + shift, out var growth) && growth <= 1
                && Pair(payload, batch, 897 + shift, out var health)
                && Pair(payload, batch, 963 + shift, out var maxHealth) && maxHealth > 0
                && Pair(payload, batch, 1029 + shift, out var currentStamina)
                && Pair(payload, batch, 1095 + shift, out var maxStamina) && maxStamina > 0
                && Pair(payload, batch, 237 + shift, out var maxHunger) && maxHunger > 0
                && health <= maxHealth * 1.01 && currentStamina <= maxStamina * 1.01)
            {
                Add("GrowthCandidate", growth, 831 + shift, "measured-1391-1491");
                Add("HealthCandidate", health, 897 + shift, "measured-1391-1491");
                Add("MaxHealthCandidate", maxHealth, 963 + shift, "measured-1391-1491");
                Add("StaminaCandidate", currentStamina, 1029 + shift, "measured-1391-1491");
                Add("MaxStaminaCandidate", maxStamina, 1095 + shift, "measured-1391-1491");
                Add("MaxHungerCandidate", maxHunger, 237 + shift, "measured-1391-1491");
                continue;
            }
            // Measured compact layouts only; exports may append unrelated bits.
            // Tail A/A/B was observed during HP decline, not proven GAS schema.
            if (batch.HasExports || batch.DataBitCount > 1200) continue;
            // Read the measured survival triplet A/B/A with optional trailing
            // slots. Only a unique alignment is usable; no website percentage
            // or default maximum is used to manufacture values.
            var survival = new List<(int Offset, int Slots, double Hunger, double Thirst)>();
            for (var slots = 0; slots <= 4; slots++)
            {
                var offset = batch.DataBitCount - (3 + slots) * 66;
                if (offset < 0 || !Pair(payload, batch, offset, out var food)
                    || !Pair(payload, batch, offset + 66, out var water)
                    || !Pair(payload, batch, offset + 132, out var repeatFood)
                    || food != repeatFood || food > 10000 || water > 2000
                    || Math.Max(food, water) < .05) continue;
                survival.Add((offset, slots, food, water));
            }
            if (survival.Count == 1)
            {
                var s = survival[0];
                var foodReady = Admit(batch.NetRefHandle, "HungerCandidate", s.Hunger, at);
                var waterReady = Admit(batch.NetRefHandle, "ThirstCandidate", s.Thirst, at);
                if (foodReady && waterReady)
                {
                    Add("HungerCandidate", s.Hunger, s.Offset, "survival-A-B-A");
                    Add("ThirstCandidate", s.Thirst, s.Offset + 66, "survival-A-B-A");
                    // Only the one-slot variant has controlled-run evidence for
                    // stamina. Two slots are NOT an implicit HP/stamina schema:
                    // raw 493-bit batches carry unrelated 0.293/0.043 fields.
                    if (s.Slots == 1 && Pair(payload, batch, batch.DataBitCount - 66, out var staminaUpdate))
                        Add("StaminaCandidate", staminaUpdate, batch.DataBitCount - 66, "survival-A-B-A-tail");
                }
                continue;
            }
            var tail = batch.DataBitCount - 198;
            if (Pair(payload, batch, tail, out var first) && first >= 100
                && Pair(payload, batch, tail + 66, out var repeat) && first == repeat
                && Pair(payload, batch, tail + 132, out var candidate)
                && Admit(batch.NetRefHandle, "HealthCandidate", candidate, at))
            {
                Add("HealthCandidate", candidate, tail + 132, "tail-A-A-B");
            }

            void Add(string field, double value, int relative, string layout) => result.Add(new(
                flow, batch.NetRefHandle, field, value, at, packet.PacketSequence,
                batch.DataBitOffset + relative, batch.DataBitCount, layout));
        }
        return result;
    }

    private bool Admit(ulong owner, string field, double value, DateTimeOffset at)
    {
        var key = (owner, field);
        var hits = 1;
        if (_candidates.TryGetValue(key, out var previous) && at > previous.At
            && at - previous.At <= TimeSpan.FromSeconds(15)
            && Math.Abs(value - previous.Value) <= Math.Max(100, previous.Value * 0.5)) hits = previous.Hits + 1;
        if (_candidates.Count >= 4096 && !_candidates.ContainsKey(key)) return false;
        _candidates[key] = (at, value, hits);
        return hits >= 2;
    }

    internal static bool Pair(ReadOnlySpan<byte> payload, UnrealIrisReplicationBatch batch, int relative, out double value)
    {
        value = 0;
        var absolute = batch.DataBitOffset + relative;
        if (relative < 0 || relative > batch.DataBitCount - 66 || absolute < 0
            || absolute > payload.Length * 8 - 66 || (payload[(absolute + 32) / 8] & (1 << ((absolute + 32) % 8))) == 0) return false;
        var first = Bits(payload, absolute);
        var second = Bits(payload, absolute + 33);
        // Exact duplicate prevents denormal/negative overlap from passing epsilon checks.
        if (first != second) return false;
        var number = BitConverter.Int32BitsToSingle(unchecked((int)first));
        if (!float.IsFinite(number) || number < 0 || number > 100000 || (number > 0 && number < 1e-6)) return false;
        value = number;
        return true;
    }

    private static uint Bits(ReadOnlySpan<byte> payload, int offset)
    {
        uint bits = 0;
        for (var bit = 0; bit < 32; bit++) bits |= (uint)((payload[(offset + bit) / 8] >> ((offset + bit) % 8)) & 1) << bit;
        return bits;
    }

    public void Reset() { _candidates.Clear(); _flow = null; _lastAt = default; _lastSequence = null; LastPacketOwners = new HashSet<ulong>(); }
}

public sealed record VitalsFieldEvidence(string Flow, ulong Owner, string Field, double Value,
    DateTimeOffset ObservedAt, int Sequence, int AbsoluteBitOffset, int BatchBits, string Layout);
