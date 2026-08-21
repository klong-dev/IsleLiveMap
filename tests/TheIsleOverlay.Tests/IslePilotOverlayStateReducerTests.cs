using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayStateReducerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PartialLiveFrames_DoNotEraseBaselineOrPreviousLiveValues()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8,
            Nutrition = new IslePilotNutritionDto { Protein = 4 }
        }, Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Thirst = 500,
            Nutrition = new IslePilotNutritionDto { Lipid = 5 }
        }, Now.AddSeconds(1));

        var player = reducer.BuildSnapshot(Now.AddSeconds(1)).Player;

        Assert.Equal(8, player?.ExactVitals?.Health);
        Assert.Equal(20, player?.ExactVitals?.MaxHealth);
        Assert.Equal(7, player?.ExactVitals?.Hunger);
        Assert.Equal(2, player?.Nutrition?.Carb);
        Assert.Equal(4, player?.Nutrition?.Protein);
        Assert.Equal(5, player?.Nutrition?.Lipid);
    }

    [Fact]
    public void HasDinoFalse_HidesPlayerButKeepsMapState()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto { HasDino = false }, Now);

        var snapshot = reducer.BuildSnapshot(Now);

        Assert.False(snapshot.PlayerOnline);
        Assert.Null(snapshot.Player);
        Assert.NotNull(snapshot.Map);
    }

    [Fact]
    public void LiveDataOlderThanFourSeconds_IsStaleAndUsesMapMarkerFallback()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(10, 20), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Health = 8,
            Position = new IslePilotOverlayPositionDto { X = 80, Y = 90, Yaw = 0 }
        }, Now);

        var snapshot = reducer.BuildSnapshot(Now.AddSeconds(5));

        Assert.Equal(TelemetrySessionState.Stale, snapshot.SessionState);
        Assert.True(snapshot.LiveDataStale);
        Assert.Equal(8, snapshot.Player?.ExactVitals?.Health);
        Assert.Equal(10, snapshot.Player?.Location?.X);
        Assert.Equal(20, snapshot.Player?.Location?.Y);
    }

    [Fact]
    public void MeRefresh_UpdatesIdentityServerAndPrimeState()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMe(Baseline() with
        {
            Species = "Tyrannosaurus",
            Server = "Second IslePilot Server",
            Prime = new IslePilotPrimeDto
            {
                Elder = true,
                Eligible = true,
                Done = 3,
                Required = 3,
                Quests = [new IslePilotPrimeQuestDto { Name = "Survive", Done = true }]
            }
        }, Now.AddSeconds(10));

        var player = reducer.BuildSnapshot(Now.AddSeconds(10)).Player;

        Assert.Equal("Tyrannosaurus", player?.Class);
        Assert.Equal("Second IslePilot Server", player?.Server);
        Assert.True(player?.Prime?.Elder);
        Assert.True(player?.Prime?.Eligible);
        Assert.Equal(3, player?.Prime?.Done);
        Assert.Equal(3, player?.Prime?.Required);
        var quest = Assert.Single(player?.Prime?.Quests ?? []);
        Assert.Equal("Survive", quest.Name);
        Assert.True(quest.Done);
    }

    [Fact]
    public void ApplyLive_UpdatesHeadingWhileTheDinosaurIsStationary()
    {
        var reducer = new IslePilotOverlayStateReducer();
        reducer.ApplyMe(Baseline(), Now);
        reducer.ApplyMap(MapWithSelfMarker(50, 50), Now);
        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 50, Y = 50, Yaw = 0 }
        }, Now);
        var firstHeading = reducer.BuildSnapshot(Now).Player?.ExactMapHeadingDegrees;

        reducer.ApplyLive(new IslePilotOverlayLiveDataDto
        {
            HasDino = true,
            Position = new IslePilotOverlayPositionDto { X = 50, Y = 50, Yaw = 90 }
        }, Now.AddMilliseconds(50));
        var secondHeading = reducer.BuildSnapshot(Now.AddMilliseconds(50))
            .Player?.ExactMapHeadingDegrees;

        Assert.NotNull(firstHeading);
        Assert.NotNull(secondHeading);
        Assert.NotEqual(firstHeading, secondHeading);
    }

    private static IslePilotOverlayMeDto Baseline() => new()
    {
        HasData = true,
        Online = true,
        SteamId = "76561198000000000",
        PersonaName = "Player",
        Species = "Utahraptor",
        Server = "IslePilot Server",
        Female = true,
        Growth = 0.5,
        Health = 10,
        MaxHealth = 20,
        Hunger = 7,
        MaxHunger = 10,
        Thirst = 8,
        MaxThirst = 10,
        Stamina = 9,
        MaxStamina = 10,
        Nutrition = new IslePilotNutritionDto { Carb = 2, Protein = 3, Lipid = 4 }
    };

    private static IslePilotOverlayMapDto MapWithSelfMarker(double x, double y) => new()
    {
        Calibration = new IslePilotMapCalibrationDto
        {
            A = new IslePilotMapCalibrationPointDto { WorldX = 0, WorldY = 0, U = 0, V = 0 },
            B = new IslePilotMapCalibrationPointDto { WorldX = 100, WorldY = 100, U = 1, V = 1 }
        },
        Markers =
        [
            new IslePilotOverlayMapMarkerDto
            {
                SteamId = "76561198000000000",
                Label = "You",
                X = x,
                Y = y,
                Yaw = 90,
                Self = true
            }
        ]
    };
}
