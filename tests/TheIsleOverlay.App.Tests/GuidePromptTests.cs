using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class GuidePromptTests
{
    [Fact]
    public void Guide_PutsAllGlobalShortcutsBeforeTheFeatureList()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "GuideWindow.xaml"));
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

        Assert.Contains("CTRL + SHIFT + O", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + M", allCopy, StringComparison.Ordinal);
        Assert.Contains("NHỚ 7 PHÍM TẮT NÀY", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + KÉO CHUỘT TRÁI", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + CHUỘT PHẢI", allCopy, StringComparison.Ordinal);
        Assert.Contains("PRO · ĐẶT SET POINT", allCopy, StringComparison.Ordinal);
        Assert.Contains("FREE · ĐẶT LẠI MAP", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + NÚT GIỮA", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + N", allCopy, StringComparison.Ordinal);
        Assert.Contains("ALT + P", allCopy, StringComparison.Ordinal);
        Assert.Contains("ẨN / HIỆN TOÀN BỘ HUD", allCopy, StringComparison.Ordinal);
        Assert.Contains("NHIỆM VỤ PRIME TIẾNG VIỆT", allCopy, StringComparison.Ordinal);
        Assert.Contains("NHÓM TẠM + VỊ TRÍ ĐỒNG ĐỘI", allCopy, StringComparison.Ordinal);
        Assert.Equal("ĐÃ HIỂU · VÀO TOOL  →", (string?)Control("EnterToolButton").Attribute("Content"));
        Assert.NotNull(Control("GuideContent"));
    }
}
