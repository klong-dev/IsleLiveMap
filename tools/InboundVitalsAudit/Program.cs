using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.LocalTelemetry;

if (args.Length is < 1 or > 4)
{
    Console.Error.WriteLine("Usage: InboundVitalsAudit <ISLEIN01-inbound.bin> [report.json] [reference-growth-percent-or-dash] [focus-owner]");
    return 2;
}

var capturePath = Path.GetFullPath(args[0]);
var targetGrowthFraction = args.Length >= 3
    && double.TryParse(args[2], System.Globalization.CultureInfo.InvariantCulture, out var targetGrowthPercent)
    ? targetGrowthPercent / 100d
    : (double?)null;
var focusOwner = args.Length == 4 ? ulong.Parse(args[3]) : (ulong?)null;
var packets = ReadCapture(capturePath, out var truncatedTail);
var groups = new Dictionary<string, EndpointAudit>(StringComparer.OrdinalIgnoreCase);
foreach (var packet in packets.OrderBy(packet => packet.ObservedAt).ThenBy(packet => packet.Offset))
{
    var endpoint = $"{packet.SourceAddress}:{packet.SourcePort}";
    if (!groups.TryGetValue(endpoint, out var audit))
        groups[endpoint] = audit = new EndpointAudit();

    audit.Packets++;
    if (audit.InboundTrial.TryTrack(packet.Payload, packet.ObservedAt, endpoint, out var trial))
        audit.InboundTrialObservations.Add(new { RawOffset = packet.Offset, Observation = trial });
    audit.FirstAt ??= packet.ObservedAt;
    audit.LastAt = packet.ObservedAt;
    if (audit.Parser.TryParse(packet.Payload, out var parsed))
    {
        audit.IrisPackets++;
        if (parsed.IsComplete) audit.CompleteIrisPackets++;
        audit.ObjectBatches += parsed.Batches.Count;
        foreach (var batch in parsed.Batches)
        {
            if (!batch.HasOwnerData) continue;
            if (batch.NetRefHandle == focusOwner)
            {
                var pairs = new List<object>();
                var repeatedPairTriplets = new List<object>();
                for (var relative = 0; relative + 66 <= batch.DataBitCount; relative++)
                {
                    if (batch.DataBitOffset + relative + 66 <= packet.Payload.Length * 8
                        && TryReadAttribute(packet.Payload, batch, relative, out var value)
                        && (value >= 0.000001d || (batch.DataBitCount - relative) % 66 == 0))
                        pairs.Add(new { RelativeBitOffset = relative,
                            AbsoluteBitOffset = batch.DataBitOffset + relative, Value = value });
                    if (relative + 198 <= batch.DataBitCount
                        && batch.DataBitOffset + relative + 198 <= packet.Payload.Length * 8
                        && TryReadAttribute(packet.Payload, batch, relative, out var first)
                        && first >= 100d
                        && TryReadAttribute(packet.Payload, batch, relative + 66, out var repeated)
                        && first == repeated)
                    {
                        var nextAbsolute = batch.DataBitOffset + relative + 132;
                        var pairValid = TryReadAttribute(packet.Payload, batch, relative + 132, out var third);
                        repeatedPairTriplets.Add(new
                        {
                            FirstRelativeBitOffset = relative, RepeatedValue = first,
                            ThirdRelativeBitOffset = relative + 132,
                            ThirdAbsoluteBitOffset = nextAbsolute,
                            ThirdPairValid = pairValid,
                            ThirdValue = pairValid ? third : (double?)null,
                            ThirdFirstRawBits = ReadBits(packet.Payload, nextAbsolute, 32),
                            ThirdSeparator = IsBitSet(packet.Payload, nextAbsolute + 32),
                            ThirdSecondRawBits = ReadBits(packet.Payload, nextAbsolute + 33, 32)
                        });
                    }
                }
                audit.FocusBatches.Add(new
                {
                    packet.ObservedAt, RawPacketOffset = packet.Offset,
                    Sequence = parsed.PacketSequence, parsed.IsComplete,
                    batch.NetRefHandle, batch.DataBitOffset, batch.DataBitCount,
                    batch.HasExports, batch.HasOwnerData, AttributePairHypotheses = pairs,
                    RepeatedPairTripletHypotheses = repeatedPairTriplets,
                    PayloadBase64 = Convert.ToBase64String(packet.Payload)
                });
            }
            audit.OwnerDataBatches++;
            if (batch.DataBitCount is >= 198 and <= 1200)
                audit.HeartbeatLayoutSizedBatches++;
            if (batch.DataBitCount is >= 1380 and <= 1535)
                audit.GrowthLayoutSizedBatches++;
            if (!audit.HandleBatches.TryGetValue(batch.NetRefHandle, out var handle))
                audit.HandleBatches[batch.NetRefHandle] = handle = new HandleBatchAudit();
            handle.Total++;
            if (batch.DataBitCount is >= 1380 and <= 1535) handle.GrowthLayoutSized++;
            if (TryReadFullGasFrame(packet.Payload, batch, packet.ObservedAt, out var fullFrame))
                audit.FullFrames.Add(fullFrame);
            if (batch.DataBitCount == 1482)
                audit.DebugFrames.Add(ReadDebugFrame(packet.Payload, batch, packet.ObservedAt));
            ObserveAttributePairs(audit, packet.Payload, batch, packet.ObservedAt, targetGrowthFraction);
            ObserveSingleFloats(audit, packet.Payload, batch, packet.ObservedAt, targetGrowthFraction);
        }
    }

    if (!audit.Tracker.TryTrack(packet.Payload, packet.ObservedAt, out var observation))
        continue;

    audit.VitalsObservations++;
    audit.OwnerHandles.Add(observation.NetRefHandle);
    var vitals = observation.Vitals;
    // These are tracker snapshots, potentially carrying old fields forward.
    // Packet provenance identifies the triggering packet, not proof that every
    // field was serialized in that packet or belongs to the local player.
    audit.VitalsSnapshots.Add(new
    {
        At = observation.ObservedAt,
        RawPacketOffset = packet.Offset,
        OwnerHandle = observation.NetRefHandle,
        GrowthFraction = vitals.Growth,
        vitals.Health, vitals.MaxHealth,
        vitals.Stamina, vitals.MaxStamina,
        StaminaPercent = vitals.Stamina is { } stamina && vitals.MaxStamina is > 0d
            ? stamina / vitals.MaxStamina.Value * 100d : (double?)null,
        vitals.Hunger, vitals.MaxHunger, vitals.Thirst, vitals.MaxThirst
    });
    if (vitals.Growth is not null) audit.GrowthObservations++;
    if (vitals.Growth is { } observedGrowth)
    {
        if (audit.LastGrowthFraction is { } previousGrowth)
        {
            if (observedGrowth > previousGrowth + 0.000001d) audit.GrowthIncreasingSteps++;
            if (observedGrowth < previousGrowth - 0.000001d) audit.GrowthDecreasingSteps++;
        }
        if (audit.LastGrowthFraction is not { } previous
            || Math.Abs(observedGrowth - previous) > 0.000001d)
            audit.GrowthValueChanges++;
        audit.FirstGrowthFraction ??= observedGrowth;
        audit.LastGrowthFraction = observedGrowth;
        audit.MinimumGrowthFraction = Math.Min(audit.MinimumGrowthFraction ?? observedGrowth, observedGrowth);
        audit.MaximumGrowthFraction = Math.Max(audit.MaximumGrowthFraction ?? observedGrowth, observedGrowth);
        if (audit.GrowthSamples.Count < 500)
            audit.GrowthSamples.Add(new GrowthSample(observation.ObservedAt, observation.NetRefHandle, observedGrowth));
    }
    if (vitals.MaxHealth is not null) audit.VerifiedMaximumObservations++;
    audit.LastVitals = new
    {
        At = observation.ObservedAt,
        OwnerHandle = observation.NetRefHandle,
        Species = (string?)null,
        SpeciesEvidence = "No proven owner GAS/Pawn/species association in this audit",
        GrowthFraction = vitals.Growth,
        GrowthPercent = vitals.Growth is { } growth ? Math.Round(growth * 100, 2) : (double?)null,
        vitals.Health,
        vitals.MaxHealth,
        vitals.Stamina,
        vitals.MaxStamina,
        vitals.Hunger,
        vitals.MaxHunger,
        vitals.Thirst,
        vitals.MaxThirst
    };
}

