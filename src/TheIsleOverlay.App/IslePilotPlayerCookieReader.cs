using System.IO;
using Microsoft.Web.WebView2.Core;

namespace TheIsleOverlay.App;

internal static class IslePilotPlayerCookieReader
{
    private const string CookieName = "islepilot_player";

    public static async Task<string?> ReadFromProfileAsync(
        nint parentWindow,
        CancellationToken cancellationToken = default)
    {
        if (parentWindow == 0)
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.WebView2Profile);
        var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.WebView2Profile)
            .WaitAsync(cancellationToken);
        var controller = await environment.CreateCoreWebView2ControllerAsync(parentWindow)
            .WaitAsync(cancellationToken);
        try
        {
            controller.IsVisible = false;
            return await ReadAsync(controller.CoreWebView2.CookieManager, cancellationToken);
        }
        finally
        {
            controller.Close();
        }
    }

    public static async Task<string?> ReadAsync(
        CoreWebView2CookieManager cookieManager,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cookieManager);
        foreach (var uri in new[]
                 {
                     "https://islepilot.eu/",
                     "https://dinovietnam.islepilot.eu/"
                 })
        {
            var cookies = await cookieManager.GetCookiesAsync(uri)
                .WaitAsync(cancellationToken);
            var cookie = cookies.FirstOrDefault(item =>
                string.Equals(item.Name, CookieName, StringComparison.OrdinalIgnoreCase));
            if (cookie is { Value.Length: > 0 })
            {
                return cookie.Value;
            }
        }

        return null;
    }
}
