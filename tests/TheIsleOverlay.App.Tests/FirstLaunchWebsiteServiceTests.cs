using System.ComponentModel;
using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class FirstLaunchWebsiteServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "isle-first-launch-" + Guid.NewGuid().ToString("N"));
    private string Marker => Path.Combine(_root, "opened.marker");

    [Fact]
    public void OpensDefaultBrowserOnlyOnceAcrossInstances()
    {
        var calls = 0;
        var service = new FirstLaunchWebsiteService(Marker, info =>
        {
            Assert.Equal(FirstLaunchWebsiteService.WebsiteUri, info.FileName);
            Assert.True(info.UseShellExecute);
        Assert.True(File.Exists(Marker));
            calls++;
        });

        Assert.True(service.TryOpenOnce());
        Assert.False(service.TryOpenOnce());
        Assert.False(new FirstLaunchWebsiteService(Marker, _ => calls++).TryOpenOnce());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ConcurrentInstancesClaimOnlyOnce()
    {
        var calls = 0;
        Parallel.For(0, 32, _ => new FirstLaunchWebsiteService(
            Marker,
            _ => Interlocked.Increment(ref calls)).TryOpenOnce());

        Assert.Equal(1, calls);
    }

    [Fact]
    public void BrowserFailureStillConsumesClaim()
    {
        var calls = 0;
        Assert.False(new FirstLaunchWebsiteService(Marker, _ =>
        {
            calls++;
            throw new Win32Exception("Synthetic browser failure");
        }).TryOpenOnce());
        Assert.False(new FirstLaunchWebsiteService(Marker, _ => calls++).TryOpenOnce());
        Assert.Equal(1, calls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
