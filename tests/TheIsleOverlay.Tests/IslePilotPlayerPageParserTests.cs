using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotPlayerPageParserTests
{
    [Fact]
    public void Parse_ReadsSemanticStatsFromNextServerRenderedHtml()
    {
        const string html = """
            <main>
              <h1 class="species">Pteranodon</h1>
              <span class="badge">Online</span>
              <span>Growth</span><span class="font-medium">33%</span>
              <span>Health</span><span class="font-medium">7 / 9</span>
              <span>Hunger</span><span class="font-medium">1.5 / 3</span>
              <span>Thirst</span><span class="font-medium">670 / 1000</span>
            </main>
            """;

        var result = IslePilotPlayerPageParser.Parse(html);

        Assert.Equal("Pteranodon", result.Species);
        Assert.True(result.Online);
        Assert.Equal(33, result.GrowthPercent);
        Assert.Equal(7, result.Health);
        Assert.Equal(9, result.MaxHealth);
        Assert.Equal(1.5, result.Hunger);
        Assert.Equal(3, result.MaxHunger);
        Assert.Equal(670, result.Thirst);
        Assert.Equal(1000, result.MaxThirst);
    }

    [Fact]
    public void Parse_ToleratesMissingVitals()
    {
        var result = IslePilotPlayerPageParser.Parse("<h1>Gallimimus</h1><span>Offline</span>");

        Assert.Equal("Gallimimus", result.Species);
        Assert.False(result.Online);
        Assert.Null(result.Health);
        Assert.Null(result.MaxHealth);
    }
}
