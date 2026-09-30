using System.IO;
using Microsoft.Web.WebView2.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

internal sealed record LoginResetResult(IReadOnlyList<string> Completed, IReadOnlyList<string> Failed)
{
    public bool Success => Failed.Count == 0;
    public string Message => Success
        ? "Đã xóa phiên website server trên máy này. ORIGIN, GACHA, SDVN và IslePilot sẽ yêu cầu đăng nhập lại. Quyền Pro được giữ nguyên."
        : $"Chưa xóa hết phiên đăng nhập. Không thành công: {string.Join(", ", Failed)}. Hãy thử lại; một số phiên có thể vẫn còn. Quyền Pro được giữ nguyên.";
}

internal sealed class ServerLoginResetService
{
    internal sealed record Target(string Name, string? Credential, string? Profile);
    internal static IReadOnlyList<Target> Targets(string root) => new Target[]
    {
        new("IslePilot", Path.Combine(root, "islepilot-overlay.credential"), null),
        new("IslePilot Voice", Path.Combine(root, "islepilot-voice.credential"), null),
        new("GACHA", Path.Combine(root, "gacha-overlay.credential"), Path.Combine(root, "GachaWebView2")),
        new("ORIGIN / website dùng chung", null, Path.Combine(root, "WebView2"))
    }.Concat(SdvnTenant.All.Select(t => new Target(t.DisplayName,
        Path.Combine(root, "SDVN", t.Id + ".credential"), Path.Combine(root, "SDVN", t.Id, "WebView2")))).ToArray();

    public Task<LoginResetResult> ClearAsync(nint parent, CancellationToken ct) => ClearAsync(
        Targets(AppPaths.Root), DeleteCredential,
        (path, token) => ClearProfileAsync(parent, path, token), ct);

    internal static void DeleteCredential(string path)
    {
        // File.Delete ignores a missing file but throws when its parent is absent.
        // Never-used server credentials are already logged out, not a failure.
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { }
        if (File.Exists(path)) throw new IOException("Credential remains");
    }

    private static string FailureCode(Exception error) => $"{error.GetType().Name}/0x{error.HResult:X8}";

    internal static async Task<LoginResetResult> ClearAsync(IReadOnlyList<Target> targets,
        Action<string> deleteCredential, Func<string, CancellationToken, Task> clearProfile, CancellationToken ct)
    {
        var completed = new List<string>(); var failed = new List<string>();
        foreach (var target in targets)
        {
            var ok = true;
            if (target.Credential is { } credential)
            {
                try { ct.ThrowIfCancellationRequested(); deleteCredential(credential); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { ok = false; failed.Add(target.Name + " (file phiên: " + FailureCode(error) + ")"); }
            }
            if (target.Profile is { } profile)
            {
                try { ct.ThrowIfCancellationRequested(); await clearProfile(profile, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { ok = false; failed.Add(target.Name + " (cookie / dữ liệu website: " + FailureCode(error) + ")"); }
            }
            if (ok) completed.Add(target.Name);
        }
        return new(completed, failed);
    }

    internal static async Task ClearProfileAsync(nint parent, string path, CancellationToken ct)
    {
        // Only app-owned profile data; never touch the user's Chrome/Edge profile.
        if (!Directory.Exists(path)) return;
        if (parent == 0) throw new InvalidOperationException("Missing window handle");
        ct.ThrowIfCancellationRequested();
        var environment = await CoreWebView2Environment.CreateAsync(null, path);
        ct.ThrowIfCancellationRequested();
        // Await acquisition directly so cancellation cannot orphan a controller.
        var controller = await environment.CreateCoreWebView2ControllerAsync(parent);
        try
        {
            controller.IsVisible = false;
            ct.ThrowIfCancellationRequested();
            await controller.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.Cookies | CoreWebView2BrowsingDataKinds.AllDomStorage);
            // ClearBrowsingData can leave session cookies in a running profile.
            // Explicitly clear the cookie manager as well and verify after its
            // asynchronous browser-process commands have had a chance to settle.
            var cookies = controller.CoreWebView2.CookieManager;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                cookies.DeleteAllCookies();
                await Task.Delay(250, ct);
                var remaining = await cookies.GetCookiesAsync(null);
                if (remaining.Count == 0) return;
            }
            throw new IOException("Cookies still present after clearing");
        }
        finally { controller.Close(); }
    }
}
