using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.LocalTelemetry;

if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: InboundActorAudit capture.bin server:port focusHandle output.json");
    return 2;
}
var path = Path.GetFullPath(args[0]);
var focus = ulong.Parse(args[2]);
var parser = new UnrealIrisPacketParser();
var assembler = new UnrealIrisPartialBunchAssembler();
var exports = new IrisObjectExportTracker();
var scanner = new UnrealIrisActorCreationScanner();
var references = new UnrealIrisObjectReferenceScanner();
var creations = new List<object>();
var focusHeaders = new List<object>();
var matchingExports = new List<object>();
var diagnostics = new List<object>();
var packets = 0;
var partials = 0;
var assembled = 0;
var truncatedTail = false;
string? flow = null;
using var stream = File.OpenRead(path);
using var reader = new BinaryReader(stream, Encoding.UTF8);
if (Encoding.ASCII.GetString(reader.ReadBytes(8)) != "ISLEIN01")
    throw new InvalidDataException("Expected ISLEIN01.");
while (stream.Position < stream.Length)
{
    var offset = stream.Position;
    DateTimeOffset at;
    byte[] payload;
    string source, destination;
    ushort sourcePort, destinationPort;
    try
    {
        at = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        source = reader.ReadString();
        sourcePort = reader.ReadUInt16();
        destination = reader.ReadString();
        destinationPort = reader.ReadUInt16();
        var length = reader.ReadInt32();
        if (length is <= 0 or > 65535) throw new InvalidDataException($"Invalid length at {offset}.");
        if (stream.Length - stream.Position < length) { truncatedTail = true; break; }
        payload = reader.ReadBytes(length);
    }
    catch (EndOfStreamException) { truncatedTail = true; break; }
    if ($"{source}:{sourcePort}" != args[1]) continue;
    var nextFlow = $"{source}:{sourcePort}->{destination}:{destinationPort}";
    if (flow != nextFlow)
    {
        assembler.Reset();
        exports.Reset();
        flow = nextFlow;
    }
    packets++;
    if (!parser.TryParse(payload, out var packet)) continue;
    partials += packet.Bunches?.Count(b => b.IsPartial) ?? 0;
    Inspect(payload, packet.Batches, at, offset, packet.PacketSequence, false);
    foreach (var bunch in assembler.Observe(payload, packet, at).Where(b => b.IsReassembled))
    {
        assembled++;
        if (!parser.TryParseDataStreamPayload(bunch.Payload, bunch.PayloadBitCount, out var data))
        {
            diagnostics.Add(new { at, offset, status = "reassembled-stream-parse-failed", bunch.FragmentCount });
            continue;
        }
        diagnostics.Add(new { at, offset, data.IsComplete, data.IncompleteReason, bunch.FragmentCount, batches = data.Batches.Count });
        Inspect(bunch.Payload, data.Batches, at, offset, bunch.LastPacketSequence, true);
    }
}
var report = new
{
    Capture = path, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
    Packets = packets, PartialBunches = partials, ReassembledBunches = assembled, TruncatedTail = truncatedTail,
    FocusHandle = focus, Creations = creations, FocusHeaders = focusHeaders,
    MatchingExports = matchingExports, AssemblyDiagnostics = diagnostics,
    Limitation = "References are scanning hypotheses, not owner proof. Creation rows include unrelated roots for comparison."
};
File.WriteAllText(args[3], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(args[3]);
return 0;

void Inspect(byte[] payload, IReadOnlyList<UnrealIrisReplicationBatch> batches, DateTimeOffset at,
    long offset, int sequence, bool reassembled)
{
    foreach (var batch in batches)
    {
        var exported = exports.Observe(payload, batch);
        foreach (var item in exported.Where(e => e.NetRefHandle == focus || e.OuterNetRefHandle == focus))
            matchingExports.Add(new { at, offset, sequence, reassembled, batch.NetRefHandle, Export = item });
        if (scanner.TryRead(payload, batch, out var creation))
        {
            exports.TryGetObject(creation.ArchetypeNetRefHandle, out var archetype);
            creations.Add(new { at, offset, sequence, reassembled, Creation = creation, Archetype = archetype });
        }
        if (batch.NetRefHandle != focus) continue;
        if (focusHeaders.Count < 30 || reassembled)
        {
            var header = new StringBuilder();
            for (var bit = 0; bit < Math.Min(64, batch.DataBitCount); bit++)
            {
                var absolute = batch.DataBitOffset + bit;
                header.Append((payload[absolute >> 3] & (1 << (absolute & 7))) != 0 ? '1' : '0');
            }
            focusHeaders.Add(new
            {
                at,
                offset,
                sequence,
                reassembled,
                Batch = batch,
                HeaderBits = header.ToString(),
                References = references.Find(payload, batch)
                    .DistinctBy(reference => reference.NetRefHandle)
                    .Take(80)
                    .ToArray()
            });
        }
    }
}
