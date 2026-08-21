using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.Pandora;

public sealed class PandoraTelemetryProvider : ITelemetryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PandoraOptions _options;

    public PandoraTelemetryProvider(HttpClient httpClient, PandoraOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.SessionCookieHeader))
        {
            throw new ArgumentException("A PANDORA website session is required.", nameof(options));
        }

        if (options.SessionCookieHeader.Contains('\r') || options.SessionCookieHeader.Contains('\n'))
        {
            throw new ArgumentException("The PANDORA cookie header is invalid.", nameof(options));
        }
    }

    public async Task<TelemetrySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(_options.BaseUri, "api/map/mylocation"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Cookie", _options.SessionCookieHeader.Trim());

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new PandoraAuthenticationException(
                "Phiên PANDORA đã hết hạn hoặc tài khoản chưa liên kết Steam.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<PandoraLocationResponse>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidDataException("PANDORA returned an empty location response.");

        if (string.Equals(payload.Error, "Forbidden", StringComparison.OrdinalIgnoreCase))
        {
            throw new PandoraAuthenticationException(
                "Phiên PANDORA đã hết hạn hoặc tài khoản chưa liên kết Steam.");
        }

        var serverAvailable = payload.Success != false;
        var playerOnline = serverAvailable && payload.InGame == true && payload.Player is not null;
        return new TelemetrySnapshot
        {
            Source = "PANDORA",
            Success = serverAvailable,
            ServerOnline = serverAvailable,
            PlayerOnline = playerOnline,
            UpdatedAt = payload.UpdatedAt ?? DateTimeOffset.UtcNow,
            Player = playerOnline ? ToPlayer(payload.Player!) : null
        };
    }

    private static PlayerTelemetry ToPlayer(PandoraPlayer player)
    {
        var location = ToLocation(player);
        double? exactHeading = player.Heading is not null
            ? MapHeading.Normalize(player.Heading.Value)
            : player.Yaw is not null
                ? MapHeading.FromUnrealYaw(player.Yaw.Value)
                : null;

        return new PlayerTelemetry
        {
            SteamId = player.SteamId,
            Name = player.Name,
            Class = player.Dino ?? player.Species,
            Server = string.IsNullOrWhiteSpace(player.Server) ? "Isla Pandora" : player.Server,
            Female = ToFemale(player.Gender),
            GrowthPercent = ToPercent(player.Growth),
            HealthPercent = ToPercent(player.Health),
            StaminaPercent = ToPercent(player.Stamina),
            HungerPercent = ToPercent(player.Hunger),
            ThirstPercent = ToPercent(player.Thirst),
            Location = location,
            MapLocation = location is null ? null : GatewayMapProjection.Project(location),
            ExactMapHeadingDegrees = exactHeading
        };
    }

    private static WorldLocation? ToLocation(PandoraPlayer player) =>
        player.X is not null && player.Y is not null &&
        double.IsFinite(player.X.Value) && double.IsFinite(player.Y.Value)
            ? new WorldLocation { X = player.X.Value, Y = player.Y.Value, Z = player.Z }
            : null;

    private static double? ToPercent(double? value)
    {
        if (value is null || !double.IsFinite(value.Value))
        {
            return null;
        }

        var percent = value.Value <= 1d ? value.Value * 100d : value.Value;
        return Math.Clamp(percent, 0d, 100d);
    }

    private static bool? ToFemale(string? gender) => gender?.Trim() switch
    {
        { } value when value.Equals("Female", StringComparison.OrdinalIgnoreCase) => true,
        { } value when value.Equals("Male", StringComparison.OrdinalIgnoreCase) => false,
        _ => null
    };

    private sealed record PandoraLocationResponse
    {
        public bool? Success { get; init; }
        public bool? InGame { get; init; }
        public string? Error { get; init; }
        public DateTimeOffset? UpdatedAt { get; init; }
        public PandoraPlayer? Player { get; init; }
    }

    private sealed record PandoraPlayer
    {
        public string? SteamId { get; init; }
        public string? Name { get; init; }
        public string? Dino { get; init; }
        public string? Species { get; init; }
        public string? Server { get; init; }
        public string? Gender { get; init; }
        public double? X { get; init; }
        public double? Y { get; init; }
        public double? Z { get; init; }
        public double? Yaw { get; init; }
        public double? Heading { get; init; }
        public double? Growth { get; init; }
        public double? Health { get; init; }
        public double? Stamina { get; init; }
        public double? Hunger { get; init; }
        public double? Thirst { get; init; }
    }
}

public sealed class PandoraAuthenticationException(string message)
    : TelemetryAuthenticationException(message);
