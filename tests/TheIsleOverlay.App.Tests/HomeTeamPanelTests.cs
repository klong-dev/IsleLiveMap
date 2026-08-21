using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class HomeTeamPanelTests
{
    [Fact]
    public void Home_OffersCreateJoinAndEphemeralSessionControls()
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

        Assert.Equal("Survivor", (string?)Control("TeamDisplayNameTextBox").Attribute("Text"));
        Assert.Equal("6", (string?)Control("InviteCodeTextBox").Attribute("MaxLength"));
        Assert.NotNull(Control("CreateTeamButton"));
        Assert.NotNull(Control("JoinTeamButton"));
        Assert.NotNull(Control("ActiveInviteCodeLabel"));
        Assert.NotNull(Control("LeaveTeamButton"));

        var allText = string.Join(
            " ",
            document.Descendants().Select(element => (string?)element.Attribute("Text")));
        Assert.Contains("Tắt app là nhóm tự hủy", allText, StringComparison.Ordinal);
    }
}
