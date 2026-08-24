using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class ReleaseHighlightsTests
{
    [Fact]
    public void Store_ShowsEachNewerVersionOnlyOnce()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "last-release-highlights.txt");
        try
        {
            var store = new ReleaseHighlightsStore(path);
            Assert.True(store.ShouldShow("1.2.0"));

            store.MarkShown("1.2.0");

            Assert.False(store.ShouldShow("1.2.0"));
            Assert.False(store.ShouldShow("1.1.3"));
            Assert.True(store.ShouldShow("1.2.1"));
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
    public void Modal_SummarizesVersion130DirectTelemetryUpdate()
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

        Assert.Equal("1.3.0", ReleaseHighlightsWindow.ReleaseVersion);
        Assert.Contains("GPS TRỰC TIẾP TỪ GAME", allCopy, StringComparison.Ordinal);
        Assert.Contains("STATUS DINO GIỮ NGUYÊN", allCopy, StringComparison.Ordinal);
        Assert.Contains("BAY KHÔNG NHẢY MARKER", allCopy, StringComparison.Ordinal);
        Assert.Contains("UPDATE XONG MỚI MỞ MAP", allCopy, StringComparison.Ordinal);
        Assert.Equal("ĐÃ XEM · BẮT ĐẦU  →", (string?)Control("EnterToolButton").Attribute("Content"));
    }
}
