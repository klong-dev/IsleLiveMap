using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class LocalVitalsSessionCacheTests
{
    [Fact]
    public void TryRestoreLatest_RecoversFullSnapshotBeforeServerRepeatsHeartbeat()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"isle-vitals-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "cache.json");
        try
        {
            var store = new LocalVitalsSessionCache(path);
            store.Enrich("game", "10.0.0.1:7777", Observation(19_504, new ExactVitals
            {
                Growth = 0.169,
                Health = 7.5,
                MaxHealth = 7.5,
                Stamina = 241,
                MaxStamina = 241,
                Hunger = 0.1,
                MaxHunger = 2.5,
                Thirst = 870,
                MaxThirst = 1_000
            }));

            var reloaded = new LocalVitalsSessionCache(path);
            Assert.True(reloaded.TryRestoreLatest(
                "game",
                "10.0.0.1:7777",
                DateTimeOffset.UtcNow,
                out var restored));

            Assert.Equal(19_504UL, restored.NetRefHandle);
            Assert.Equal(7.5, restored.Vitals.Health);
            Assert.Equal(241, restored.Vitals.Stamina);
            Assert.Equal(0.1, restored.Vitals.Hunger);
            Assert.Equal(870, restored.Vitals.Thirst);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Enrich_RefreshesCurrentEvidenceWhenMaximumsDoNotChange()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"isle-vitals-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "cache.json");
        try
        {
            var store = new LocalVitalsSessionCache(path);
            var maximums = new ExactVitals
            {
                Health = 100,
                MaxHealth = 100,
                Stamina = 200,
                MaxStamina = 200,
                Hunger = 40,
                MaxHunger = 50,
                Thirst = 900,
                MaxThirst = 1_000
            };
            store.Enrich("game", "10.0.0.1:7777", Observation(42, maximums));
            store.Enrich("game", "10.0.0.1:7777", Observation(42, maximums with
            {
                Health = 75,
                Stamina = 120
            }));

            var enriched = store.Enrich(
                "game",
                "10.0.0.1:7777",
                Observation(42, new ExactVitals
                {
                    Hunger = 39,
                    Thirst = 899,
                    MaxThirst = 1_000
                }));

            Assert.Equal(75, enriched.Vitals.Health);
            Assert.Equal(120, enriched.Vitals.Stamina);
            Assert.Equal(100, enriched.Vitals.MaxHealth);
            Assert.Equal(200, enriched.Vitals.MaxStamina);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Enrich_RestoresVerifiedVitalsOnlyForSameGameServerAndActor()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"isle-vitals-cache-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "cache.json");
        try
        {
            var store = new LocalVitalsSessionCache(path);
            var full = Observation(54_816, new ExactVitals
            {
                Growth = 0.71,
                Health = 62.79,
                MaxHealth = 62.79,
                Stamina = 777.4,
                MaxStamina = 777.4,
                Hunger = 11.4,
                MaxHunger = 20.72,
                Thirst = 565,
                MaxThirst = 1_000
            });
            store.Enrich("84168:123", "10.0.0.1:7777", full);

            var reloaded = new LocalVitalsSessionCache(path);
            var partial = Observation(54_816, new ExactVitals
            {
                Health = 0,
                Stamina = 83.2,
                Hunger = 11.3,
                Thirst = 560,
                MaxThirst = 1_000
            });
            var enriched = reloaded.Enrich(
                "84168:123",
                "10.0.0.1:7777",
                partial);

            Assert.Equal(0.71, enriched.Vitals.Growth);
            Assert.Equal(62.79, enriched.Vitals.Health);
            Assert.Equal(62.79, enriched.Vitals.MaxHealth);
            Assert.Equal(777.4, enriched.Vitals.Stamina);
            Assert.Equal(777.4, enriched.Vitals.MaxStamina);
            Assert.Equal(11.3, enriched.Vitals.Hunger);
            Assert.Equal(20.72, enriched.Vitals.MaxHunger);
            Assert.Equal(560, enriched.Vitals.Thirst);

            var otherActor = reloaded.Enrich(
                "84168:123",
                "10.0.0.1:7777",
                partial with { NetRefHandle = 54_817 });
            Assert.Null(otherActor.Vitals.MaxHealth);

            var otherGame = reloaded.Enrich(
                "84168:456",
                "10.0.0.1:7777",
                partial);
            Assert.Null(otherGame.Vitals.MaxHealth);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static LocalDinosaurVitalsObservation Observation(
        ulong handle,
        ExactVitals vitals) => new(
        DateTimeOffset.UtcNow,
        vitals,
        handle);
}
