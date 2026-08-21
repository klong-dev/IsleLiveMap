using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotOverlayApiClient
{
    private static readonly Uri MeUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/me");
    private static readonly Uri MapUri = new(IslePilotOverlayOptions.ServiceBaseUri, "api/overlay/map");

    private readonly HttpClient _httpClient;
    private readonly string _overlayToken;

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
    }

    public Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMeDto>(MeUri, cancellationToken);

    public Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IslePilotOverlayMapDto>(MapUri, cancellationToken);

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
}

public sealed class IslePilotOverlayAuthenticationException(string message)
    : TelemetryAuthenticationException(message);
