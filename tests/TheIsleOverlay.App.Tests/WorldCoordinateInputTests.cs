using System.IO;
using System.Xml.Linq;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class WorldCoordinateInputTests
{
    [Theory]
    [InlineData("-238,743.261, 88,587.6,  28,509.171")]
    [InlineData("-238743.261 88587.6 28509.171")]
    [InlineData("X=-238,743.261; Y=88,587.6; Z=28,509.171")]
    public void TryParse_AcceptsCopiedAndPlainXyzFormats(string input)
    {
        Assert.True(WorldCoordinateInput.TryParse(input, out var location));
        Assert.Equal(-238743.261d, location.X, precision: 3);
        Assert.Equal(88587.6d, location.Y, precision: 3);
        Assert.Equal(28509.171d, location.Z!.Value, precision: 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-238743 88587")]
    [InlineData("-238743 88587 28509 123")]
    [InlineData("tọa độ -238743 88587 28509")]
    public void TryParse_RejectsIncompleteOrUnexpectedInput(string? input) =>
        Assert.False(WorldCoordinateInput.TryParse(input, out _));

    [Fact]
    public void Projection_ProducesTheSameWorldPointUsedByClickPlacement()
    {
        Assert.True(WorldCoordinateInput.TryParse(
            "-238,743.261, 88,587.6, 28,509.171",
            out var location));
        Assert.True(WorldCoordinateInput.TryProjectToGateway(location, out var point));

        var clickPipelineWorld = GatewayMapProjection.Unproject(point);
        Assert.Equal(location.X, clickPipelineWorld.X, precision: 6);
        Assert.Equal(location.Y, clickPipelineWorld.Y, precision: 6);
    }

    [Fact]
    public void Projection_RejectsCoordinatesOutsideGateway()
    {
        var outside = new WorldLocation { X = 2_000_000, Y = 2_000_000, Z = 10_000 };

        Assert.False(WorldCoordinateInput.TryProjectToGateway(outside, out _));
    }

    [Fact]
    public void TacticalMap_ExposesKeyboardAccessibleCoordinateEntry()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "MapNotesWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        var input = Control("CoordinateInputTextBox");
        var submit = Control("CoordinateSubmitButton");
        Assert.Equal("CoordinateInputTextBox_KeyDown", (string?)input.Attribute("KeyDown"));
        Assert.Equal("CoordinateSubmitButton_Click", (string?)submit.Attribute("Click"));
        Assert.Equal("ĐẶT MỐC", (string?)submit.Attribute("Content"));
        Assert.NotNull(Control("CoordinateFeedbackLabel"));
    }
}
