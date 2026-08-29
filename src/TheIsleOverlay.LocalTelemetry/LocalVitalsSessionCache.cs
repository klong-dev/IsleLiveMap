using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.LocalTelemetry;

/// <summary>
/// Retains sparse, verified GAS maximums while the same game process, server,
/// and actor remain active. The key prevents values crossing a game restart,
/// server switch, or dinosaur respawn.
/// </summary>
public sealed class LocalVitalsSessionCache
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan MinimumPersistInterval = TimeSpan.FromSeconds(5);
    // Restarting only the overlay does not make the server replicate GAS again.
    // A short endpoint-level fallback bridges that restart without carrying a
    // dinosaur snapshot indefinitely into a later respawn.
    private static readonly TimeSpan LatestSessionFallbackLifetime = TimeSpan.FromMinutes(10);
    private readonly object _gate = new();
    private readonly string _path;
    private Dictionary<string, CacheEntry>? _entries;

    public LocalVitalsSessionCache(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IsleLiveMap",
                "direct-vitals-cache.json")
            : Path.GetFullPath(path);
    }

    public LocalDinosaurVitalsObservation Enrich(
        string gameSessionId,
        string serverEndpoint,
        LocalDinosaurVitalsObservation observation)
    {
        if (string.IsNullOrWhiteSpace(gameSessionId)
            || string.IsNullOrWhiteSpace(serverEndpoint))
        {
            return observation;
        }

        lock (_gate)
        {
            var entries = LoadEntries();
            var now = DateTimeOffset.UtcNow;
            var key = BuildKey(gameSessionId, serverEndpoint, observation.NetRefHandle);
            var incoming = observation.Vitals;
            if (HasVerifiedMaximums(incoming))
            {
                var next = CacheEntry.From(incoming, now);
                var shouldPersist = !entries.TryGetValue(key, out var existing)
                                    || !SameMaximums(existing, next)
                                    || now - existing.UpdatedAt >= MinimumPersistInterval;
                // Keep current health/stamina fresh in memory on every
                // verified reducer update. Disk writes are throttled because
                // some species replicate these attributes every second.
                entries[key] = next;
                if (shouldPersist)
                {
                    SaveEntries(entries, now);
                }
                return observation;
            }

            if (!entries.TryGetValue(key, out var cached)
                || now - cached.UpdatedAt > EntryLifetime
                || incoming.Hunger is { } hunger
                && hunger > cached.MaxHunger * 1.1d)
            {
                return observation;
            }

            var enriched = incoming with
            {
                Growth = incoming.Growth ?? cached.Growth,
                Health = SelectCurrent(
                    incoming.Health,
                    cached.Health,
                    cached.MaxHealth),
                MaxHealth = cached.MaxHealth,
                Stamina = SelectCurrent(
                    incoming.Stamina,
                    cached.Stamina,
                    cached.MaxStamina),
                MaxStamina = cached.MaxStamina,
                MaxHunger = cached.MaxHunger,
                MaxThirst = incoming.MaxThirst ?? 1_000d
            };
            return observation with { Vitals = enriched };
        }
    }

    public bool TryRestoreLatest(
        string gameSessionId,
        string serverEndpoint,
        DateTimeOffset observedAt,
        out LocalDinosaurVitalsObservation observation)
    {
        observation = default;
        if (string.IsNullOrWhiteSpace(gameSessionId)
            || string.IsNullOrWhiteSpace(serverEndpoint))
        {
            return false;
        }

        lock (_gate)
        {
            var prefix = BuildPrefix(gameSessionId, serverEndpoint);
            var now = DateTimeOffset.UtcNow;
            var latest = LoadEntries()
                .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderByDescending(pair => pair.Value.UpdatedAt)
                .FirstOrDefault();
            if (string.IsNullOrEmpty(latest.Key)
                || now - latest.Value.UpdatedAt > LatestSessionFallbackLifetime
                || !ulong.TryParse(latest.Key[prefix.Length..], out var handle))
            {
                return false;
            }

            observation = new LocalDinosaurVitalsObservation(
                observedAt,
                latest.Value.ToVitals(),
                handle);
            return true;
        }
    }

    private Dictionary<string, CacheEntry> LoadEntries()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            _entries = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(
                      File.ReadAllText(_path))
                  ?? []
                : [];
        }
        catch
        {
            _entries = [];
        }

        return _entries;
    }

    private void SaveEntries(Dictionary<string, CacheEntry> entries, DateTimeOffset now)
    {
        foreach (var expired in entries
                     .Where(pair => now - pair.Value.UpdatedAt > EntryLifetime)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            entries.Remove(expired);
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(entries));
        }
        catch
        {
            // Cache failure must never stop packet telemetry.
        }
    }

    private static bool HasVerifiedMaximums(ExactVitals vitals) =>
        vitals.MaxHealth is > 0d
        && vitals.MaxStamina is > 0d
        && vitals.MaxHunger is > 0d
        && vitals.Health is > 0d
        && vitals.Stamina is >= 0d;

    private static bool SameMaximums(CacheEntry left, CacheEntry right) =>
        NearlyEqual(left.Growth, right.Growth)
        && NearlyEqual(left.MaxHealth, right.MaxHealth)
        && NearlyEqual(left.MaxStamina, right.MaxStamina)
        && NearlyEqual(left.MaxHunger, right.MaxHunger);

    private static bool NearlyEqual(double? left, double? right) =>
        left is null && right is null
        || left is { } leftValue
        && right is { } rightValue
        && Math.Abs(leftValue - rightValue)
        <= Math.Max(0.000001d, Math.Abs(rightValue) * 0.000001d);

    private static double? SelectCurrent(
        double? incoming,
        double? cached,
        double maximum)
    {
        if (incoming is not > 0d
            || incoming > maximum * 1.01d
            || cached is { } previous
            && Math.Abs(incoming.Value - previous) > maximum * 0.5d)
        {
            return cached;
        }

        return incoming;
    }

    private static string BuildKey(
        string gameSessionId,
        string serverEndpoint,
        ulong handle) =>
        $"{BuildPrefix(gameSessionId, serverEndpoint)}{handle}";

    private static string BuildPrefix(string gameSessionId, string serverEndpoint) =>
        $"{gameSessionId}|{serverEndpoint.Trim().ToLowerInvariant()}|";

    public sealed record CacheEntry
    {
        public DateTimeOffset UpdatedAt { get; init; }
        public double? Growth { get; init; }
        public double? Health { get; init; }
        public double MaxHealth { get; init; }
        public double? Stamina { get; init; }
        public double MaxStamina { get; init; }
        public double? Hunger { get; init; }
        public double MaxHunger { get; init; }
        public double? Thirst { get; init; }
        public double MaxThirst { get; init; } = 1_000d;

        public static CacheEntry From(ExactVitals vitals, DateTimeOffset now) => new()
        {
            UpdatedAt = now,
            Growth = vitals.Growth,
            Health = vitals.Health,
            MaxHealth = vitals.MaxHealth!.Value,
            Stamina = vitals.Stamina,
            MaxStamina = vitals.MaxStamina!.Value,
            Hunger = vitals.Hunger,
            MaxHunger = vitals.MaxHunger!.Value,
            Thirst = vitals.Thirst,
            MaxThirst = vitals.MaxThirst ?? 1_000d
        };

        public ExactVitals ToVitals() => new()
        {
            Growth = Growth,
            Health = Health,
            MaxHealth = MaxHealth,
            Stamina = Stamina,
            MaxStamina = MaxStamina,
            Hunger = Hunger,
            MaxHunger = MaxHunger,
            Thirst = Thirst,
            MaxThirst = MaxThirst
        };
    }
}
