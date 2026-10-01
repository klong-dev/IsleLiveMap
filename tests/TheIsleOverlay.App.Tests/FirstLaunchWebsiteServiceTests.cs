using System.ComponentModel;
using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class FirstLaunchWebsiteServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "isle-first-launch-" + Guid.NewGuid().ToString("N"));
    private string Marker => Path.Combine(_root, "opened.marker");

    [Fact]
    public void OpensDefaultBrowserOnceAcrossIndependentInstances()
    {
        var calls = 0;
        var service = new FirstLaunchWebsiteService(Marker, info =>
        {
            Assert.Equal("https://islecheat.org/", info.FileName);
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
    public void ConcurrentInstancesDispatchOnlyOnce()
    {
        var calls = 0;
        Parallel.For(0, 32, i => new FirstLaunchWebsiteService(Marker,
            _ => Interlocked.Increment(ref calls)).TryOpenOnce());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void BrowserFailureDoesNotBreakStartupOrRetryLater()
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

    [Fact]
    public void EmptyExistingMarkerIsConsumed()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Marker, "");
        Assert.False(new FirstLaunchWebsiteService(Marker, _ => Assert.Fail("Must not open")).TryOpenOnce());
    }

    [Fact]
    public void UnwritableStateDoesNotOpenBrowser()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "parent-file"), "fixture");
        Assert.False(new FirstLaunchWebsiteService(Path.Combine(_root, "parent-file", "marker"),
            _ => Assert.Fail("Must not open without durable claim")).TryOpenOnce());
    }

    [Fact]
    public void MarkerIsOutsideVersionedInstallAndLoginResetTargets()
    {
        Assert.Equal(Path.Combine(AppPaths.Root, "islecheat-website-opened.marker"), AppPaths.FirstLaunchWebsiteMarker);
        Assert.DoesNotContain(ServerLoginResetService.Targets(AppPaths.Root), t => t.Credential == AppPaths.FirstLaunchWebsiteMarker);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
