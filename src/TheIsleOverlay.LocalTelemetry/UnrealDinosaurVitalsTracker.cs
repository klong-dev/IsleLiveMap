using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Tracks the owning dinosaur's Gameplay Ability System attributes from
/// server-to-client Iris replication. Values are observed passively from the
/// player's own game connection; no website telemetry is involved.
/// </summary>
public sealed class UnrealDinosaurVitalsTracker
{
    private static readonly TimeSpan CandidateLifetime = TimeSpan.FromSeconds(2.5);
    // Busy servers and airborne movement can leave short holes in inbound Iris
    // replication. Five seconds caused a valid owner to be discarded, forcing
    // the UI to wait for bootstrap and the next periodic full GAS frame again.
    private static readonly TimeSpan SelfHeartbeatTimeout = TimeSpan.FromSeconds(12);
    private const int RequiredHeartbeatHits = 2;
    private const int AttributePairBits = 66;
    private const int MaximumHeartbeatBatchBits = 1_200;
    private const int MaximumTrailingHeartbeatAttributeBits = AttributePairBits * 4;
    private const double DefaultMaximumThirst = 1_000d;
    private const double MinimumPlausibleMaximum = 0.01d;
    private const double MinimumUnverifiedCurrentHealth = 0.1d;

    private readonly UnrealIrisPacketParser _parser;
    private readonly Dictionary<ulong, HeartbeatCandidate> _candidates = [];
    // Full GAS frames are sparse and may not be repeated for several minutes.
    // Keep only their verified denominators per actor handle so a short Iris
    // heartbeat gap cannot make every percentage bar lose its maximum.
    private readonly Dictionary<ulong, VerifiedMaximums> _verifiedMaximums = [];
    private ulong? _selfHandle;
    private DateTimeOffset _lastSelfHeartbeatAt;
    private ExactVitals _vitals = new() { MaxThirst = DefaultMaximumThirst };

    public UnrealDinosaurVitalsTracker(UnrealIrisPacketParser? parser = null)
    {
        _parser = parser ?? new UnrealIrisPacketParser();
    }

    public bool TryTrack(
        ReadOnlySpan<byte> payload,
        DateTimeOffset observedAt,
        out LocalDinosaurVitalsObservation observation)
    {
        observation = default;
        if (!_parser.TryParse(payload, out var packet))
        {
            return false;
        }

        if (_selfHandle is not null
            && (observedAt < _lastSelfHeartbeatAt
                || observedAt - _lastSelfHeartbeatAt > SelfHeartbeatTimeout))
        {
            ResetSelf();
        }

        var changed = false;
        if (_selfHandle is null)
        {
            // Inspect the whole packet before choosing a handle. Selecting as
            // soon as the first candidate reaches two hits can lock onto a
            // preceding overlap batch while the real owner batch appears later
            // in the same packet.
            var packetHeartbeats = new Dictionary<ulong, Heartbeat>();
            foreach (var batch in packet.Batches)
            {
                if (!TryFindHeartbeat(payload, batch, null, out var heartbeat))
                {
                    continue;
                }

                ObserveHeartbeatCandidate(batch.NetRefHandle, heartbeat, observedAt);
                packetHeartbeats[batch.NetRefHandle] = heartbeat;
            }

            _selfHandle = SelectReadyCandidate(observedAt);
            if (_selfHandle is { } selectedHandle
                && packetHeartbeats.TryGetValue(selectedHandle, out var selectedHeartbeat))
            {
                RestoreVerifiedMaximums(selectedHandle);
                _lastSelfHeartbeatAt = observedAt;
                changed |= SetHeartbeat(selectedHeartbeat);
                foreach (var batch in packet.Batches)
                {
                    if (batch.NetRefHandle == selectedHandle)
                    {
                        changed |= TryReadPeriodicAttributeFrame(payload, batch);
                    }
                }
            }
        }
        else
        {
            var knownHandle = _selfHandle.Value;
            // Once bootstrapped, scan only the authoritative owner handle so
            // vitals parsing cannot add latency to the movement hot path.
            foreach (var batch in packet.Batches)
            {
                if (batch.NetRefHandle != knownHandle)
                {
                    continue;
                }

                if (TryFindHeartbeat(payload, batch, _vitals, out var heartbeat))
                {
                    _lastSelfHeartbeatAt = observedAt;
                    changed |= SetHeartbeat(heartbeat);
                }

                changed |= TryReadPeriodicAttributeFrame(payload, batch);
            }
        }

        if (!packet.IsComplete
            && _selfHandle is not null)
        {
            changed |= TryReadDetachedPeriodicAttributeFrame(payload);
        }

        PruneCandidates(observedAt);
        if (!changed || _selfHandle is not { } selfHandle)
        {
            return false;
        }

        observation = new LocalDinosaurVitalsObservation(
            observedAt,
            _vitals,
            selfHandle);
        return true;
    }

