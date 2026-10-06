using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

if (args.Length != 2) return 2;
var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Report already exists.");
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() }
};
// An exclusive reader prevents replaying a capture that is still being written.
using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
var hash = Convert.ToHexString(SHA256.HashData(input));
input.Position = 0;
using var reader = new StreamReader(input);
TelemetrySnapshot? previous = null;
string? lastScope = null;
long frames = 0, received = 0, rendered = 0, stale = 0, changedCoordinates = 0;
var unique = new HashSet<string>();
var rejections = new Dictionary<string, long>();
while (reader.ReadLine() is { } line)
{
    using var doc = JsonDocument.Parse(line);
    var root = doc.RootElement;
    if (!root.TryGetProperty("RemoteEntities", out var values)) continue;
    var entities = values.Deserialize<VerifiedRemoteEntityTelemetry[]>(options) ?? [];
    var now = root.GetProperty("ReceivedAt").GetDateTimeOffset();
    var scope = root.GetProperty("SessionId").GetString() + "|"
        + root.GetProperty("ServerEndpoint").GetString();
    if (scope != lastScope) previous = null;
    lastScope = scope;
    // Isolate the shared Host merger, including the PRO-only path. No fake
    // provider traffic or inferred game population is used in this replay.
    var merged = LocalPositionSnapshotMerger.Merge(previous, null, now, remotePlayers: entities);
    frames++;
    received += entities.Length;
    foreach (var marker in merged.Map?.Markers ?? [])
    {
        rendered++;
        if (marker.ProEntityIsStale) stale++;
        unique.Add(scope + "|" + marker.SteamId);
        var entity = entities.FirstOrDefault(e => marker.SteamId ==
            $"pro-entity:{e.Kind.ToString().ToLowerInvariant()}:{e.TrackId}");
        if (entity is null || marker.Location != entity.Location) changedCoordinates++;
    }
    foreach (var pair in merged.ProTrackingDiagnostics?.Rejections
        ?? new Dictionary<RemoteEntityRejectionReason, int>())
        rejections[pair.Key.ToString()] = rejections.GetValueOrDefault(pair.Key.ToString()) + pair.Value;
    previous = merged;
}
#if ACCEPTANCE_BINARY
const string build = "acceptance-host-history-binary";
#elif RELEASE_BASELINE
const string build = "published-v2.4.0";
#else
const string build = "working-tree-relaxed-host";
#endif
var result = new
{
    Build = build,
    Scope = "Frozen Host IPC through actual Host merger; entity-frame counts, not independent player recall or live render proof",
    InputSha256 = hash, frames, received, rendered, stale,
    UniqueRenderedIdentities = unique.Count, changedCoordinates, rejections
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));
return changedCoordinates == 0 ? 0 : 1;
