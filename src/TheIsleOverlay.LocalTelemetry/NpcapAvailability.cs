using SharpPcap;

namespace TheIsleOverlay.LocalTelemetry;

public readonly record struct NpcapAvailability(bool IsAvailable, string? ErrorMessage = null);

public static class NpcapAvailabilityProbe
{
    public static NpcapAvailability Check(bool refresh = false)
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!HasRuntimeFiles(systemDirectory))
        {
            return Unavailable();
        }

        try
        {
            var devices = CaptureDeviceList.Instance;
            if (refresh)
            {
                devices.Refresh();
            }

            return devices.Count > 0
                ? new NpcapAvailability(true)
                : Unavailable();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or TypeInitializationException
                or PcapException)
        {
            return Unavailable();
        }
    }

    internal static bool HasRuntimeFiles(string systemDirectory) =>
        File.Exists(Path.Combine(systemDirectory, "Npcap", "wpcap.dll"))
        && File.Exists(Path.Combine(systemDirectory, "Npcap", "Packet.dll"));

    private static NpcapAvailability Unavailable() => new(
        false,
        "Chưa có Npcap nên app chưa thể đọc vị trí trực tiếp từ game.");
}
