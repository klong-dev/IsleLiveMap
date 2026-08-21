namespace TheIsleOverlay.IslePilot;

public interface IIslePilotOverlayWebSocket : IAsyncDisposable
{
    Task ConnectAsync(string overlayToken, CancellationToken cancellationToken = default);

    Task SendHelloAsync(string? personaName, CancellationToken cancellationToken = default);

    IAsyncEnumerable<IslePilotOverlayLiveDataDto> ReadLiveAsync(
        CancellationToken cancellationToken = default);
}