var report = new
{
    Capture = capturePath,
    CaptureSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(capturePath))),
    GroundTruthGrowthPercent = targetGrowthFraction * 100d,
    TotalPackets = packets.Count,
    TruncatedTail = truncatedTail,
    ValidationStatus = "NEED_STAGE_EVIDENCE",
    EvidenceLimitations = "Heuristic layouts and heuristic owner selection. VitalsSnapshots may carry old fields; timestamps are not per-field freshness. Scalar growth proximity is not synchronized ground truth or local-owner proof.",
    Endpoints = groups.OrderByDescending(pair => pair.Value.Packets).Select(pair => new
    {
        Endpoint = pair.Key,
        pair.Value.Packets,
        pair.Value.IrisPackets,
        pair.Value.CompleteIrisPackets,
        pair.Value.ObjectBatches,
        pair.Value.OwnerDataBatches,
        pair.Value.HeartbeatLayoutSizedBatches,
        pair.Value.GrowthLayoutSizedBatches,
        pair.Value.VitalsObservations,
        pair.Value.GrowthObservations,
        pair.Value.GrowthValueChanges,
        pair.Value.VerifiedMaximumObservations,
        OwnerHandles = pair.Value.OwnerHandles.Order().ToArray(),
        DecodedOwnerBatchEvidence = pair.Value.OwnerHandles.Order().Select(handle => new
        {
            Handle = handle,
            Batches = pair.Value.HandleBatches.GetValueOrDefault(handle)?.Total ?? 0,
            GrowthLayoutSizedBatches = pair.Value.HandleBatches.GetValueOrDefault(handle)?.GrowthLayoutSized ?? 0
        }).ToArray(),
        pair.Value.FirstAt,
        pair.Value.LastAt,
        pair.Value.LastVitals,
        pair.Value.VitalsSnapshots,
        pair.Value.InboundTrialObservations,
        pair.Value.FocusBatches,
        pair.Value.LastGrowthFraction,
        pair.Value.FirstGrowthFraction,
        pair.Value.MinimumGrowthFraction,
        pair.Value.MaximumGrowthFraction,
        pair.Value.GrowthIncreasingSteps,
        pair.Value.GrowthDecreasingSteps,
        pair.Value.GrowthSamples,
        ClosestRepeatedAttributePairDistance = pair.Value.AttributeCandidates.Values
            .Where(candidate => candidate.Count >= 3)
            .Select(candidate => candidate.ClosestDistanceToGroundTruth)
            .Where(distance => distance is not null)
            .DefaultIfEmpty()
            .Min(),
        GrowthValueCandidates = pair.Value.AttributeCandidates.Values
            .Where(candidate => candidate.Count >= 3
                                && candidate.LastAt - candidate.FirstAt >= TimeSpan.FromSeconds(5))
            .OrderBy(candidate => candidate.ClosestDistanceToGroundTruth)
            .ThenByDescending(candidate => candidate.Count)
            .Take(30)
            .Select(candidate => new
            {
                candidate.OwnerHandle,
                candidate.RelativeBitOffset,
                candidate.Count,
                candidate.FirstAt,
                candidate.LastAt,
                candidate.FirstValue,
                candidate.LastValue,
                candidate.Minimum,
                candidate.Maximum,
                candidate.IncreasingSteps,
                candidate.DecreasingSteps,
                candidate.ClosestDistanceToGroundTruth
            }).ToArray(),
        FullGasFrames = pair.Value.FullFrames
            .OrderBy(frame => frame.ObservedAt)
            .Select(frame => new
            {
                frame.OwnerHandle,
                frame.ObservedAt,
                frame.DataBitCount,
                frame.GrowthFraction,
                GrowthPercent = Math.Round(frame.GrowthFraction * 100d, 4),
                frame.Health,
                frame.MaxHealth,
                frame.Stamina,
                frame.MaxStamina,
                frame.MaxHunger,
                DistanceToGroundTruth = targetGrowthFraction is { } expected
                    ? Math.Abs(frame.GrowthFraction - expected)
                    : (double?)null
            }).ToArray(),
        DebugFrames = pair.Value.DebugFrames,
        // Hypotheses only: a repeated float has no proven field meaning or owner relation.
        SingleFloatCandidates = pair.Value.SingleFloatCandidates.Values
            .Where(candidate => candidate.Count >= 3
                                && candidate.LastAt - candidate.FirstAt >= TimeSpan.FromSeconds(5))
            .OrderBy(candidate => candidate.ClosestDistanceToGroundTruth)
            .ThenByDescending(candidate => candidate.Count)
            .Take(30)
            .Select(candidate => new
            {
                candidate.OwnerHandle,
                candidate.RelativeBitOffset,
                candidate.Count,
                candidate.FirstAt,
                candidate.LastAt,
                candidate.FirstValue,
                candidate.LastValue,
                candidate.Minimum,
                candidate.Maximum,
                candidate.IncreasingSteps,
                candidate.DecreasingSteps,
                candidate.ClosestDistanceToGroundTruth
            }).ToArray(),
        DecodedOwnerSingleFloatCandidates = pair.Value.SingleFloatCandidates.Values
            .Where(candidate => pair.Value.OwnerHandles.Contains(candidate.OwnerHandle)
                                && candidate.Count >= 3
                                && candidate.LastAt - candidate.FirstAt >= TimeSpan.FromSeconds(5))
            .OrderBy(candidate => candidate.ClosestDistanceToGroundTruth)
            .ThenByDescending(candidate => candidate.Count)
            .Take(30)
            .Select(candidate => new
            {
                candidate.OwnerHandle,
                candidate.RelativeBitOffset,
                candidate.Count,
                candidate.FirstValue,
                candidate.LastValue,
                candidate.Minimum,
                candidate.Maximum,
                candidate.IncreasingSteps,
                candidate.DecreasingSteps,
                candidate.ClosestDistanceToGroundTruth
            }).ToArray(),
        Status = pair.Value.VitalsObservations == 0
            ? "NO_OWNER_VITALS_DECODED"
            : pair.Value.GrowthObservations == 0
                ? "OWNER_VITALS_WITHOUT_GROWTH"
                : "GROWTH_CANDIDATE_OWNER_AND_LAYOUT_UNVERIFIED"
    }).ToArray()
};

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length >= 2)
{
    var output = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, json, new UTF8Encoding(false));
    Console.WriteLine(output);
}
else Console.WriteLine(json);
return 0;

