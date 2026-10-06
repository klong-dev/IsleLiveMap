using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;
if (args.Length != 2) return 2;
var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Report exists");
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
var prepare = typeof(LocalPositionTelemetrySession).GetMethod("PrepareMergeInput", BindingFlags.NonPublic | BindingFlags.Static)!;
var provider = new TelemetrySnapshot { Source = "REPLAY_PROVIDER", Success = true };
TelemetrySnapshot? previous = null;
long frames = 0, oldCount = 0, newCount = 0, badPosition = 0, staleRetained = 0;
var examples = new List<object>();
foreach (var line in File.ReadLines(source))
{
    using var doc = JsonDocument.Parse(line);
    var r = doc.RootElement;
    if (!r.TryGetProperty("RemoteEntities", out var entities)) continue;
    var list = entities.Deserialize<VerifiedRemoteEntityTelemetry[]>(options) ?? [];
    var at = r.GetProperty("ReceivedAt").GetDateTimeOffset();
    var frame = new RemotePlayerTelemetryFrame(r.GetProperty("Sequence").GetInt64(),
        r.GetProperty("ObservedAt").GetDateTimeOffset(), r.GetProperty("ServerEndpoint").GetString(),
        r.GetProperty("LocalLocation").Deserialize<WorldLocation>(options)!, 0, list,
        ReceivedAt: at, SessionId: r.GetProperty("SessionId").GetString());
    // Explicit counterfactual: a provider stats snapshot without Pro marker
    // history is present on every frame. This is not replay of provider traffic.
    var baseline = LocalPositionSnapshotMerger.Merge(provider, null, at, remotePlayers: list, verifiedLocalFallback: frame);
    var input = (TelemetrySnapshot?)prepare.Invoke(null, [provider, previous, frame]);
    var patched = LocalPositionSnapshotMerger.Merge(input, null, at, remotePlayers: list, verifiedLocalFallback: frame);
    frames++; oldCount += baseline.Map?.Markers.Count ?? 0; newCount += patched.Map?.Markers.Count ?? 0;
    var oldKeys = (baseline.Map?.Markers ?? []).Select(m => m.SteamId).ToHashSet();
    foreach (var marker in patched.Map?.Markers ?? [])
    {
        if (oldKeys.Contains(marker.SteamId)) continue;
        var entity = list.First(e => marker.SteamId == $"pro-entity:{e.Kind.ToString().ToLowerInvariant()}:{e.TrackId}");
        if (!marker.ProEntityIsStale || marker.Location != entity.Location) badPosition++;
        else staleRetained++;
        if (examples.Count < 5) examples.Add(new { frame.Sequence, at, entity.TrackId, entity.LocationObservedAt, entity.ObservedAt, marker.ProEntityIsStale });
    }
    previous = patched;
}
using var raw = File.OpenRead(source);
var result = new { Scope = "Frozen IPC replay with simulated provider snapshot; not independent game ground truth", Status = staleRetained > 0 && badPosition == 0 ? "PASS" : "NEED_STAGE_EVIDENCE",
    InputSha256 = Convert.ToHexString(SHA256.HashData(raw)), frames, oldCount, newCount, staleRetained, badPosition, examples };
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));
return badPosition == 0 ? 0 : 1;
