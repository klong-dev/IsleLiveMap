using System.IO;
using System.Resources;
using System.Security.Cryptography;
using System.Xml.Linq;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class DonatePromptTests
{
    private const string ResourceKey = "assets/cute_mouse.jpg";
    private const string ExpectedSha256 = "4306AAF2D8F99F3B2A234B49576ABA4B1FEA7F6E4D9B16A3E5C2BC4CA98DD290";

    [Fact]
    public void DonatePrompt_ContainsVerifiedPaymentDetailsAndClearExit()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "DonateWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        Assert.Equal(
            "/IsleLiveMap;component/Assets/cute_mouse.jpg",
            (string?)Control("DonateImage").Attribute("Source"));
        Assert.Equal("ĐÓNG SAU 7S  →", (string?)Control("EnterToolButton").Attribute("Content"));
        Assert.Equal("False", (string?)Control("EnterToolButton").Attribute("IsEnabled"));
        Assert.Equal("False", (string?)Control("TopCloseButton").Attribute("IsEnabled"));
        Assert.Equal(7, DonateWindow.CloseDelaySeconds);
        Assert.NotNull(Control("TopCloseButton"));
        Assert.NotNull(Control("CopyAccountButton"));

        var allCopy = string.Join(
            " ",
            document.Descendants().SelectMany(element => new[]
            {
                (string?)element.Attribute("Text"),
                (string?)element.Attribute("Content")
            }));
        Assert.Contains("VIETCOMBANK", allCopy, StringComparison.Ordinal);
        Assert.Contains("1029 118 580", allCopy, StringComparison.Ordinal);
        Assert.Contains("HOANG KIM LONG", allCopy, StringComparison.Ordinal);
        Assert.Contains("KHÔNG BẮT BUỘC", allCopy, StringComparison.Ordinal);
        Assert.Contains("NUÔI LIVE MAP", allCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NUÔI CON CHUỘT NÀY", allCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ủng hộ dev đau lưng", allCopy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DonateImage_IsEmbeddedUnchanged()
    {
        var assembly = typeof(DonateWindow).Assembly;
        var manifestName = Assert.Single(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith(".g.resources", StringComparison.Ordinal));

        using var manifestStream = assembly.GetManifestResourceStream(manifestName);
        Assert.NotNull(manifestStream);
        using var reader = new ResourceReader(manifestStream);
        var resources = reader.GetEnumerator();

        while (resources.MoveNext())
        {
            if (!string.Equals(resources.Key as string, ResourceKey, StringComparison.Ordinal))
            {
                continue;
            }

            using var imageStream = Assert.IsAssignableFrom<Stream>(resources.Value);
            Assert.Equal(56_745, imageStream.Length);
            Assert.Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(imageStream)));
            return;
        }

        Assert.Fail($"Bundled donate image resource '{ResourceKey}' was not found.");
    }
}
