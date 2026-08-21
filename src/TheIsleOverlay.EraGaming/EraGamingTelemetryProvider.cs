using System.Net.Http.Headers;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.EraGaming;

public sealed class EraGamingTelemetryProvider : ITelemetryProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly EraGamingOptions _options;

    public EraGamingTelemetryProvider(HttpClient httpClient, EraGamingOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        if (string.IsNullOrWhiteSpace(options.SessionCookie))
        {
            throw new ArgumentException("EraGaming session cookie is required.", nameof(options));
        }

        if (options.SessionCookie.Contains('\r') || options.SessionCookie.Contains('\n'))
        {
            throw new ArgumentException("Invalid cookie value.", nameof(options));
        }
    }

    public async Task<TelemetrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseUri, "api/theisle/map"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(_options.SessionCookie));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new EraGamingAuthenticationException("Phiên EraGaming đã hết hạn hoặc chưa đăng nhập.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var snapshot = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, JsonOptions, cancellationToken);
        return (snapshot ?? throw new InvalidDataException("EraGaming trả về dữ liệu rỗng.")) with
        {
            Source = "EraGaming"
        };
    }

    private static string BuildCookieHeader(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("era_session=", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"era_session={trimmed}";
    }
}

public sealed class EraGamingAuthenticationException(string message) : Exception(message);
