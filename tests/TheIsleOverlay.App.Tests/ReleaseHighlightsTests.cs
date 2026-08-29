using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class ReleaseHighlightsTests
{
    [Fact]
    public void Home_ShowsProAnnouncementOnEveryStartupWithoutVersionMarker()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml.cs"));

        Assert.Contains(
            "new ReleaseHighlightsWindow(currentVersion)",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldShow(currentVersion)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkShown(currentVersion)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Modal_SummarizesVersion140PlayerTrackingPro()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "ReleaseHighlightsWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        var allCopy = string.Join(
            " ",
            document.Descendants().SelectMany(element => new[]
            {
                (string?)element.Attribute("Text"),
                (string?)element.Attribute("Content")
            }));

        Assert.Equal("1.4.0", ReleaseHighlightsWindow.ReleaseVersion);
        Assert.Contains("ISLE LIVE MAP PRO", allCopy, StringComparison.Ordinal);
        Assert.Contains("PLAYER ĐÃ XÁC MINH", allCopy, StringComparison.Ordinal);
        Assert.Contains("AI TÁCH BIỆT", allCopy, StringComparison.Ordinal);
        Assert.Contains("LOÀI + CÂN NẶNG", allCopy, StringComparison.Ordinal);
        Assert.Contains("MỌI SERVER · MỘT LIVE MAP", allCopy, StringComparison.Ordinal);
        Assert.Equal(
            "XEM PRO TRÊN HOME  →",
            (string?)Control("EnterToolButton").Attribute("Content"));

        var preview = Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute("Source"),
                ReleaseHighlightsWindow.ProPreviewResourcePath,
                StringComparison.Ordinal));
        Assert.Equal("Image", preview.Name.LocalName);
    }
}
