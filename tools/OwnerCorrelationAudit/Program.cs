using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: OwnerCorrelationAudit inbound.bin outbound.bin output.json server:port");
    return 2;
}

var inbound = ReadCapture(Path.GetFullPath(args[0]), inbound: true);
var outbound = ReadCapture(Path.GetFullPath(args[1]), inbound: false);
var events = inbound.Concat(outbound).Where(item => item.ServerEndpoint == args[3])
    .OrderBy(item => item.At).ThenBy(item => item.Offset).ToArray();
var movement = new LocalMovementTracker();
var parser = new UnrealIrisPacketParser();
var exports = new IrisObjectExportTracker();
var creationScanner = new UnrealIrisActorCreationScanner();
var local = new IrisLocalCreatureTracker(objectExportTracker: exports);
var localSamples = new List<object>();
var actorTransitions = new List<object>();
var actorCreations = new List<CreationEvidence>();
var gasFrames = new List<object>();
var seenHandles = new HashSet<ulong>();
var lastLocal = new WorldLocation();
var hasLocal = false;
DateTimeOffset? lastLocalAt = null;
object? firstLocal = null;
string? activeFlow = null;
var flowResets = 0;
var partialBunches = 0;
var inboundPackets = 0;
var parsedInboundPackets = 0;
var outboundPackets = 0;
var parsedOutboundMovement = 0;

foreach (var item in events)
{
    if (activeFlow != item.Flow)
    {
        activeFlow = item.Flow;
        flowResets++;
        movement.Reset();
        local.Reset();
        hasLocal = false;
        lastLocalAt = null;
    }
    if (!item.Inbound)
    {
        outboundPackets++;
        if (movement.TryTrack(item.Payload, item.At, out var sample))
        {
            parsedOutboundMovement++;
            lastLocal = sample.Location;
            hasLocal = true;
            lastLocalAt = item.At;
            firstLocal ??= new { item.At, sample.X, sample.Y, sample.Z };
            local.Current(lastLocal);
            if (local.CurrentActorHandle is { } actor)
                seenHandles.Add(actor);
        }
        continue;
    }

    inboundPackets++;
    if (!parser.TryParse(item.Payload, out var packet) || !packet.HasDataStream)
        continue;
    parsedInboundPackets++;
    partialBunches += packet.Bunches?.Count(b => b.IsPartial) ?? 0;
    foreach (var batch in packet.Batches)
    {
        if (creationScanner.TryRead(item.Payload, batch, out var creation))
        {
            actorCreations.Add(new CreationEvidence(
                item.At,
                creation.NetRefHandle,
                creation.ProtocolId,
                creation.ArchetypeNetRefHandle,
                creation.SpawnLocation,
                creation.LocationWasSerialized,
                hasLocal && lastLocalAt is { } gpsAt && item.At - gpsAt <= TimeSpan.FromSeconds(2)
                    && creation.LocationWasSerialized
                    ? Distance(lastLocal, creation.SpawnLocation)
                    : (double?)null,
                batch.DataBitCount));
        }

        if (TryReadGasFrame(item.Payload, batch, out var gas))
        {
            gasFrames.Add(new
            {
                item.At,
                gas.OwnerHandle,
                gas.DataBitCount,
                gas.Growth,
                GrowthPercent = gas.Growth * 100d,
                gas.Health,
                gas.MaxHealth,
                gas.Stamina,
                gas.MaxStamina,
                gas.MaxHunger,
                DistanceFromLocal = (double?)null
            });
        }
    }
    Observe(item.Payload, item.At);
}

