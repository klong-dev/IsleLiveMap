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
    public void Modal_SummarizesVersion131NpcapSetupHotfix()
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

        Assert.Equal("1.3.1", ReleaseHighlightsWindow.ReleaseVersion);
        Assert.Contains("CÀI NPCAP MỘT CHẠM", allCopy, StringComparison.Ordinal);
        Assert.Contains("NGUỒN CHÍNH THỨC", allCopy, StringComparison.Ordinal);
        Assert.Contains("KIỂM TRA TRƯỚC KHI CHẠY", allCopy, StringComparison.Ordinal);
        Assert.Contains("CÀI XONG TỰ TIẾP TỤC", allCopy, StringComparison.Ordinal);
        Assert.Equal("ĐÃ XEM · BẮT ĐẦU  →", (string?)Control("EnterToolButton").Attribute("Content"));
    }
}
