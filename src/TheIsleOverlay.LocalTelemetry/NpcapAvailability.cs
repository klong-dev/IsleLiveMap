using SharpPcap;

namespace TheIsleOverlay.LocalTelemetry;

public readonly record struct NpcapAvailability(bool IsAvailable, string? ErrorMessage = null);

public static class NpcapAvailabilityProbe
{
    public static NpcapAvailability Check()
    {
        try
        {
            return CaptureDeviceList.Instance.Count > 0
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

    private static NpcapAvailability Unavailable() => new(
        false,
        "Chưa có Npcap nên app chưa thể đọc vị trí trực tiếp từ game.");
}
