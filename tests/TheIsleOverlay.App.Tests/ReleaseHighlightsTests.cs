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
            "highlightsStore.ShouldShow(ReleaseHighlightsWindow.BriefingKey)",
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
        Assert.Contains(
            "ShowProPromotionIfNeeded();",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiringProSession_ReturnsToFreeAndShowsPromotionAgain()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "HomeWindow.Pro.cs"));
        var expiryHandler = source[source.IndexOf(
            "private async void ProExpiryTimer_Tick",
            StringComparison.Ordinal)..];

        Assert.Contains("ApplyProAccessState(_proAccess);", expiryHandler, StringComparison.Ordinal);
        Assert.Contains("ShowProPromotionIfNeeded();", expiryHandler, StringComparison.Ordinal);
        Assert.True(
            expiryHandler.IndexOf("ApplyProAccessState(_proAccess);", StringComparison.Ordinal)
            < expiryHandler.IndexOf("ShowProPromotionIfNeeded();", StringComparison.Ordinal));
    }

    [Fact]
    public void Modal_IsAFiveStepOfflineMapTeamBriefingWithCapturedFeatureFrames()
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

        Assert.Equal(
            "offline-map-layers-team-recovery-2026-09-18",
            ReleaseHighlightsWindow.BriefingKey);
        Assert.Equal(5, ReleaseHighlightsWindow.PageCount);
        Assert.Contains("BẬT ĐÚNG THỨ BẠN CẦN TRÊN MAP", allCopy, StringComparison.Ordinal);
        Assert.Contains("430 Animal", allCopy, StringComparison.Ordinal);
        Assert.Contains("245 Plant/Fungi", allCopy, StringComparison.Ordinal);
        Assert.Contains("278 Earth", allCopy, StringComparison.Ordinal);
        Assert.Contains("MỐC ỔN ĐỊNH, XÓA ĐƯỢC NGAY", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + M", allCopy, StringComparison.Ordinal);
        Assert.Contains("Delete", allCopy, StringComparison.Ordinal);
        Assert.Contains("ĐỒNG ĐỘI KHÔNG CÒN BIẾN MẤT", allCopy, StringComparison.Ordinal);
        Assert.Contains("10 GIÂY", allCopy, StringComparison.Ordinal);
        Assert.Contains("15 GIÂY", allCopy, StringComparison.Ordinal);
        Assert.Contains("35 GIÂY", allCopy, StringComparison.Ordinal);
        Assert.Contains("MINIMAP NHẸ MẮT", allCopy, StringComparison.Ordinal);
        Assert.Contains("CTRL + SHIFT + O", allCopy, StringComparison.Ordinal);
        Assert.Contains("Không hiển thị lại thông báo cập nhật này", allCopy, StringComparison.Ordinal);
        Assert.Equal(
            "HOÀN TẤT",
            (string?)Control("FinishButton").Attribute("Content"));
        var pages = new[] { "PageLayers", "PageNotes", "PageTeam", "PageHud", "PageSummary" };
        var markers = new[] { "StepLayersMarker", "StepNotesMarker", "StepTeamMarker", "StepHudMarker", "StepSummaryMarker" };
        for (var step = 0; step < ReleaseHighlightsWindow.PageCount; step++)
        {
            Assert.NotNull(Control(pages[step]));
            Assert.NotNull(Control(markers[step]));
        }

        var optOut = Control("DoNotShowAgainCheckBox");
        Assert.Contains(
            optOut.Ancestors(),
            ancestor => string.Equals(
                (string?)ancestor.Attribute(nameAttribute),
                "PageSummary",
                StringComparison.Ordinal));
        var sources = document.Descendants()
            .Where(element => element.Name.LocalName == "Image")
            .Select(element => (string?)element.Attribute("Source"))
            .Where(source => source?.StartsWith("Assets/ReleaseHighlights/", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(4, sources.Length);
        Assert.Contains("Assets/ReleaseHighlights/MapLayerInspector.png", sources);
        Assert.Contains("Assets/ReleaseHighlights/MapNotesAltM.png", sources);
        Assert.Contains("Assets/ReleaseHighlights/SurvivalTeamPanel.png", sources);
        Assert.Contains("Assets/ReleaseHighlights/CompactTrackingMap.png", sources);
        Assert.Equal("https://isle.klong.dev/", ReleaseHighlightsWindow.ProLandingPageUri.AbsoluteUri);

    }
}
