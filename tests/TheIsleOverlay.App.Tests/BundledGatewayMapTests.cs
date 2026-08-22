using System.IO;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class BundledGatewayMapTests
{
    private const string ResourceKey = "assets/gatewaymap.jpg";
    private const string ExpectedSha256 = "D773E50DDD5FD691D4F751454F972EB49E70243E3326789BE9D6E32913481BB7";

    [Fact]
    public void GatewayMap_IsEmbeddedAsNativeWpfJpeg()
    {
        var assembly = typeof(MainWindow).Assembly;
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

            using var mapStream = Assert.IsAssignableFrom<Stream>(resources.Value);
            Assert.Equal(13_787_683, mapStream.Length);
            Assert.Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(mapStream)));
            return;
        }

        Assert.Fail($"Bundled map resource '{ResourceKey}' was not found.");
    }

    [Fact]
    public void Overlay_UsesLocalMapResourceInsteadOfSourceMapUris()
    {
        Assert.Null(typeof(TelemetrySourceDefinition).GetProperty("MapUri"));

        var resourceUriField = typeof(MainWindow).GetField(
            "GatewayMapResourceUri",
            BindingFlags.NonPublic | BindingFlags.Static);
        var resourceUri = Assert.IsType<Uri>(resourceUriField?.GetValue(null));

        Assert.False(resourceUri.IsAbsoluteUri);
        Assert.Equal("Assets/GatewayMap.jpg", resourceUri.OriginalString);
    }
}
