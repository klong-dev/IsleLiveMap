using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class MapLaunchGatePolicyTests
{
    [Theory]
    [InlineData(UpdatePreparationState.Current)]
    [InlineData(UpdatePreparationState.DevelopmentBuild)]
    [InlineData(UpdatePreparationState.Unavailable)]
    public void CompletedCheck_AllowsMapWhenNoPreparedUpdateRequiresRestart(
        UpdatePreparationState updateState)
    {
        var gate = MapLaunchGatePolicy.FromUpdate(updateState);

        Assert.Equal(MapLaunchGateState.Available, gate);
        Assert.True(MapLaunchGatePolicy.AllowsMap(gate));
    }

    [Fact]
    public void PreparedUpdate_BlocksMapUntilRestart()
    {
        var gate = MapLaunchGatePolicy.FromUpdate(UpdatePreparationState.Ready);

        Assert.Equal(MapLaunchGateState.UpdateRequired, gate);
        Assert.False(MapLaunchGatePolicy.AllowsMap(gate));
        Assert.False(MapLaunchGatePolicy.AllowsMap(MapLaunchGateState.Checking));
    }

    [Fact]
    public void Home_StartsEveryMapControlDisabledWithAnExplanation()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        Assert.Equal("False", (string?)Control("SteamLoginButton").Attribute("IsEnabled"));
        Assert.Equal("False", (string?)Control("EraSourceButton").Attribute("IsEnabled"));
        Assert.Equal("False", (string?)Control("PandoraSourceButton").Attribute("IsEnabled"));
        Assert.Contains(
            "tự mở khóa",
            (string?)Control("MapLaunchStateDetail").Attribute("Text"),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                "DirectMapButton",
                StringComparison.Ordinal));
    }
}
