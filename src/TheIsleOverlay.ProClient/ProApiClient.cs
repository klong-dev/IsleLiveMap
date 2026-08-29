using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TheIsleOverlay.ProClient;

public sealed class ProApiClient
{
    private const long MaximumDownloadBytes = 128L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;

    public ProApiClient(HttpClient httpClient, Uri? baseUri = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _baseUri = baseUri ?? ProClientOptions.ProductionBaseUri;
        if (!_baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The backend base URI must be absolute.", nameof(baseUri));
        }
    }

    public ProLoginAttempt CreateLoginAttempt() => ProLoginAttempt.Create(_baseUri);

    public async Task<ProTokenResponse> ExchangeAsync(
        ProLoginAttempt attempt,
        string callbackUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (!attempt.TryComplete(callbackUri, out var code))
        {
            throw new ProApiException("Steam returned an invalid or mismatched login callback.");
        }

        return await PostAsync<ProTokenResponse>(
            "api/v1/auth/token",
            new { code, codeVerifier = attempt.CodeVerifier },
            cancellationToken).ConfigureAwait(false);
    }

    public Task<ProTokenResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return PostAsync<ProTokenResponse>(
            "api/v1/auth/refresh",
            new { refreshToken },
            cancellationToken);
    }

    public Task<ProEntitlementResponse> GetEntitlementAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetAsync<ProEntitlementResponse>(
            "api/v1/entitlements/me",
            accessToken,
            cancellationToken);

    public async Task<ProReleaseManifest?> GetManifestAsync(
        string hostVersion,
        int ipcApiMajor,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        var path = "api/v1/pro/manifest" +
                   $"?hostVersion={Uri.EscapeDataString(hostVersion)}" +
                   $"&ipcApiMajor={ipcApiMajor}";
        using var request = CreateAuthorizedRequest(HttpMethod.Get, path, accessToken);
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<ProReleaseManifest>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadArtifactAsync(
        Uri downloadUri,
        string accessToken,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(downloadUri);
        ArgumentNullException.ThrowIfNull(destination);
        if (!downloadUri.IsAbsoluteUri ||
            !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(downloadUri.Host, _baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProApiException("The Pro artifact URL is not trusted.");
        }

        using var request = CreateAuthorizedRequest(HttpMethod.Get, downloadUri, accessToken);
        using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
        {
            throw new ProApiException("The Pro artifact exceeds the download limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumDownloadBytes)
            {
                throw new ProApiException("The Pro artifact exceeds the download limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<T> PostAsync<T>(
        string path,
        object payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, path))
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        };
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> GetAsync<T>(
        string path,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, path, accessToken);
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string accessToken) =>
        CreateAuthorizedRequest(method, new Uri(_baseUri, path), accessToken);

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        Uri uri,
        string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new ProApiException("The Isle Live Map license service is unavailable.", null, exception);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false)
                   ?? throw new ProApiException("The license service returned an empty response.", response.StatusCode);
        }
        catch (JsonException exception)
        {
            throw new ProApiException("The license service returned invalid data.", response.StatusCode, exception);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = string.Empty;
        try
        {
            detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (detail.Length > 512)
            {
                detail = detail[..512];
            }
        }
        catch
        {
        }

        throw new ProApiException(
            string.IsNullOrWhiteSpace(detail)
                ? $"The license service returned HTTP {(int)response.StatusCode}."
                : $"The license service rejected the request: {detail}",
            response.StatusCode);
    }
}
