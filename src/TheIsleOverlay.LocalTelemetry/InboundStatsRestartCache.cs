using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>Short restart bridge. No endpoint-only restore, old currents, or timestamp renewal.</summary>
public sealed class InboundStatsRestartCache
{
    public static readonly TimeSpan RestartWindow = TimeSpan.FromSeconds(60);
    private readonly string _directory;
    private DateTimeOffset _lastSave;
    public InboundStatsRestartCache(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IsleLiveMap", "inbound-stats-v1");

    public IReadOnlyList<VitalsFieldEvidence> Restore(string scope, ulong owner, DateTimeOffset now, IReadOnlyList<VitalsFieldEvidence> fresh)
    {
        if (!HasGameGeneration(scope)) return [];
        try
        {
            var path = CachePath(scope);
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return [];
            var snapshot = JsonSerializer.Deserialize<RestartSnapshot>(File.ReadAllText(path));
            return snapshot is null ? [] : SelectRestorable(snapshot, scope, owner, now, fresh);
        }
        catch { return []; }
    }

    public void Save(string scope, ulong owner, DateTimeOffset now, IReadOnlyList<VitalsFieldEvidence> evidence)
    {
        if (!HasGameGeneration(scope) || now - _lastSave < TimeSpan.FromSeconds(5) || owner == 0 || evidence.Count == 0) return;
        try
        {
            Directory.CreateDirectory(_directory);
            var target = CachePath(scope);
            var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new RestartSnapshot(1, scope, owner, now, evidence.ToArray())));
            File.Move(temp, target, overwrite: true);
            _lastSave = now;
        }
        catch { /* Optional cache failure must not stop capture. */ }
    }

    internal static IReadOnlyList<VitalsFieldEvidence> SelectRestorable(RestartSnapshot snapshot, string scope, ulong owner,
        DateTimeOffset now, IReadOnlyList<VitalsFieldEvidence> fresh)
    {
        if (snapshot.Version != 1 || snapshot.Scope != scope || snapshot.Owner != owner || owner == 0
            || snapshot.SavedAt > now || now - snapshot.SavedAt > RestartWindow) return [];
        // Need two newly decoded survival fields for the same owner, close to the
        // last received state. A raw presence batch alone is insufficient.
        foreach (var field in new[] { "HungerCandidate", "ThirstCandidate" })
        {
            var incoming = fresh.FirstOrDefault(e => e.Field == field && e.Owner == owner && e.Flow == scope);
            var previous = snapshot.Fields.FirstOrDefault(e => e.Field == field && e.Owner == owner && e.Flow == scope);
            if (incoming is null || previous is null || !double.IsFinite(incoming.Value) || !double.IsFinite(previous.Value)
                || incoming.ObservedAt > now || now - incoming.ObservedAt > TimeSpan.FromSeconds(3)
                || previous.ObservedAt > now || now - previous.ObservedAt > RestartWindow
                || Math.Abs(incoming.Value - previous.Value) > Math.Max(2d, previous.Value * .02d)) return [];
        }
        return snapshot.Fields.Where(e => e.Owner == owner && e.Flow == scope && e.ObservedAt <= now
            && now - e.ObservedAt <= TimeSpan.FromMinutes(5) && double.IsFinite(e.Value) && e.Value > 0
            && e.Field is "MaxHealthCandidate" or "MaxStaminaCandidate" or "MaxHungerCandidate"
            && e.Layout is "measured-1391-1491" or "measured-1482"
            && !fresh.Any(n => n.Field == e.Field)).ToArray();
    }

    private string CachePath(string scope) => Path.Combine(_directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope))) + ".json");
    private static bool HasGameGeneration(string scope)
    {
        var parts = scope.Split('|');
        if (parts.Length != 3) return false;
        var game = parts[0].Split(':');
        return game.Length == 2 && int.TryParse(game[0], out var pid) && pid > 0
            && long.TryParse(game[1], out var started) && started > 0;
    }
    internal sealed record RestartSnapshot(int Version, string Scope, ulong Owner, DateTimeOffset SavedAt, VitalsFieldEvidence[] Fields);
}