    public void Reset()
    {
        _candidates.Clear();
        _verifiedMaximums.Clear();
        ResetSelf();
    }

    internal void SeedVerifiedVitals(LocalDinosaurVitalsObservation observation)
    {
        var source = observation.Vitals;
        if (source.MaxHealth is not > 0d
            || source.MaxStamina is not > 0d
            || source.MaxHunger is not > 0d
            || source.Health is not > 0d
            || source.Stamina is not >= 0d)
        {
            return;
        }

        _verifiedMaximums[observation.NetRefHandle] = new VerifiedMaximums(
            source.Growth,
            source.Health,
            source.MaxHealth.Value,
            source.Stamina,
            source.MaxStamina.Value,
            source.MaxHunger.Value);
        if (_selfHandle != observation.NetRefHandle)
        {
            return;
        }

        _vitals = _vitals with
        {
            Growth = source.Growth ?? _vitals.Growth,
            Health = source.Health,
            MaxHealth = source.MaxHealth,
            Stamina = source.Stamina,
            MaxStamina = source.MaxStamina,
            MaxHunger = source.MaxHunger,
            MaxThirst = DefaultMaximumThirst
        };
    }

    private void ResetSelf()
    {
        _selfHandle = null;
        _lastSelfHeartbeatAt = default;
        _candidates.Clear();
        _vitals = new ExactVitals { MaxThirst = DefaultMaximumThirst };
    }

    private void ObserveHeartbeatCandidate(
        ulong handle,
        Heartbeat heartbeat,
        DateTimeOffset observedAt)
    {
        if (!_candidates.TryGetValue(handle, out var candidate)
            || observedAt < candidate.LastSeen
            || observedAt - candidate.LastSeen > CandidateLifetime
            || !IsContinuous(candidate.Heartbeat.Hunger, heartbeat.Hunger, 100d)
            || !IsContinuous(candidate.Heartbeat.Thirst, heartbeat.Thirst, 250d))
        {
            _candidates[handle] = new HeartbeatCandidate(
                heartbeat,
                observedAt,
                observedAt,
                1);
            return;
        }

        if (observedAt == candidate.LastSeen)
        {
            _candidates[handle] = candidate with { Heartbeat = heartbeat };
            return;
        }

        _candidates[handle] = candidate with
        {
            Heartbeat = heartbeat,
            LastSeen = observedAt,
            ConsecutiveHits = candidate.ConsecutiveHits + 1
        };
    }

    private ulong? SelectReadyCandidate(DateTimeOffset observedAt) =>
        _candidates
            .Where(pair =>
                pair.Value.ConsecutiveHits >= RequiredHeartbeatHits
                && observedAt - pair.Value.LastSeen <= CandidateLifetime)
            .OrderByDescending(pair => pair.Value.ConsecutiveHits)
            .ThenByDescending(pair => Math.Min(pair.Value.Heartbeat.Hunger, 100d))
            .ThenBy(pair => pair.Value.FirstSeen)
            .Select(pair => (ulong?)pair.Key)
            .FirstOrDefault();