static void ObserveAttributePairs(
    EndpointAudit audit,
    ReadOnlySpan<byte> payload,
    UnrealIrisReplicationBatch batch,
    DateTimeOffset observedAt,
    double? targetGrowthFraction)
{
    for (var relative = 0; relative + 66 <= batch.DataBitCount; relative++)
    {
        var absolute = batch.DataBitOffset + relative;
        if (absolute + 66 > payload.Length * 8 || !IsBitSet(payload, absolute + 32))
            continue;
        var value = BitConverter.Int32BitsToSingle((int)ReadBits(payload, absolute, 32));
        if (!float.IsFinite(value) || value is <= 0.01f or >= 1f)
            continue;
        var duplicate = BitConverter.Int32BitsToSingle((int)ReadBits(payload, absolute + 33, 32));
        if (!float.IsFinite(duplicate)
            || Math.Abs(value - duplicate) > Math.Max(0.00001d, value * 0.000001d))
            continue;

        var key = (batch.NetRefHandle, relative);
        if (!audit.AttributeCandidates.TryGetValue(key, out var candidate))
            audit.AttributeCandidates[key] = candidate = new AttributePairCandidate(
                batch.NetRefHandle, relative, observedAt, value);
        candidate.Observe(observedAt, value, targetGrowthFraction);
    }
}

