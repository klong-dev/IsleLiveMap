namespace TheIsleOverlay.IslePilot;

public interface IIslePilotOverlayApiClient
{
    Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default);

    Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default);
}
