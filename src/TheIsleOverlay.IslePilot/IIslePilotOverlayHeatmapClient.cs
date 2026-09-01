namespace TheIsleOverlay.IslePilot;

public interface IIslePilotOverlayHeatmapClient
{
    Task<IslePilotOverlayHeatmapDto?> GetHeatmapAsync(
        string? serverName,
        CancellationToken cancellationToken = default);
}
