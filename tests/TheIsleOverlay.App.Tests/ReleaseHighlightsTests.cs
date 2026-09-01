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
    public void Modal_IsAFourStepFreeAndProBriefingWithFinalOptOut()
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

        Assert.Equal("1.4.3", ReleaseHighlightsWindow.ReleaseVersion);
        Assert.Equal(4, ReleaseHighlightsWindow.PageCount);
        Assert.Contains("FREE UPDATE", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + KÉO GIỮ CHUỘT TRÁI", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + CHUỘT PHẢI", allCopy, StringComparison.Ordinal);
        Assert.Contains("CTRL + SHIFT + O", allCopy, StringComparison.Ordinal);
        Assert.Contains("PRO · ALT + M", allCopy, StringComparison.Ordinal);
        Assert.Contains("CHIA SẺ CHO NHÓM", allCopy, StringComparison.Ordinal);
        Assert.Contains("PRO · MAP ZONES", allCopy, StringComparison.Ordinal);
        Assert.Contains("PLAYER ĐƯỢC DỰNG NHANH VÀ ĐỦ HƠN", allCopy, StringComparison.Ordinal);
        Assert.Equal(
            "HOÀN TẤT",
            (string?)Control("FinishButton").Attribute("Content"));
        Assert.NotNull(Control("DoNotShowAgainCheckBox"));
        Assert.Equal("https://isle.klong.dev/", ReleaseHighlightsWindow.ProLandingPageUri.AbsoluteUri);

        var setPointPreview = Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute("Source"),
                ReleaseHighlightsWindow.SetPointPreviewResourcePath,
                StringComparison.Ordinal));
        Assert.Equal("Image", setPointPreview.Name.LocalName);
        var zonePreview = Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute("Source"),
                ReleaseHighlightsWindow.MapZonePreviewResourcePath,
                StringComparison.Ordinal));
        Assert.Equal("Image", zonePreview.Name.LocalName);
    }
}
