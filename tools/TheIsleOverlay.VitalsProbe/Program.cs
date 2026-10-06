using System.Text;
using TheIsleOverlay.LocalTelemetry;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

if (args.Length == 4 && args[0] == "replay-pipeline")
{
    var frames = ReadRaw(args[1], true).Concat(ReadRaw(args[2], false)).OrderBy(p => p.ObservedAt).ToArray();
    Environment.SetEnvironmentVariable(LocalVitalsFeature.ReplaceIslePilotEnvironmentVariable, "1");
    await using var source = new NpcapLocalMovementSource(enableLocalVitals: true);
    // No PID/start ticks: replay cannot restore or write live cache.
    const string replaySession = "replay";
    source.SetReplayGameSession(replaySession);
    var channel = Channel.CreateUnbounded<LocalMovementObservation>();
    LocalDinosaurVitalsObservation? previous = null;
    var transitions = new List<object>();
    var losses = 0; var samples = 0;
    foreach (var packet in frames)
    {
        if (packet.Inbound) source.ProcessInboundPacket(packet, replaySession, channel.Writer);
        else source.ProcessOutboundPacket(packet, channel.Writer);
        while (channel.Reader.TryRead(out var published))
        {
            if (published.DinosaurVitals is not { } stats || stats.ExperimentalEvidence is not { } evidence) continue;
            var current = InboundStatsAccumulator.LastKnownVitals(evidence, packet.ObservedAt);
            var old = previous?.ExperimentalEvidence is { } oldEvidence
                ? InboundStatsAccumulator.LastKnownVitals(oldEvidence, packet.ObservedAt) : null;
            samples++;
            if (old?.Health is not null && current.Health is null) losses++;
            if (old?.Health != current.Health || old?.MaxHunger != current.MaxHunger
                || old?.Stamina != current.Stamina || previous?.NetRefHandle != stats.NetRefHandle)
                transitions.Add(new { At = packet.ObservedAt, Owner = stats.NetRefHandle, Vitals = current });
            previous = stats;
        }
    }
    using var reportStream = new FileStream(args[3], FileMode.CreateNew);
    JsonSerializer.Serialize(reportStream, new { PacketCount = frames.Length, Samples = samples, CurrentLosses = losses,
        Diagnostics = source.GetLocalVitalsDiagnostics(), Transitions = transitions,
        InboundSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1]))),
        OutboundSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[2]))) }, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine($"packets={frames.Length} samples={samples} losses={losses} resets={source.GetLocalVitalsDiagnostics().SessionResets}");
    return 0;
}

if (args.Length is 1 or 2 && string.Equals(args[0], "live", StringComparison.OrdinalIgnoreCase))
{
    var seconds = args.Length == 2 ? int.Parse(args[1]) : 20;
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
    await using var source = new NpcapLocalMovementSource(enableLocalVitals: true);
    try
    {
        await foreach (var observation in source.WatchAsync(cancellation.Token))
        {
            var value = observation.DinosaurVitals?.Vitals;
            var movement = observation.HasMovement
                ? $"{observation.Movement.X:0.00},{observation.Movement.Y:0.00},{observation.Movement.Z:0.00}"
                : "—";
            Console.WriteLine(
                $"movementAt={(observation.HasMovement ? observation.ObservedAt.ToString("HH:mm:ss.fff") : "—")} " +
                $"vitalsAt={observation.DinosaurVitals?.ObservedAt:HH:mm:ss.fff} " +
                $"xyz={movement} " +
                $"growth={value?.Growth:R} " +
                $"hp={value?.Health:R}/{value?.MaxHealth:R} " +
                $"stamina={value?.Stamina:R}/{value?.MaxStamina:R} " +
                $"hunger={value?.Hunger:R}/{value?.MaxHunger:R} " +
                $"thirst={value?.Thirst:R}/{value?.MaxThirst:R}");
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
    }

    var diagnostics = source.GetPipelineDiagnostics();
    var lanes = source.GetLaneDiagnostics();
    var vitalsDiagnostics = source.GetLocalVitalsDiagnostics();
    Console.Error.WriteLine(
        $"pipeline captured={diagnostics.CapturedPackets} " +
        $"processed={diagnostics.ProcessedPackets} " +
        $"queueDrops={diagnostics.QueueDroppedPackets} " +
        $"queueHigh={diagnostics.QueueHighWatermark} " +
        $"iris={diagnostics.IrisPackets} " +
        $"incomplete={diagnostics.IncompleteIrisPackets} " +
        $"sequenceGaps={diagnostics.SequenceGapPackets} " +
        $"reordered={diagnostics.ReorderedPackets} " +
        $"duplicates={diagnostics.DuplicatePackets}");
    Console.Error.WriteLine(
        $"lanes outboundCaptured={lanes.Outbound.CapturedPackets} " +
        $"outboundDrops={lanes.Outbound.QueueDroppedPackets} " +
        $"inboundCaptured={lanes.Inbound.CapturedPackets} " +
        $"inboundDrops={lanes.Inbound.QueueDroppedPackets} " +
        $"localVitals={vitalsDiagnostics.Enabled} " +
        $"publishedVitals={vitalsDiagnostics.PublishedObservations}");

    return 0;
}

if (args.Length == 4 && string.Equals(args[0], "cache", StringComparison.OrdinalIgnoreCase))
{
    SeedVerifiedCache(args[1], args[2], args[3]);
    return 0;
}

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: <ISLETR01 capture> | live [seconds]");
    return 2;
}