static bool TryReadFullGasFrame(
    ReadOnlySpan<byte> payload,
    UnrealIrisReplicationBatch batch,
    DateTimeOffset observedAt,
    out FullGasFrame frame)
{
    frame = default;
    if (!batch.HasOwnerData)
        return false;

    var layout = batch.DataBitCount switch
    {
        >= 1_460 and <= 1_490 => (Growth: 822, Health: 888, MaxHealth: 954, RepeatedMaxHealth: 1_350, Stamina: 1_020, MaxStamina: 1_086, MaxHunger: 228),
        >= 1_380 and <= 1_410 => (Growth: 731, Health: 797, MaxHealth: 863, RepeatedMaxHealth: -1, Stamina: 929, MaxStamina: 995, MaxHunger: 137),
        >= 1_480 and <= 1_510 => (Growth: 831, Health: 897, MaxHealth: 963, RepeatedMaxHealth: -1, Stamina: 1_029, MaxStamina: 1_095, MaxHunger: 237),
        _ => (Growth: -1, Health: -1, MaxHealth: -1, RepeatedMaxHealth: -1, Stamina: -1, MaxStamina: -1, MaxHunger: -1)
    };
    if (layout.Growth < 0)
        return false;

    double growth, health, maxHealth, repeatedMaxHealth = 0d;
    double stamina, maxStamina, maxHunger;
    if (!TryReadAttribute(payload, batch, layout.Growth, out growth)
        || !TryReadAttribute(payload, batch, layout.Health, out health)
        || !TryReadAttribute(payload, batch, layout.MaxHealth, out maxHealth)
        || !TryReadAttribute(payload, batch, layout.Stamina, out stamina)
        || !TryReadAttribute(payload, batch, layout.MaxStamina, out maxStamina)
        || !TryReadAttribute(payload, batch, layout.MaxHunger, out maxHunger)
        || layout.RepeatedMaxHealth >= 0
        && (!TryReadAttribute(payload, batch, layout.RepeatedMaxHealth, out repeatedMaxHealth)
            || Math.Abs(maxHealth - repeatedMaxHealth)
               > Math.Max(0.0001d, Math.Abs(maxHealth) * 0.000001d))
        || growth > 1.001d
        || maxHealth <= 0d
        || maxStamina <= 0d
        || maxHunger <= 0d
        || health > maxHealth * 1.01d
        || stamina > maxStamina * 1.01d)
    {
        return false;
    }

    frame = new FullGasFrame(
        batch.NetRefHandle,
        observedAt,
        batch.DataBitCount,
        growth,
        health,
        maxHealth,
        stamina,
        maxStamina,
        maxHunger);
    return true;
}