var result = new
{
    Inbound = args[0],
    Outbound = args[1],
    InboundSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))),
    OutboundSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))),
    Endpoint = args[3],
    FlowResets = flowResets,
    PartialBunches = partialBunches,
    Status = "NEED_STAGE_EVIDENCE",
    Limitation = "GPS proximity is not owner proof. GAS labels are decoder hypotheses. This probe does not reassemble partial bunches or infer local identity without fresh GPS.",
    InboundPackets = inboundPackets,
    ParsedInboundPackets = parsedInboundPackets,
    OutboundPackets = outboundPackets,
    ParsedOutboundMovement = parsedOutboundMovement,
    HasLocalMovement = hasLocal,
    FirstLocal = firstLocal,
    LastLocal = hasLocal ? new { At = lastLocalAt, lastLocal.X, lastLocal.Y, lastLocal.Z } : null,
    CurrentLocalActorHandle = local.CurrentActorHandle,
    LocalActorHandles = seenHandles.Order().ToArray(),
    ActorTransitions = actorTransitions,
    ActorCreations = actorCreations
        .OrderBy(item => item.DistanceFromLocal ?? double.MaxValue)
        .Take(500)
        .ToArray(),
    GasFrames = gasFrames,
    LocalCreature = hasLocal ? local.Current(lastLocal) : null
};
File.WriteAllText(
    Path.GetFullPath(args[2]),
    JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(Path.GetFullPath(args[2]));
return 0;

void Observe(byte[] payload, DateTimeOffset at)
{
    var before = local.CurrentActorHandle;
    var freshGps = hasLocal && lastLocalAt is { } latest && at - latest <= TimeSpan.FromSeconds(2);
    local.Observe(payload, at, freshGps ? lastLocal : null);
    var after = local.CurrentActorHandle;
    if (after is { } handle)
        seenHandles.Add(handle);
    if (after != before)
    {
        actorTransitions.Add(new
        {
            At = at,
            Previous = before,
            Current = after,
            Local = lastLocal,
            Species = hasLocal ? local.Current(lastLocal) : null
        });
    }
    if (after is { } current && localSamples.Count < 200)
    {
        localSamples.Add(new
        {
            At = at,
            Actor = current,
            Local = lastLocal,
            Species = hasLocal ? local.Current(lastLocal) : null
        });
    }
}

static IReadOnlyList<RecordedEvent> ReadCapture(string path, bool inbound)
{
    var rows = new List<RecordedEvent>();
    using var stream = File.OpenRead(path);
    using var reader = new BinaryReader(stream, Encoding.UTF8);
    if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ISLEIN01")
        throw new InvalidDataException($"Invalid capture header: {path}");
    while (stream.Position < stream.Length)
    {
        var offset = stream.Position;
        try
        {
            var at = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var source = reader.ReadString();
            var sourcePort = reader.ReadUInt16();
            var destination = reader.ReadString();
            var destinationPort = reader.ReadUInt16();
            var length = reader.ReadInt32();
            if (length is <= 0 or > 65_535 || stream.Length - stream.Position < length)
                throw new InvalidDataException($"Invalid or truncated record at {offset} in {path}.");
            var server = inbound ? $"{source}:{sourcePort}" : $"{destination}:{destinationPort}";
            var client = inbound ? $"{destination}:{destinationPort}" : $"{source}:{sourcePort}";
            rows.Add(new RecordedEvent(at, offset, inbound, server, $"{client}->{server}", reader.ReadBytes(length)));
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException($"Truncated record at {offset} in {path}.");
        }
    }
    return rows;
}

static double Distance(WorldLocation left, WorldLocation right) =>
    Math.Sqrt(
        Math.Pow(left.X - right.X, 2)
        + Math.Pow(left.Y - right.Y, 2)
        + Math.Pow((left.Z ?? 0d) - (right.Z ?? 0d), 2));

static bool TryReadGasFrame(
    ReadOnlySpan<byte> payload,
    UnrealIrisReplicationBatch batch,
    out GasFrame frame)
{
    frame = default;
    var layout = batch.DataBitCount switch
    {
        >= 1_380 and <= 1_410 => (Growth: 731, Health: 797, MaxHealth: 863, Stamina: 929, MaxStamina: 995, MaxHunger: 137),
        >= 1_480 and <= 1_510 => (Growth: 831, Health: 897, MaxHealth: 963, Stamina: 1_029, MaxStamina: 1_095, MaxHunger: 237),
        _ => (Growth: -1, Health: -1, MaxHealth: -1, Stamina: -1, MaxStamina: -1, MaxHunger: -1)
    };
    if (!batch.HasOwnerData || layout.Growth < 0
        || !TryReadAttribute(payload, batch, layout.Growth, out var growth)
        || !TryReadAttribute(payload, batch, layout.Health, out var health)
        || !TryReadAttribute(payload, batch, layout.MaxHealth, out var maxHealth)
        || !TryReadAttribute(payload, batch, layout.Stamina, out var stamina)
        || !TryReadAttribute(payload, batch, layout.MaxStamina, out var maxStamina)
        || !TryReadAttribute(payload, batch, layout.MaxHunger, out var maxHunger)
        || growth > 1.001d
        || maxHealth <= 0d
        || maxStamina <= 0d
        || maxHunger <= 0d
        || health > maxHealth * 1.01d
        || stamina > maxStamina * 1.01d)
        return false;

    frame = new GasFrame(
        batch.NetRefHandle,
        batch.DataBitCount,
        growth,
        health,
        maxHealth,
        stamina,
        maxStamina,
        maxHunger);
    return true;
}

static bool TryReadAttribute(
    ReadOnlySpan<byte> payload,
    UnrealIrisReplicationBatch batch,
    int relative,
    out double value)
{
    value = 0d;
    if (relative < 0 || relative + 66 > batch.DataBitCount
        || batch.DataBitOffset < 0 || batch.DataBitOffset + relative + 66 > payload.Length * 8)
        return false;
    var absolute = batch.DataBitOffset + relative;
    if (!IsBitSet(payload, absolute + 32))
        return false;
    var first = BitConverter.Int32BitsToSingle((int)ReadBits(payload, absolute, 32));
    var second = BitConverter.Int32BitsToSingle((int)ReadBits(payload, absolute + 33, 32));
    if (!float.IsFinite(first)
        || !float.IsFinite(second)
        || first < 0f
        || first > 100_000f
        || Math.Abs(first - second) > Math.Max(0.0001f, Math.Abs(first) * 0.000001f))
        return false;
    value = first;
    return true;
}

static bool IsBitSet(ReadOnlySpan<byte> payload, int bit) =>
    bit >= 0
    && bit < payload.Length * 8
    && (payload[bit >> 3] & (1 << (bit & 7))) != 0;

static uint ReadBits(ReadOnlySpan<byte> payload, int offset, int count)
{
    uint result = 0;
    for (var bit = 0; bit < count; bit++)
        if (IsBitSet(payload, offset + bit))
            result |= 1u << bit;
    return result;
}

internal readonly record struct GasFrame(
    ulong OwnerHandle,
    int DataBitCount,
    double Growth,
    double Health,
    double MaxHealth,
    double Stamina,
    double MaxStamina,
    double MaxHunger);

internal sealed record RecordedEvent(DateTimeOffset At, long Offset, bool Inbound,
    string ServerEndpoint, string Flow, byte[] Payload);

internal sealed record CreationEvidence(DateTimeOffset At, ulong NetRefHandle,
    uint ProtocolId, ulong ArchetypeNetRefHandle, WorldLocation SpawnLocation,
    bool LocationWasSerialized, double? DistanceFromLocal, int DataBitCount);
