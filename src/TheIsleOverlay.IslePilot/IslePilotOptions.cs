namespace TheIsleOverlay.IslePilot;

public sealed record IslePilotOptions
{
    public Uri BaseUri { get; init; } = new("https://dinovietnam.islepilot.eu/");
    public string ServerSlug { get; init; } = "dinovietnam";
    public string DisplayName { get; init; } = "DinoVietnam";
    public required string PlayerCookie { get; init; }
    public TimeSpan StatsRefreshInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan HeatmapRefreshInterval { get; init; } = TimeSpan.FromSeconds(15);
}