static object ReadDebugFrame(
    ReadOnlySpan<byte> payload,
    UnrealIrisReplicationBatch batch,
    DateTimeOffset observedAt)
{
    var offsets = new[] { 228, 822, 855, 888, 921, 954, 987, 1_020, 1_053, 1_086, 1350, 1383, 1416 };
    var values = new List<object>();
    foreach (var relative in offsets)
    {
        values.Add(new
        {
            Relative = relative,
            Separator = IsBitSet(payload, batch.DataBitOffset + relative + 32),
            First = ReadFloatAt(payload, batch.DataBitOffset + relative),
            Second = ReadFloatAt(payload, batch.DataBitOffset + relative + 33),
            FirstBits = ReadBits(payload, batch.DataBitOffset + relative, 32),
            SecondBits = ReadBits(payload, batch.DataBitOffset + relative + 33, 32)
        });
    }

    var batchBytes = new byte[(batch.DataBitCount + 7) / 8];
    for (var bit = 0; bit < batch.DataBitCount; bit++)
        if (IsBitSet(payload, batch.DataBitOffset + bit))
            batchBytes[bit >> 3] |= (byte)(1 << (bit & 7));
    return new
    {
        At = observedAt,
        batch.NetRefHandle,
        batch.DataBitCount,
        BatchBase64 = Convert.ToBase64String(batchBytes),
        Values = values.ToArray()
    };
}

