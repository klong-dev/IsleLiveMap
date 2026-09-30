using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace TheIsleOverlay.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class OriginLogoutBrowserTests
{
    [Fact]
    public async Task ClearingAppProfileRemovesOriginCookieAndPersistsAcrossControllerReopen()
    {
        // Isolated real WebView2 profile only, never actual user cookies.
        if (Environment.GetEnvironmentVariable("ISLE_LOGOUT_BROWSER_CHECK") != "1") return;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                Window? window = null;
                CoreWebView2Controller? controller = null;
                try
                {
                    window = new Window();
                    var hwnd = new WindowInteropHelper(window).EnsureHandle();
                    var profile = Path.Combine(Path.GetTempPath(), "isle-logout-browser-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(profile);
                    var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                    controller = await environment.CreateCoreWebView2ControllerAsync(hwnd);
                    controller.IsVisible = false;
                    var cookies = controller.CoreWebView2.CookieManager;
                    cookies.AddOrUpdateCookie(cookies.CreateCookie("origin_session", "fixture-only", ".playorigin.gg", "/"));
                    cookies.AddOrUpdateCookie(cookies.CreateCookie("steamLoginSecure", "fixture-sso", ".steamcommunity.com", "/"));
                    var persistent = cookies.CreateCookie("persistent-session", "fixture-only", ".islepilot.eu", "/");
                    persistent.Expires = DateTime.UtcNow.AddDays(1);
                    cookies.AddOrUpdateCookie(persistent);
                    Assert.NotNull(await OriginSessionCookieReader.ReadAsync(cookies));
                    controller.Close(); controller = null;
                    await ServerLoginResetService.ClearProfileAsync(hwnd, profile, CancellationToken.None);
                    controller = await environment.CreateCoreWebView2ControllerAsync(hwnd);
                    controller.IsVisible = false;
                    Assert.Null(await OriginSessionCookieReader.ReadAsync(controller.CoreWebView2.CookieManager));
                    Assert.Empty(await controller.CoreWebView2.CookieManager.GetCookiesAsync(null));
                    controller.Close(); controller = null;
                    // Idempotent: a second reset succeeds on an already cleared profile.
                    await ServerLoginResetService.ClearProfileAsync(hwnd, profile, CancellationToken.None);
                    done.SetResult();
                }
                catch (Exception ex) { done.SetException(ex); }
                finally { controller?.Close(); window?.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
