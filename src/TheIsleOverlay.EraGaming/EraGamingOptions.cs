namespace TheIsleOverlay.EraGaming;

public sealed record EraGamingOptions
{
    public Uri BaseUri { get; init; } = new("https://eragamingvn.net/");
    public required string SessionCookie { get; init; }
}