    private void PruneCandidates(DateTimeOffset observedAt)
    {
        foreach (var handle in _candidates
                     .Where(pair =>
                         observedAt < pair.Value.LastSeen
                         || observedAt - pair.Value.LastSeen > CandidateLifetime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _candidates.Remove(handle);
        }
    }

    private bool SetHeartbeat(Heartbeat heartbeat)
    {
        if (NearlyEqual(_vitals.Hunger, heartbeat.Hunger)
            && NearlyEqual(_vitals.Thirst, heartbeat.Thirst)
            && (heartbeat.Health is null
                || NearlyEqual(_vitals.Health, heartbeat.Health.Value))
            && (heartbeat.Stamina is null
                || NearlyEqual(_vitals.Stamina, heartbeat.Stamina.Value)))
        {
            return false;
        }

        _vitals = _vitals with
        {
            Hunger = heartbeat.Hunger,
            Thirst = heartbeat.Thirst,
            Health = heartbeat.Health ?? _vitals.Health,
            Stamina = heartbeat.Stamina ?? _vitals.Stamina
        };
        RememberVerifiedMaximums();
        return true;
    }

    private bool TryReadPeriodicAttributeFrame(
        ReadOnlySpan<byte> payload,
        UnrealIrisReplicationBatch batch)
    {
        if (TryReadSparseMaximumAttributeFrame(payload, batch))
        {
            return true;
        }

        // The owning dinosaur alternates between two equivalent change-mask
        // shapes. The shorter form omits a 100-bit prefix; GAS attribute order
        // after that prefix is identical.
        var shift = batch.DataBitCount switch
        {
            >= 1_480 and <= 1_510 => 0,
            >= 1_380 and <= 1_410 => -100,
            _ => int.MinValue
        };
        if (shift == int.MinValue
            || !TryReadAttribute(payload, batch, 831 + shift, out var growth)
            || !TryReadAttribute(payload, batch, 897 + shift, out var health)
            || !TryReadAttribute(payload, batch, 963 + shift, out var maxHealth)
            || !TryReadAttribute(payload, batch, 1_029 + shift, out var stamina)
            || !TryReadAttribute(payload, batch, 1_095 + shift, out var maxStamina)
            || !TryReadAttribute(payload, batch, 237 + shift, out var maxHunger)
            || maxHealth < MinimumPlausibleMaximum
            || maxStamina < MinimumPlausibleMaximum
            || maxHunger < MinimumPlausibleMaximum
            || growth > 1.001d
            || health > maxHealth * 1.01d
            || stamina > maxStamina * 1.01d
            || _vitals.Hunger is { } currentHunger
            && currentHunger > maxHunger * 1.1d)
        {
            return false;
        }

        if (NearlyEqual(_vitals.Growth, growth)
            && NearlyEqual(_vitals.Health, health)
            && NearlyEqual(_vitals.MaxHealth, maxHealth)
            && NearlyEqual(_vitals.Stamina, stamina)
            && NearlyEqual(_vitals.MaxStamina, maxStamina)
            && NearlyEqual(_vitals.MaxHunger, maxHunger))
        {
            return false;
        }

        _vitals = _vitals with
        {
            Growth = growth,
            Health = health,
            MaxHealth = maxHealth,
            Stamina = stamina,
            MaxStamina = maxStamina,
            MaxHunger = maxHunger,
            MaxThirst = DefaultMaximumThirst
        };
        RememberVerifiedMaximums();
        return true;
    }

    private bool TryReadSparseMaximumAttributeFrame(
        ReadOnlySpan<byte> payload,
        UnrealIrisReplicationBatch batch)
    {
        // Some species use a sparse change mask: MaxStamina is serialized in
        // the leading maximum block and omitted beside current Stamina. That
        // makes the frame 1417/1517 bits instead of the older 140x/150x form.
        // The three repeated MaxHealth values at the tail are a strong guard
        // against interpreting unrelated replicated floats as GAS vitals.
        var prefix = batch.DataBitCount switch
        {
            >= 1_400 and <= 1_435 => 0,
            >= 1_500 and <= 1_535 => 100,
            _ => int.MinValue
        };
        if (prefix == int.MinValue
            || !TryReadAttribute(payload, batch, 229 + prefix, out var maxHunger)
            || !TryReadAttribute(payload, batch, 361 + prefix, out var maxStamina)
            || !TryReadAttribute(payload, batch, 823 + prefix, out var growth)
            || !TryReadAttribute(payload, batch, 889 + prefix, out var health)
            || !TryReadAttribute(payload, batch, 955 + prefix, out var maxHealth)
            || !TryReadAttribute(payload, batch, 1_021 + prefix, out var stamina)
            || !TryReadAttribute(payload, batch, 1_285 + prefix, out var repeatedMaxHealth)
            || !TryReadAttribute(payload, batch, 1_318 + prefix, out var repeatedMaxHealth2)
            || !TryReadAttribute(payload, batch, 1_351 + prefix, out var repeatedMaxHealth3)
            || !NearlyEqual(maxHealth, repeatedMaxHealth)
            || !NearlyEqual(maxHealth, repeatedMaxHealth2)
            || !NearlyEqual(maxHealth, repeatedMaxHealth3)
            || maxHealth < MinimumPlausibleMaximum
            || maxStamina < MinimumPlausibleMaximum
            || maxHunger < MinimumPlausibleMaximum
            || growth > 1.001d
            || health > maxHealth * 1.01d
            || stamina > maxStamina * 1.01d
            || _vitals.Hunger is { } currentHunger
            && currentHunger > maxHunger * 1.1d)
        {
            return false;
        }

        if (NearlyEqual(_vitals.Growth, growth)
            && NearlyEqual(_vitals.Health, health)
            && NearlyEqual(_vitals.MaxHealth, maxHealth)
            && NearlyEqual(_vitals.Stamina, stamina)
            && NearlyEqual(_vitals.MaxStamina, maxStamina)
            && NearlyEqual(_vitals.MaxHunger, maxHunger))
        {
            return false;
        }

        _vitals = _vitals with
        {
            Growth = growth,
            Health = health,
            MaxHealth = maxHealth,
            Stamina = stamina,
            MaxStamina = maxStamina,
            MaxHunger = maxHunger,
            MaxThirst = DefaultMaximumThirst
        };
        RememberVerifiedMaximums();
        return true;
    }

    private static bool TryFindHeartbeat(
        ReadOnlySpan<byte> payload,
        UnrealIrisReplicationBatch batch,
        ExactVitals? expected,
        out Heartbeat heartbeat)
    {
        heartbeat = default;
        if (!batch.HasOwnerData
            || batch.DataBitCount < AttributePairBits * 3
            || batch.DataBitCount > MaximumHeartbeatBatchBits)
        {
            return false;
        }

        var matches = new List<Heartbeat>();
        var maximumStart = batch.DataBitCount - AttributePairBits * 3;
        for (var relative = 0; relative <= maximumStart; relative++)
        {
            var trailingBits = batch.DataBitCount
                               - (relative + AttributePairBits * 3);
            // Different dinosaurs enable a different number of optional GAS
            // attributes after Hunger/Thirst/Hunger. The survival triplet is
            // still aligned to complete 66-bit FGameplayAttributeData slots.
            if (trailingBits < 0
                || trailingBits > MaximumTrailingHeartbeatAttributeBits
                || trailingBits % AttributePairBits != 0)
            {
                continue;
            }

            if (!TryReadAttribute(payload, batch, relative, out var hunger)
                || !TryReadAttribute(
                    payload,
                    batch,
                    relative + AttributePairBits,
                    out var thirst)
                || !TryReadAttribute(
                    payload,
                    batch,
                    relative + AttributePairBits * 2,
                    out var duplicateHunger)
                || Math.Abs(hunger - duplicateHunger) > Math.Max(0.0001d, hunger * 0.00001d)
                || hunger > 10_000d
                || thirst > 2_000d
                // Bit overlap frequently produces denormal triples. At least
                // one survival meter must carry a meaningful game value.
                || expected is null && Math.Max(hunger, thirst) < 0.05d
                || expected?.MaxThirst is > 0d
                && thirst > expected.MaxThirst.Value * 1.05d)
            {
                continue;
            }

            if (expected?.MaxHunger is > 0d
                && hunger > expected.MaxHunger.Value * 1.05d)
            {
                continue;
            }

            double? health = null;
            double? stamina = null;
            if (trailingBits >= AttributePairBits
                && TryReadAttribute(
                    payload,
                    batch,
                    relative + AttributePairBits * 3 + trailingBits - AttributePairBits,
                    out var currentStamina)
                && (IsPlausibleOptionalStamina(expected, currentStamina)
                    || CanBootstrapOptionalStamina(expected, currentStamina)))
            {
                stamina = currentStamina;
            }

            if (stamina is not null
                && trailingBits >= AttributePairBits * 2
                && TryReadAttribute(
                    payload,
                    batch,
                    relative + AttributePairBits * 3 + trailingBits - AttributePairBits * 2,
                    out var currentHealth)
                && (IsPlausibleOptionalHealth(
                        expected,
                        currentHealth)
                    || CanBootstrapOptionalVitals(
                        expected,
                        currentHealth,
                        stamina.Value)))
            {
                health = currentHealth;
            }

            matches.Add(new Heartbeat(hunger, thirst, health, stamina));
        }

        if (matches.Count == 0)
        {
            return false;
        }

        if (expected is { Hunger: { } expectedHunger, Thirst: { } expectedThirst })
        {
            var selected = matches.MinBy(candidate =>
                Math.Abs(candidate.Hunger - expectedHunger)
                / Math.Max(1d, expected.MaxHunger ?? Math.Abs(expectedHunger))
                + Math.Abs(candidate.Thirst - expectedThirst)
                / Math.Max(100d, expected.MaxThirst ?? Math.Abs(expectedThirst)));
            // Eating or drinking can legitimately refill one meter quickly;
            // both survival meters collapsing to denormals in the same packet
            // is a known bit-overlap signature, not gameplay.
            if (expectedHunger > 0.05d
                && expectedThirst > 0.05d
                && selected.Hunger < expectedHunger * 0.1d
                && selected.Thirst < expectedThirst * 0.1d)
            {
                return false;
            }

            heartbeat = selected;
        }
        else
        {
            heartbeat = matches.MaxBy(candidate => candidate.Thirst + candidate.Hunger * 0.1d);
        }

        return true;
    }

    private bool TryReadDetachedPeriodicAttributeFrame(ReadOnlySpan<byte> payload)
    {
        if (!TryDecodeDetachedPeriodicAttributeFrame(
                payload,
                _vitals.Hunger,
                out var decoded))
        {
            return false;
        }

        _vitals = _vitals with
        {
            Growth = decoded.Growth,
            Health = decoded.Health,
            MaxHealth = decoded.MaxHealth,
            Stamina = decoded.Stamina,
            MaxStamina = decoded.MaxStamina,
            MaxHunger = decoded.MaxHunger,
            MaxThirst = DefaultMaximumThirst
        };
        RememberVerifiedMaximums();
        return true;
    }

    private void RememberVerifiedMaximums()
    {
        if (_selfHandle is not { } handle
            || _vitals.MaxHealth is not > 0d
            || _vitals.MaxStamina is not > 0d
            || _vitals.MaxHunger is not > 0d)
        {
            return;
        }

        _verifiedMaximums[handle] = new VerifiedMaximums(
            _vitals.Growth,
            _vitals.Health,
            _vitals.MaxHealth.Value,
            _vitals.Stamina,
            _vitals.MaxStamina.Value,
            _vitals.MaxHunger.Value);
    }

    private void RestoreVerifiedMaximums(ulong handle)
    {
        if (!_verifiedMaximums.TryGetValue(handle, out var verified))
        {
            return;
        }

        _vitals = _vitals with
        {
            Growth = verified.Growth,
            Health = verified.Health,
            MaxHealth = verified.MaxHealth,
            Stamina = verified.Stamina,
            MaxStamina = verified.MaxStamina,
            MaxHunger = verified.MaxHunger,
            MaxThirst = DefaultMaximumThirst
        };
    }

    private static bool IsPlausibleOptionalHealth(
        ExactVitals? expected,
        double health)
    {
        if (expected?.MaxHealth is not > 0d
            || health <= 0d
            || health > expected.MaxHealth.Value * 1.01d)
        {
            return false;
        }

        // Optional GAS slots differ by dinosaur. Only treat them as
        // Health/Stamina when they remain continuous with a verified full
        // frame. This rejects Pteranodon's unrelated 0/x trailing pair while
        // retaining the real per-second pair used by Herrera and similar
        // layouts.
        return expected.Health is null
               || Math.Abs(health - expected.Health.Value)
               <= expected.MaxHealth.Value * 0.5d;
    }

    private static bool IsPlausibleOptionalStamina(
        ExactVitals? expected,
        double stamina) =>
        expected?.MaxStamina is > 0d
        && stamina <= expected.MaxStamina.Value * 1.01d
        && (expected.Stamina is null
            || Math.Abs(stamina - expected.Stamina.Value)
            <= expected.MaxStamina.Value * 0.5d);

    private static bool CanBootstrapOptionalStamina(
        ExactVitals? expected,
        double stamina) =>
        expected?.MaxStamina is not > 0d
        && stamina >= MinimumPlausibleMaximum
        && stamina <= 100_000d;

    private static bool CanBootstrapOptionalVitals(
        ExactVitals? expected,
        double health,
        double stamina) =>
        expected?.MaxHealth is not > 0d
        && expected?.MaxStamina is not > 0d
        // Live GAS layouts with current Health/Stamina place two complete
        // 66-bit attributes directly after Hunger/Thirst/Hunger. Requiring a
        // meaningful positive Health rejects Pteranodon's unrelated 0/83.2
        // optional pair while allowing the reducer to show current values
        // before the sparse maximum frame arrives.
        && health >= MinimumUnverifiedCurrentHealth
        && health <= 100_000d
        && stamina >= 0d
        && stamina <= 100_000d;

    internal static bool TryDecodeDetachedPeriodicAttributeFrame(
        ReadOnlySpan<byte> payload,
        double? currentHunger,
        out ExactVitals decoded)
    {
        decoded = new ExactVitals();
        var payloadBitCount = payload.Length * 8;
        foreach (var shift in new[] { 0, -100 })
        {
            var lastRequiredOffset = 1_425 + shift + AttributePairBits;
            for (var frameStart = 0;
                 frameStart + lastRequiredOffset <= payloadBitCount;
                 frameStart++)
            {
                if (!TryReadAttributeAt(payload, frameStart + 831 + shift, out var growth)
                    || !TryReadAttributeAt(payload, frameStart + 897 + shift, out var health)
                    || !TryReadAttributeAt(payload, frameStart + 963 + shift, out var maxHealth)
                    || !TryReadAttributeAt(payload, frameStart + 1_029 + shift, out var stamina)
                    || !TryReadAttributeAt(payload, frameStart + 1_095 + shift, out var maxStamina)
                    || !TryReadAttributeAt(payload, frameStart + 237 + shift, out var maxHunger)
                    || !TryReadAttributeAt(payload, frameStart + 1_359 + shift, out var repeatedMaxHealth)
                    || !TryReadAttributeAt(payload, frameStart + 1_392 + shift, out var repeatedMaxHealth2)
                    || !TryReadAttributeAt(payload, frameStart + 1_425 + shift, out var repeatedMaxHealth3)
                    || !NearlyEqual(maxHealth, repeatedMaxHealth)
                    || !NearlyEqual(maxHealth, repeatedMaxHealth2)
                    || !NearlyEqual(maxHealth, repeatedMaxHealth3)
                    || maxHealth < MinimumPlausibleMaximum
                    || maxStamina < MinimumPlausibleMaximum
                    || maxHunger < MinimumPlausibleMaximum
                    || growth > 1.001d
                    || health > maxHealth * 1.01d
                    || stamina > maxStamina * 1.01d
                    || currentHunger is { } hunger
                    && hunger > maxHunger * 1.1d)
                {
                    continue;
                }

                decoded = new ExactVitals
                {
                    Growth = growth,
                    Health = health,
                    MaxHealth = maxHealth,
                    Stamina = stamina,
                    MaxStamina = maxStamina,
                    MaxHunger = maxHunger,
                    MaxThirst = DefaultMaximumThirst
                };
                return true;
            }
        }

        return false;
    }

    private static bool TryReadAttributeAt(
        ReadOnlySpan<byte> payload,
        int absoluteBitOffset,
        out double value)
    {
        value = 0d;
        if (absoluteBitOffset < 0
            || absoluteBitOffset + AttributePairBits > payload.Length * 8
            || !IsBitSet(payload, absoluteBitOffset + 32))
        {
            return false;
        }

        var first = ReadFloat(payload, absoluteBitOffset);
        var second = ReadFloat(payload, absoluteBitOffset + 33);
        if (!float.IsFinite(first)
            || !float.IsFinite(second)
            || first < 0f
            || first > 100_000f
            || Math.Abs(first - second) > Math.Max(0.0001f, Math.Abs(first) * 0.000001f))
        {
            return false;
        }

        value = first;
        return true;
    }

    private static bool TryReadAttribute(
        ReadOnlySpan<byte> payload,
        UnrealIrisReplicationBatch batch,
        int relativeBitOffset,
        out double value)
    {
        value = 0d;
        if (relativeBitOffset < 0
            || relativeBitOffset + AttributePairBits > batch.DataBitCount)
        {
            return false;
        }

        var absolute = batch.DataBitOffset + relativeBitOffset;
        if (!IsBitSet(payload, absolute + 32))
        {
            return false;
        }

        var first = ReadFloat(payload, absolute);
        var second = ReadFloat(payload, absolute + 33);
        if (!float.IsFinite(first)
            || !float.IsFinite(second)
            || first < 0f
            || first > 100_000f
            || Math.Abs(first - second) > Math.Max(0.0001f, Math.Abs(first) * 0.000001f))
        {
            return false;
        }

        value = first;
        return true;
    }

    private static float ReadFloat(ReadOnlySpan<byte> payload, int bitOffset) =>
        BitConverter.Int32BitsToSingle((int)(uint)ReadBits(payload, bitOffset, 32));

    private static ulong ReadBits(
        ReadOnlySpan<byte> payload,
        int bitOffset,
        int bitCount)
    {
        ulong value = 0;
        for (var bit = 0; bit < bitCount; bit++)
        {
            var sourceBit = bitOffset + bit;
            if ((payload[sourceBit >> 3] & 1 << (sourceBit & 7)) != 0)
            {
                value |= 1UL << bit;
            }
        }

        return value;
    }

    private static bool IsBitSet(ReadOnlySpan<byte> payload, int bitOffset) =>
        (payload[bitOffset >> 3] & 1 << (bitOffset & 7)) != 0;

    private static bool IsContinuous(double previous, double current, double absoluteAllowance) =>
        Math.Abs(previous - current)
        <= Math.Max(absoluteAllowance, Math.Abs(previous) * 0.75d);

    private static bool NearlyEqual(double? left, double right) =>
        left is { } value
        && Math.Abs(value - right) <= Math.Max(0.000001d, Math.Abs(right) * 0.000001d);

    private static bool NearlyEqual(double left, double right) =>
        Math.Abs(left - right) <= Math.Max(0.0001d, Math.Abs(right) * 0.000001d);

    private readonly record struct Heartbeat(
        double Hunger,
        double Thirst,
        double? Health,
        double? Stamina);

    private readonly record struct VerifiedMaximums(
        double? Growth,
        double? Health,
        double MaxHealth,
        double? Stamina,
        double MaxStamina,
        double MaxHunger);

    private sealed record HeartbeatCandidate(
        Heartbeat Heartbeat,
        DateTimeOffset FirstSeen,
        DateTimeOffset LastSeen,
        int ConsecutiveHits);
}
