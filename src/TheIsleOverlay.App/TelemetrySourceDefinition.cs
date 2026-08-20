using System.Net.Http;
using TheIsleOverlay.Core;
using TheIsleOverlay.EraGaming;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public enum TelemetrySourceKind
{
    EraGaming,
    IslePilot
}

public sealed record TelemetrySourceDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ShortName { get; init; }
    public required TelemetrySourceKind Kind { get; init; }
    public required Uri BaseUri { get; init; }
    public required Uri LoginUri { get; init; }
    public required Uri MapUri { get; init; }
    public required string CookieName { get; init; }
    public string? ServerSlug { get; init; }

    public ITelemetryProvider CreateProvider(HttpClient httpClient, string cookieValue) => Kind switch
    {
        TelemetrySourceKind.EraGaming => new EraGamingTelemetryProvider(
            httpClient,
            new EraGamingOptions
            {
                BaseUri = BaseUri,
                SessionCookie = cookieValue
            }),
        TelemetrySourceKind.IslePilot => new IslePilotTelemetryProvider(
            httpClient,
            new IslePilotOptions
            {
                BaseUri = BaseUri,
                ServerSlug = ServerSlug ?? throw new InvalidOperationException("IslePilot source requires a server slug."),
                DisplayName = DisplayName,
                PlayerCookie = cookieValue
            }),
        _ => throw new ArgumentOutOfRangeException()
    };

    public static TelemetrySourceDefinition EraGaming { get; } = new()
    {
        Id = "era",
        DisplayName = "EraGaming",
        ShortName = "ERA",
        Kind = TelemetrySourceKind.EraGaming,
        BaseUri = new Uri("https://eragamingvn.net/"),
        LoginUri = new Uri("https://eragamingvn.net/live-map"),
        MapUri = EraGamingTelemetryProvider.GatewayMapUri,
        CookieName = "era_session"
    };

    public static TelemetrySourceDefinition DinoVietnam { get; } = new()
    {
        Id = "dinovietnam",
        DisplayName = "DinoVietNam",
        ShortName = "DINO VN",
        Kind = TelemetrySourceKind.IslePilot,
        BaseUri = new Uri("https://dinovietnam.islepilot.eu/"),
        LoginUri = new Uri("https://dinovietnam.islepilot.eu/map"),
        MapUri = new Uri("https://dinovietnam.islepilot.eu/maps/gateway-v0.21/base.webp"),
        CookieName = "islepilot_player",
        ServerSlug = "dinovietnam"
    };

    public static TelemetrySourceDefinition DinoVietnamPremium { get; } = new()
    {
        Id = "dinovietnampremium",
        DisplayName = "DinoVietNam Premium",
        ShortName = "DINO VIP",
        Kind = TelemetrySourceKind.IslePilot,
        BaseUri = new Uri("https://dinovietnampremium.islepilot.eu/"),
        LoginUri = new Uri("https://dinovietnampremium.islepilot.eu/map"),
        MapUri = new Uri("https://dinovietnampremium.islepilot.eu/maps/gateway-v0.21/base.webp"),
        CookieName = "islepilot_player",
        ServerSlug = "dinovietnampremium"
    };

    public static IReadOnlyList<TelemetrySourceDefinition> All { get; } =
    [
        EraGaming,
        DinoVietnam,
        DinoVietnamPremium
    ];

    public static TelemetrySourceDefinition? FromId(string? id) =>
        All.FirstOrDefault(source => string.Equals(source.Id, id, StringComparison.OrdinalIgnoreCase));
}
