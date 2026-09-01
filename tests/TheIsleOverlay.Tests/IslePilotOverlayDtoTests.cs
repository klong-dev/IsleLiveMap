using System.Text.Json;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotOverlayDtoTests
{
    [Fact]
    public void LiveFrame_DeserializesAllRealtimeFields()
    {
        const string json = """
            {
              "t": "live",
              "d": {
                "hasDino": true,
                "steamId": "76561198000000000",
                "growth": 0.359,
                "health": 8.2,
                "maxHealth": 9.9,
                "hunger": 0,
                "maxHunger": 3.3,
                "thirst": 530,
                "maxThirst": 1000,
                "stamina": 295,
                "maxStamina": 295,
                "nutrition": { "carb": 1, "protein": 2, "lipid": 3 },
                "position": { "x": 77761.41, "y": -235882.81, "z": 1200, "yaw": 157.37 }
              }
            }
            """;

        var frame = JsonSerializer.Deserialize<IslePilotOverlayFrame>(json, IslePilotOverlayJson.Options);

        Assert.Equal("live", frame?.Type);
        Assert.True(frame?.Data?.HasDino);
        Assert.Equal("76561198000000000", frame?.Data?.SteamId);
        Assert.Equal(0.359, frame?.Data?.Growth);
        Assert.Equal(8.2, frame?.Data?.Health);
        Assert.Equal(3, frame?.Data?.Nutrition?.Lipid);
        Assert.Equal(77761.41, frame?.Data?.Position?.X);
        Assert.Equal(1200, frame?.Data?.Position?.Z);
        Assert.Equal(157.37, frame?.Data?.Position?.Yaw);
    }

    [Fact]
    public void Map_DeserializesExplicitHeatCellsAndRadius()
    {
        const string json = """
            {
              "heatmapEnabled": true,
              "heat": [{ "u": 0.2, "v": 0.7, "intensity": 0.6 }],
              "heatRadius": 30
            }
            """;

        var map = JsonSerializer.Deserialize<IslePilotOverlayMapDto>(
            json,
            IslePilotOverlayJson.Options);

        Assert.True(map?.HeatmapEnabled);
        Assert.Equal(30, map?.HeatRadius);
        var cell = Assert.Single(map?.Heat ?? []);
        Assert.Equal(0.2, cell.U);
        Assert.Equal(0.7, cell.V);
        Assert.Equal(0.6, cell.Intensity);
    }
}
