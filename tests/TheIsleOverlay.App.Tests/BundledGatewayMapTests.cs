using System.IO;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class BundledGatewayMapTests
{
    private const string ResourceKey = "assets/gatewaymap.webp";
    private const string ExpectedSha256 = "BA2E5E614995BEC84559B950F1AE978C2F9A66743F0DA47A348278DB01557EF3";

    [Fact]
    public void GatewayMap_IsEmbeddedUnchanged()
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
            Assert.Equal(6_739_366, mapStream.Length);
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
        Assert.Equal("Assets/GatewayMap.webp", resourceUri.OriginalString);
    }
}
