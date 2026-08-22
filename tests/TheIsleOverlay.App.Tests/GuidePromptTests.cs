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
        Assert.Contains("ALT + LĂN CHUỘT", allCopy, StringComparison.Ordinal);
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
