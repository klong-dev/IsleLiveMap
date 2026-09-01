namespace TheIsleOverlay.IslePilot;

public sealed record IslePilotOverlayOptions
{
    public static Uri ServiceBaseUri { get; } = new("https://islepilot.eu/");
    public static Uri WebSocketUri { get; } = new("wss://islepilot.eu/ows");

    public required string OverlayToken { get; init; }
    public string? PlayerCookie { get; init; }
    public TimeSpan MeRefreshInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan MapRefreshInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan LiveDataLifetime { get; init; } = TimeSpan.FromSeconds(4);
}
