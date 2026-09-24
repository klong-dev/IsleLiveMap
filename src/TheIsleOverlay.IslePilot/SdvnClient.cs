using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class SdvnClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _cookie;
    public SdvnTenant Tenant { get; }
    // Accept a handler (not an arbitrary redirecting HttpClient) for offline tests.
    public SdvnClient(SdvnTenant tenant, string cookie, HttpMessageHandler? handler = null)
    {
        tenant.RequireEnabled();
        if (!SdvnCredentialStore.IsUsableCookie(cookie, DateTimeOffset.UtcNow)) throw new IslePilotAuthenticationException("Phiên SDVN đã hết hạn.");
        Tenant = tenant; _cookie = cookie;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(8) };
    }
    private async Task<string> GetAsync(string path, CancellationToken ct)
    {
        var uri = new Uri(Tenant.BaseUri, path);
        if (!Tenant.IsTrusted(uri)) throw new InvalidDataException("Host SDVN không hợp lệ.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.AcceptLanguage.ParseAdd("en");
        request.Headers.TryAddWithoutValidation("Cookie", "islepilot_player=" + _cookie);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new IslePilotAuthenticationException("Phiên SDVN hết hạn; hãy đăng nhập lại.");
        if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidDataException("SDVN trả chuyển hướng ngoài luồng API.");
        if (!Tenant.IsTrusted(response.RequestMessage!.RequestUri!)) throw new InvalidDataException("SDVN trả dữ liệu từ host khác.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
    public async Task<IslePilotPlayerPage> GetPlayerAsync(CancellationToken ct = default)
    {
        var html = await GetAsync("me", ct).ConfigureAwait(false);
        // Next.js embeds scripts/RSC strings containing unrelated auth links;
        // only actual page anchors establish login/logout state.
        var markup = Regex.Replace(html, @"<script\b[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        if (Regex.IsMatch(markup, @"<a\b[^>]*href\s*=\s*[""'][^""']*/api/player/steam/login(?:\?|[""'])", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
            throw new IslePilotAuthenticationException("Phiên SDVN chưa đăng nhập hoặc đã hết hạn.");
        if (markup.Contains("Sign in with Steam to see your current dinosaur", StringComparison.OrdinalIgnoreCase))
            throw new IslePilotAuthenticationException("Phiên SDVN chưa đăng nhập hoặc đã hết hạn.");
        var player = IslePilotPlayerPageParser.Parse(markup);
        var hasStats = player.GrowthPercent is not null || player.Health is not null || player.Thirst is not null;
        var authenticatedPage = Regex.IsMatch(markup, @"<a\b[^>]*href\s*=\s*[""'][^""']*/api/player/(?:steam/)?logout(?:\?|[""'])", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (!hasStats && !authenticatedPage)
            throw new InvalidDataException("Trang SDVN không có cấu trúc dữ liệu được hỗ trợ.");
        // A heading such as 'My dinosaur' is not a species when no dino exists.
        return hasStats ? player : player with { Species = null, Online = false };
    }
    public async Task<IslePilotMarkersResponse> GetMarkersAsync(CancellationToken ct = default)
    {
        var json = await GetAsync($"api/p/{Uri.EscapeDataString(Tenant.ServerSlug!)}/map/markers", ct).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<IslePilotMarkersResponse>(json, IslePilotOverlayJson.Options);
        if (result?.Ok != true) throw new InvalidDataException("SDVN chưa cung cấp marker cho phiên này.");
        return result;
    }
    public async Task ValidateAsync(CancellationToken ct = default) =>
        await Task.WhenAll(GetPlayerAsync(ct), GetMarkersAsync(ct)).ConfigureAwait(false);
    public void Dispose() => _http.Dispose();
}