static float ReadFloatAt(ReadOnlySpan<byte> payload, int bitOffset) =>
    BitConverter.Int32BitsToSingle((int)ReadBits(payload, bitOffset, 32));

static void ObserveSingleFloats(
    EndpointAudit audit,
    ReadOnlySpan<byte> payload,
    UnrealIrisReplicationBatch batch,
    DateTimeOffset observedAt,
    double? targetGrowthFraction)
{
    for (var relative = 0; relative + 32 <= batch.DataBitCount; relative++)
    {
        var absolute = batch.DataBitOffset + relative;
        if (absolute + 32 > payload.Length * 8) continue;
        var value = BitConverter.Int32BitsToSingle((int)ReadBits(payload, absolute, 32));
        // Diagnostic hypotheses over the full growth-fraction range; the old
        // 0.1..0.5 gate could never find a 73% candidate. Not admission proof.
        if (!float.IsFinite(value) || value is <= 0f or > 1f) continue;
        var key = (batch.NetRefHandle, relative);
        if (!audit.SingleFloatCandidates.TryGetValue(key, out var candidate))
            audit.SingleFloatCandidates[key] = candidate = new AttributePairCandidate(
                batch.NetRefHandle, relative, observedAt, value);
        candidate.Observe(observedAt, value, targetGrowthFraction);
    }
}

static bool IsBitSet(ReadOnlySpan<byte> payload, int bitOffset) =>
    (payload[bitOffset >> 3] & (1 << (bitOffset & 7))) != 0;

static uint ReadBits(ReadOnlySpan<byte> payload, int bitOffset, int count)
{
    uint result = 0;
    for (var bit = 0; bit < count; bit++)
        if (IsBitSet(payload, bitOffset + bit)) result |= 1u << bit;
    return result;
}

static bool TryReadAttribute(
    ReadOnlySpan<byte> payload,
    UnrealIrisReplicationBatch batch,
    int relativeBitOffset,
    out double value)
{
    value = 0d;
    const int attributeBits = 66;
    if (relativeBitOffset < 0
        || relativeBitOffset + attributeBits > batch.DataBitCount)
    {
        return false;
    }

    var absolute = batch.DataBitOffset + relativeBitOffset;
    if (!IsBitSet(payload, absolute + 32))
    {
        return false;
    }

    var first = BitConverter.Int32BitsToSingle((int)ReadBits(payload, absolute, 32));
    var second = BitConverter.Int32BitsToSingle((int)ReadBits(payload, absolute + 33, 32));
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

static List<RecordedPacket> ReadCapture(string path, out bool truncatedTail)
{
    truncatedTail = false;
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream, Encoding.UTF8);
    if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ISLEIN01")
        throw new InvalidDataException("Expected an ISLEIN01 capture.");

    var packets = new List<RecordedPacket>();
    while (stream.Position < stream.Length)
    {
        var offset = stream.Position;
        try
        {
            var observedAt = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var sourceAddress = reader.ReadString();
            var sourcePort = reader.ReadUInt16();
            _ = reader.ReadString();
            _ = reader.ReadUInt16();
            var length = reader.ReadInt32();
            if (length is <= 0 or > 65_535 || stream.Length - stream.Position < length)
            {
                truncatedTail = true;
                break;
            }
            packets.Add(new RecordedPacket(offset, observedAt, sourceAddress, sourcePort, reader.ReadBytes(length)));
        }
        catch (EndOfStreamException)
        {
            // A capture interrupted mid-write keeps all complete prior packets.
            truncatedTail = true;
            break;
        }
    }

    return packets;
}

internal sealed record RecordedPacket(long Offset, DateTimeOffset ObservedAt, string SourceAddress,
    ushort SourcePort, byte[] Payload);

