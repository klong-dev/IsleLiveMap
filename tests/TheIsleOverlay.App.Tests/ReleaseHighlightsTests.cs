using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class ReleaseHighlightsTests
{
    [Fact]
    public void Home_ShowsReleaseWizardUnlessTheCurrentVersionWasHidden()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.xaml.cs"));

        Assert.Contains(
            "highlightsStore.ShouldShow(ReleaseHighlightsWindow.ReleaseVersion)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "proPresentation.HasCurrentProAccess",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new ReleaseHighlightsWindow(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Modal_IsAFiveStep147And148BriefingWithFinalOptOut()
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

        Assert.Equal("1.4.9", ReleaseHighlightsWindow.ReleaseVersion);
        Assert.Equal(5, ReleaseHighlightsWindow.PageCount);
        Assert.Contains("PRO · v1.4.7", allCopy, StringComparison.Ordinal);
        Assert.Contains("MAP BẮT ĐẦU NGHE TRƯỚC KHI BẠN MỞ", allCopy, StringComparison.Ordinal);
        Assert.Contains("ONGOING ACTOR", allCopy, StringComparison.Ordinal);
        Assert.Contains("SPARSE BATCH", allCopy, StringComparison.Ordinal);
        Assert.Contains("DESTROY EVENT", allCopy, StringComparison.Ordinal);
        Assert.Contains("FREE · v1.4.8", allCopy, StringComparison.Ordinal);
        Assert.Contains("MAP · TRÒN", allCopy, StringComparison.Ordinal);
        Assert.Contains("CTRL + SHIFT + O", allCopy, StringComparison.Ordinal);
        Assert.Contains("4 BLOCK. 4 KÍCH THƯỚC RIÊNG", allCopy, StringComparison.Ordinal);
        Assert.Contains("Không hiển thị lại thông báo này cho phiên bản 1.4.9", allCopy, StringComparison.Ordinal);
        Assert.Equal(
            "HOÀN TẤT",
            (string?)Control("FinishButton").Attribute("Content"));
        for (var step = 1; step <= ReleaseHighlightsWindow.PageCount; step++)
        {
            Assert.NotNull(Control($"Page{NumberWord(step)}"));
            Assert.NotNull(Control($"Step{NumberWord(step)}Marker"));
        }

        var optOut = Control("DoNotShowAgainCheckBox");
        Assert.Contains(
            optOut.Ancestors(),
            ancestor => string.Equals(
                (string?)ancestor.Attribute(nameAttribute),
                "PageFive",
                StringComparison.Ordinal));
        Assert.Equal("https://isle.klong.dev/", ReleaseHighlightsWindow.ProLandingPageUri.AbsoluteUri);

        static string NumberWord(int value) => value switch
        {
            1 => "One",
            2 => "Two",
            3 => "Three",
            4 => "Four",
            5 => "Five",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }
}
