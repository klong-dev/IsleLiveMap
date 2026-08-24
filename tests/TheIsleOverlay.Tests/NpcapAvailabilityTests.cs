using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.Tests;

public sealed class NpcapAvailabilityTests
{
    [Fact]
    public void RuntimeProbe_RequiresBothNpcapLibrariesBeforeLoadingSharpPcap()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var npcapDirectory = Path.Combine(directory, "Npcap");
        try
        {
            Directory.CreateDirectory(npcapDirectory);
            Assert.False(NpcapAvailabilityProbe.HasRuntimeFiles(directory));

            File.WriteAllBytes(Path.Combine(npcapDirectory, "wpcap.dll"), []);
            Assert.False(NpcapAvailabilityProbe.HasRuntimeFiles(directory));

            File.WriteAllBytes(Path.Combine(npcapDirectory, "Packet.dll"), []);
            Assert.True(NpcapAvailabilityProbe.HasRuntimeFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