internal sealed class EndpointAudit
{
    public InboundStatsAccumulator InboundTrial { get; } = new();
    public List<object> InboundTrialObservations { get; } = [];
    public UnrealIrisPacketParser Parser { get; } = new();
    public UnrealDinosaurVitalsTracker Tracker { get; } = new();
    public HashSet<ulong> OwnerHandles { get; } = [];
    public Dictionary<ulong, HandleBatchAudit> HandleBatches { get; } = [];
    public Dictionary<(ulong Handle, int RelativeBitOffset), AttributePairCandidate> AttributeCandidates { get; } = [];
    public Dictionary<(ulong Handle, int RelativeBitOffset), AttributePairCandidate> SingleFloatCandidates { get; } = [];
    public long Packets { get; set; }
    public long IrisPackets { get; set; }
    public long CompleteIrisPackets { get; set; }
    public long ObjectBatches { get; set; }
    public long OwnerDataBatches { get; set; }
    public long HeartbeatLayoutSizedBatches { get; set; }
    public long GrowthLayoutSizedBatches { get; set; }
    public long VitalsObservations { get; set; }
    public long GrowthObservations { get; set; }
    public long GrowthValueChanges { get; set; }
    public long VerifiedMaximumObservations { get; set; }
    public double? LastGrowthFraction { get; set; }
    public double? FirstGrowthFraction { get; set; }
    public double? MinimumGrowthFraction { get; set; }
    public double? MaximumGrowthFraction { get; set; }
    public long GrowthIncreasingSteps { get; set; }
    public long GrowthDecreasingSteps { get; set; }
    public List<GrowthSample> GrowthSamples { get; } = [];
    public List<object> VitalsSnapshots { get; } = [];
    public List<object> FocusBatches { get; } = [];
    public List<FullGasFrame> FullFrames { get; } = [];
    public List<object> DebugFrames { get; } = [];
    public DateTimeOffset? FirstAt { get; set; }
    public DateTimeOffset? LastAt { get; set; }
    public object? LastVitals { get; set; }
}

internal readonly record struct FullGasFrame(
    ulong OwnerHandle,
    DateTimeOffset ObservedAt,
    int DataBitCount,
    double GrowthFraction,
    double Health,
    double MaxHealth,
    double Stamina,
    double MaxStamina,
    double MaxHunger);

internal sealed record GrowthSample(DateTimeOffset At, ulong OwnerHandle, double Fraction);

internal sealed class AttributePairCandidate
{
    public AttributePairCandidate(ulong ownerHandle, int relativeBitOffset, DateTimeOffset firstAt, double firstValue)
    {
        OwnerHandle = ownerHandle;
        RelativeBitOffset = relativeBitOffset;
        FirstAt = firstAt;
        LastAt = firstAt;
        FirstValue = firstValue;
        LastValue = firstValue;
        Minimum = firstValue;
        Maximum = firstValue;
    }

    public ulong OwnerHandle { get; }
    public int RelativeBitOffset { get; }
    public long Count { get; private set; }
    public DateTimeOffset FirstAt { get; }
    public DateTimeOffset LastAt { get; private set; }
    public double FirstValue { get; }
    public double LastValue { get; private set; }
    public double Minimum { get; private set; }
    public double Maximum { get; private set; }
    public long IncreasingSteps { get; private set; }
    public long DecreasingSteps { get; private set; }
    public double? ClosestDistanceToGroundTruth { get; private set; }

    public void Observe(DateTimeOffset observedAt, double value, double? target)
    {
        if (Count > 0)
        {
            if (value > LastValue + 0.000001d) IncreasingSteps++;
            if (value < LastValue - 0.000001d) DecreasingSteps++;
        }
        Count++;
        LastAt = observedAt;
        LastValue = value;
        Minimum = Math.Min(Minimum, value);
        Maximum = Math.Max(Maximum, value);
        if (target is { } groundTruth)
        {
            var distance = Math.Abs(value - groundTruth);
            ClosestDistanceToGroundTruth = Math.Min(ClosestDistanceToGroundTruth ?? double.PositiveInfinity, distance);
        }
    }
}

internal sealed class HandleBatchAudit
{
    public long Total { get; set; }
    public long GrowthLayoutSized { get; set; }
}
