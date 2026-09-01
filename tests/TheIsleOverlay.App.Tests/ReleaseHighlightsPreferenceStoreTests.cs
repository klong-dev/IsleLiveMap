using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class ReleaseHighlightsPreferenceStoreTests
{
    [Fact]
    public void HideVersion_PersistsOnlyTheSelectedRelease()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ilm-release-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new ReleaseHighlightsPreferenceStore(path);
            Assert.True(store.ShouldShow("1.4.3"));

            store.HideVersion("1.4.3");

            var reloaded = new ReleaseHighlightsPreferenceStore(path);
            Assert.False(reloaded.ShouldShow("1.4.3"));
            Assert.True(reloaded.ShouldShow("1.4.4"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void CorruptPreferenceFile_FailsOpenAndShowsTheWizard()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not-json");
            Assert.True(new ReleaseHighlightsPreferenceStore(path).ShouldShow("1.4.3"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
