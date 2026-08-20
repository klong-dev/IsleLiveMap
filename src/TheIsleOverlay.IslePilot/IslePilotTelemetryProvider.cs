using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotTelemetryProvider : ITelemetryProvider
{
    public static readonly Uri GatewayMapUri = new("https://dinovietnam.islepilot.eu/maps/gateway-v0.21/base.webp");

    private static readonly IslePilotPlayerPage EmptyStats = new(
        null, false, null, null, null, null, null, null, null);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IslePilotOptions _options;
    private readonly SemaphoreSlim _statsGate = new(1, 1);
    private readonly object _statsStateLock = new();
    private readonly string? _personaName;
    private IslePilotPlayerPage? _cachedStats;
    private DateTimeOffset _statsExpiresAt;
    private Task? _backgroundStatsRefresh;

    public IslePilotTelemetryProvider(HttpClient httpClient, IslePilotOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        if (string.IsNullOrWhiteSpace(options.PlayerCookie))
        {
            throw new ArgumentException("IslePilot player cookie is required.", nameof(options));
        }

        if (options.PlayerCookie.Contains('\r') || options.PlayerCookie.Contains('\n'))
        {
            throw new ArgumentException("Invalid cookie value.", nameof(options));
        }

        _personaName = IslePilotCookieIdentity.TryGetPersonaName(options.PlayerCookie);
    }

    public async Task<TelemetrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var markersTask = GetMarkersAsync(cancellationToken);
        var stats = ReadCachedStats();
        if (stats is null)
        {
            // Only the first snapshot waits for /me. Afterwards marker/yaw updates
            // must never be held up by the slower player-page endpoint.
            stats = await GetInitialPlayerStatsOrFallbackAsync(cancellationToken);
        }
        else
        {
            RefreshStatsInBackgroundIfExpired();
        }

        var markers = await markersTask;
        var marker = markers.Markers.FirstOrDefault(item => item.Self) ?? markers.Markers.FirstOrDefault();
        var online = marker is not null || stats.Online;

        return new TelemetrySnapshot
        {
            Source = _options.DisplayName,
            Success = markers.Ok,
            ServerOnline = markers.Ok,
            PlayerOnline = online,
            UpdatedAt = DateTimeOffset.UtcNow,
            Player = online
                ? new PlayerTelemetry
                {
                    Name = _personaName,
                    Class = stats.Species,
                    GrowthPercent = stats.GrowthPercent,
                    HealthPercent = Percent(stats.Health, stats.MaxHealth),
                    HungerPercent = Percent(stats.Hunger, stats.MaxHunger),
                    ThirstPercent = Percent(stats.Thirst, stats.MaxThirst),
                    ExactVitalsSource = "IslePilotPageV1",
                    ExactVitals = new ExactVitals
                    {
                        Growth = stats.GrowthPercent,
                        Health = stats.Health,
                        MaxHealth = stats.MaxHealth,
                        Hunger = stats.Hunger,
                        MaxHunger = stats.MaxHunger,
                        Thirst = stats.Thirst,
                        MaxThirst = stats.MaxThirst
                    },
                    Location = marker is null ? null : new WorldLocation { X = marker.X, Y = marker.Y },
                    ExactMapHeadingDegrees = marker?.Yaw is null ? null : MapHeading.FromUnrealYaw(marker.Yaw.Value)
                }
                : null
        };
    }

    private async Task<IslePilotPlayerPage> GetInitialPlayerStatsOrFallbackAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetPlayerStatsAsync(cancellationToken);
        }
        catch (IslePilotAuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or ArgumentException or OperationCanceledException)
        {
            // /me is secondary telemetry. Keep markers and yaw usable while the
            // player page is slow, temporarily unavailable, or changes shape.
            WriteStatsState(EmptyStats, DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3));
            return EmptyStats;
        }
    }

    private async Task<IslePilotMarkersResponse> GetMarkersAsync(CancellationToken cancellationToken)
    {
        var relative = $"api/p/{Uri.EscapeDataString(_options.ServerSlug)}/map/markers";
        using var request = CreateRequest(new Uri(_options.BaseUri, relative));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        ThrowIfAuthenticationFailed(response);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<IslePilotMarkersResponse>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("IslePilot markers response was empty.");
    }

    private async Task<IslePilotPlayerPage> GetPlayerStatsAsync(CancellationToken cancellationToken)
    {
        var freshStats = ReadFreshStats();
        if (freshStats is not null)
        {
            return freshStats;
        }

        await _statsGate.WaitAsync(cancellationToken);
        try
        {
            freshStats = ReadFreshStats();
            if (freshStats is not null)
            {
                return freshStats;
            }

            try
            {
                using var request = CreateRequest(new Uri(_options.BaseUri, "me"));
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                ThrowIfAuthenticationFailed(response);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var parsed = IslePilotPlayerPageParser.Parse(html);
                WriteStatsState(parsed, DateTimeOffset.UtcNow + _options.StatsRefreshInterval);
                return parsed;
            }
            catch (HttpRequestException) when (ReadCachedStats() is not null)
            {
                DelayStatsRetry(TimeSpan.FromSeconds(3));
                return ReadCachedStats()!;
            }
        }
        finally
        {
            _statsGate.Release();
        }
    }

    private IslePilotPlayerPage? ReadCachedStats()
    {
        lock (_statsStateLock)
        {
            return _cachedStats;
        }
    }

    private IslePilotPlayerPage? ReadFreshStats()
    {
        lock (_statsStateLock)
        {
            return _cachedStats is not null && DateTimeOffset.UtcNow < _statsExpiresAt
                ? _cachedStats
                : null;
        }
    }

    private void WriteStatsState(IslePilotPlayerPage stats, DateTimeOffset expiresAt)
    {
        lock (_statsStateLock)
        {
            _cachedStats = stats;
            _statsExpiresAt = expiresAt;
        }
    }

    private void DelayStatsRetry(TimeSpan delay)
    {
        lock (_statsStateLock)
        {
            if (_cachedStats is not null)
            {
                _statsExpiresAt = DateTimeOffset.UtcNow + delay;
            }
        }
    }

    private void RefreshStatsInBackgroundIfExpired()
    {
        lock (_statsStateLock)
        {
            if (_cachedStats is null || DateTimeOffset.UtcNow < _statsExpiresAt ||
                _backgroundStatsRefresh is { IsCompleted: false })
            {
                return;
            }

            _backgroundStatsRefresh = RefreshStatsIgnoringErrorsAsync();
        }
    }

    private async Task RefreshStatsIgnoringErrorsAsync()
    {
        try
        {
            await GetPlayerStatsAsync(CancellationToken.None);
        }
        catch
        {
            // Marker polling remains available even if the secondary /me page
            // temporarily fails, expires, or the app is shutting down.
        }
    }

    private HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(_options.PlayerCookie));
        return request;
    }

    private static void ThrowIfAuthenticationFailed(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new IslePilotAuthenticationException("Phiên IslePilot đã hết hạn hoặc chưa đăng nhập.");
        }
    }

    private static string BuildCookieHeader(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("islepilot_player=", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"islepilot_player={trimmed}";
    }

    private static double? Percent(double? current, double? maximum) =>
        current is not null && maximum is > 0 ? Math.Clamp(current.Value / maximum.Value * 100d, 0d, 100d) : null;
}

public sealed class IslePilotAuthenticationException(string message) : Exception(message);

public sealed record IslePilotMarkersResponse
{
    public bool Ok { get; init; }
    public IReadOnlyList<IslePilotMarker> Markers { get; init; } = [];
}

public sealed record IslePilotMarker
{
    public string? SteamId { get; init; }
    public string? Label { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double? Yaw { get; init; }
    public bool Self { get; init; }
}
