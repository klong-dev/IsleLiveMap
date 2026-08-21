namespace TheIsleOverlay.Pandora;

public sealed record PandoraOptions
{
    public Uri BaseUri { get; init; } = new("https://islapandora.eu/");
    public required string SessionCookieHeader { get; init; }
}