using var stream = File.OpenRead(Path.GetFullPath(args[0]));
using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ISLETR01")
{
    throw new InvalidDataException("Unsupported capture.");
}

var tracker = new UnrealDinosaurVitalsTracker();
var sequence = 0;
while (stream.Position < stream.Length)
{
    var at = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
    var direction = reader.ReadByte();
    _ = reader.ReadString();
    _ = reader.ReadUInt16();
    _ = reader.ReadString();
    _ = reader.ReadUInt16();
    var payload = reader.ReadBytes(reader.ReadInt32());
    sequence++;
    if (direction != 1 || !tracker.TryTrack(payload, at, out var observation))
    {
        continue;
    }

    var value = observation.Vitals;
    Console.WriteLine(
        $"{sequence,5} {at:HH:mm:ss.fff} handle={observation.NetRefHandle} " +
        $"growth={value.Growth:R} " +
        $"hp={value.Health:R}/{value.MaxHealth:R} " +
        $"stamina={value.Stamina:R}/{value.MaxStamina:R} " +
        $"hunger={value.Hunger:R}/{value.MaxHunger:R} " +
        $"thirst={value.Thirst:R}/{value.MaxThirst:R}");
}

return 0;

static IEnumerable<CapturedUdpDatagram> ReadRaw(string path, bool inbound)
{
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var reader = new BinaryReader(stream, Encoding.UTF8);
    if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ISLEIN01") throw new InvalidDataException("ISLEIN01 required");
    while (stream.Position < stream.Length)
    {
        var at = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        var source = reader.ReadString(); var sourcePort = reader.ReadUInt16();
        var target = reader.ReadString(); var targetPort = reader.ReadUInt16();
        var length = reader.ReadInt32();
        if (length <= 0 || length > 65535 || length > stream.Length - stream.Position) throw new InvalidDataException("Invalid record");
        yield return new(at, source, sourcePort, target, targetPort, reader.ReadBytes(length), Inbound: inbound, Outbound: !inbound);
    }
}

static void SeedVerifiedCache(
    string capturePath,
    string gameSessionId,
    string serverEndpoint)
{
    using var stream = File.OpenRead(Path.GetFullPath(capturePath));
    using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
    if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ISLETR01")
    {
        throw new InvalidDataException("Unsupported capture.");
    }

    var tracker = new UnrealDinosaurVitalsTracker();
    var cache = new LocalVitalsSessionCache();
    var saved = 0;
    while (stream.Position < stream.Length)
    {
        var at = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        var direction = reader.ReadByte();
        _ = reader.ReadString();
        _ = reader.ReadUInt16();
        _ = reader.ReadString();
        _ = reader.ReadUInt16();
        var payload = reader.ReadBytes(reader.ReadInt32());
        if (direction != 1
            || !tracker.TryTrack(payload, at, out var observation)
            || observation.Vitals.MaxHealth is not > 0d
            || observation.Vitals.MaxStamina is not > 0d
            || observation.Vitals.MaxHunger is not > 0d)
        {
            continue;
        }

        cache.Enrich(gameSessionId, serverEndpoint, observation);
        saved++;
    }

    Console.WriteLine($"Cached {saved} verified observations for {gameSessionId} {serverEndpoint}.");
}
