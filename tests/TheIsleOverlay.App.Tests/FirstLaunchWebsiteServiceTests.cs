using System.Diagnostics;
using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class FirstLaunchWebsiteServiceTests
{
    [Fact]
    public void OpensTheDefaultBrowserOnlyOnceAcrossServiceInstances()
    {
        var root = Path.Combine(Path.GetTempPath(), "isle-first-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var starts = new List<ProcessStartInfo>();
            Process? FakeStart(ProcessStartInfo info)
            {
                starts.Add(info);
                return null;
            }

            // Process.Start returning null is treated as a failed launch and
            // must not consume the one-time marker.
            var failed = new FirstLaunchWebsiteService(Path.Combine(root, "opened.marker"), FakeStart);
            Assert.False(failed.TryOpenOnce());
            Assert.False(File.Exists(Path.Combine(root, "opened.marker")));
            Assert.Single(starts);

            Process? SuccessfulStart(ProcessStartInfo info)
            {
                starts.Add(info);
                return Process.GetCurrentProcess();
            }

            var service = new FirstLaunchWebsiteService(Path.Combine(root, "opened.marker"), SuccessfulStart);
            Assert.True(service.TryOpenOnce());
            Assert.False(new FirstLaunchWebsiteService(Path.Combine(root, "opened.marker"), SuccessfulStart).TryOpenOnce());
            Assert.Equal(2, starts.Count);
            Assert.Equal(FirstLaunchWebsiteService.WebsiteUri, starts[1].FileName);
            Assert.True(starts[1].UseShellExecute);
            Assert.Contains("uri=" + FirstLaunchWebsiteService.WebsiteUri, File.ReadAllText(Path.Combine(root, "opened.marker")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
