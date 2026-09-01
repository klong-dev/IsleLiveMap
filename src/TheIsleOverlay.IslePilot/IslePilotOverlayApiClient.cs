using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotOverlayApiClient :
    IIslePilotOverlayApiClient,
    IIslePilotOverlayHeatmapClient
{
    private static readonly Uri MeUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/me");
    private static readonly Uri MapUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/map");

    private readonly HttpClient _httpClient;
    private readonly string _overlayToken;
    private readonly string? _playerCookie;
    private readonly SemaphoreSlim _heatmapGate = new(1, 1);
    private string? _negativeHeatmapServer;
    private DateTimeOffset _negativeHeatmapExpiresAt;

    public IslePilotOverlayApiClient(HttpClient httpClient, IslePilotOverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.OverlayToken))
        {
            throw new ArgumentException("An IslePilot overlay token is required.", nameof(options));
        }

        if (options.OverlayToken.Contains('\r') || options.OverlayToken.Contains('\n'))
        {
            throw new ArgumentException("The IslePilot overlay token is invalid.", nameof(options));
        }

        _httpClient = httpClient;
        _overlayToken = options.OverlayToken;
        _playerCookie = NormalizeCookie(options.PlayerCookie);
    }

    public Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMeDto>(MeUri, cancellationToken);

    public Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMapDto>(MapUri, cancellationToken);

    public async Task<IslePilotOverlayHeatmapDto?> GetHeatmapAsync(
        string? serverName,
        CancellationToken cancellationToken = default)
    {
        if (_playerCookie is null || string.IsNullOrWhiteSpace(serverName))
        {
            return null;
        }

        var normalizedServer = serverName.Trim();
        if (string.Equals(_negativeHeatmapServer, normalizedServer, StringComparison.Ordinal)
            && DateTimeOffset.UtcNow < _negativeHeatmapExpiresAt)
        {
            return IslePilotOverlayHeatmapDto.Empty;
        }

        await _heatmapGate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_negativeHeatmapServer, normalizedServer, StringComparison.Ordinal)
                && DateTimeOffset.UtcNow < _negativeHeatmapExpiresAt)
            {
                return IslePilotOverlayHeatmapDto.Empty;
            }

            foreach (var slug in IslePilotServerSlugCandidates.Resolve(normalizedServer))
            {
                var endpoint = new Uri(
                    $"https://{slug}.islepilot.eu/api/p/{Uri.EscapeDataString(slug)}/map/heatmap");
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.CacheControl = new CacheControlHeaderValue
                {
                    NoCache = true,
                    NoStore = true
                };
                request.Headers.TryAddWithoutValidation("Cookie", _playerCookie);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (exception is HttpRequestException
                                                   or OperationCanceledException)
                {
                    continue;
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    try
                    {
                        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                        var heatmap = await JsonSerializer.DeserializeAsync<IslePilotOverlayHeatmapDto>(
                            stream,
                            IslePilotOverlayJson.Options,
                            cancellationToken);
                        if (heatmap?.Ok == true)
                        {
                            _negativeHeatmapServer = null;
                            _negativeHeatmapExpiresAt = default;
                            return heatmap;
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }
            }

            _negativeHeatmapServer = normalizedServer;
            _negativeHeatmapExpiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45);
            return IslePilotOverlayHeatmapDto.Empty;
        }
        finally
        {
            _heatmapGate.Release();
        }
    }

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _overlayToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Overlay-Version", "2");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new IslePilotOverlayAuthenticationException(
                "Phiên IslePilot đã hết hạn hoặc chưa đăng nhập.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(
                stream,
                IslePilotOverlayJson.Options,
                cancellationToken)
            ?? throw new InvalidDataException("IslePilot returned an empty overlay response.");
    }

    private static string? NormalizeCookie(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || trimmed.Length > 16_384
            || trimmed.Contains('\r')
            || trimmed.Contains('\n'))
        {
            return null;
        }

        return trimmed.StartsWith("islepilot_player=", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"islepilot_player={trimmed}";
    }
}

internal static class IslePilotServerSlugCandidates
{
    private static readonly string[] NoiseTokens =
    [
        "server", "evrima", "official", "semi", "realism", "realistic",
        "the", "isle", "discord", "new", "season"
    ];

    public static IReadOnlyList<string> Resolve(string serverName)
    {
        var tokens = Tokenize(serverName)
            .Where(token => !NoiseTokens.Contains(token, StringComparer.Ordinal))
            .Take(8)
            .ToArray();
        var full = string.Concat(tokens);
        var candidates = new List<string>(4);

        AddKnown(full, candidates);
        Add(full, candidates);
        Add(string.Concat(tokens.TakeWhile(token => !token.All(char.IsDigit))), candidates);
        if (tokens.Length >= 2)
        {
            Add(string.Concat(tokens.Take(2)), candidates);
        }

        return candidates.Take(4).ToArray();
    }

    private static IReadOnlyList<string> Tokenize(string source)
    {
        var normalized = source.Normalize(NormalizationForm.FormD);
        var result = new List<string>();
        var token = new StringBuilder();
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                token.Append(char.ToLowerInvariant(character));
            }
            else if (token.Length > 0)
            {
                result.Add(token.ToString());
                token.Clear();
            }
        }

        if (token.Length > 0)
        {
            result.Add(token.ToString());
        }

        return result;
    }

    private static void AddKnown(string normalized, ICollection<string> candidates)
    {
        if (normalized.Contains("dinovietnampremium", StringComparison.Ordinal))
        {
            Add("dinovietnampremium", candidates);
        }
        else if (normalized.Contains("dinovietnam", StringComparison.Ordinal))
        {
            Add("dinovietnam", candidates);
        }

        if (normalized.Contains("hoho", StringComparison.Ordinal))
        {
            Add("hoho", candidates);
        }
    }

    private static void Add(string candidate, ICollection<string> candidates)
    {
        if (candidate.Length is < 1 or > 63
            || candidates.Contains(candidate, StringComparer.Ordinal))
        {
            return;
        }

        candidates.Add(candidate);
    }
}

public sealed class IslePilotOverlayAuthenticationException(string message)
    : TelemetryAuthenticationException(message);
