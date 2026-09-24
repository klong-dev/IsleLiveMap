using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheIsleOverlay.IslePilot;

public sealed class SdvnCredentialStore(string directory, TimeProvider? timeProvider = null)
{
    private readonly string _directory = Path.GetFullPath(directory);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    public static bool IsUsableCookie(string? cookie, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(cookie) || cookie.Length > 16_384
            || cookie.Any(char.IsControl) || cookie.Contains(';')) return false;
        var parts = cookie.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace)) return false;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            // This is an expiry hint only, never signature/auth verification.
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("exp", out var exp)
                && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var seconds)
                && DateTimeOffset.FromUnixTimeSeconds(seconds) > now;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentOutOfRangeException) { return false; }
    }
    private string PathFor(SdvnTenant tenant) => Path.Combine(_directory, tenant.Id + ".credential");
    private static byte[] Entropy(SdvnTenant tenant) => Encoding.UTF8.GetBytes("IsleLiveMap.SDVN.v1." + tenant.Id);
    public async Task SaveAsync(SdvnTenant tenant, string cookie, CancellationToken ct = default)
    {
        tenant.RequireEnabled();
        if (!IsUsableCookie(cookie, _clock.GetUtcNow())) throw new ArgumentException("Cookie SDVN không hợp lệ hoặc đã hết hạn.");
        var clear = JsonSerializer.SerializeToUtf8Bytes(new Credential(tenant.Id, cookie));
        try
        {
            var encrypted = WindowsDataProtection.Protect(clear, Entropy(tenant));
            Directory.CreateDirectory(_directory);
            var temp = PathFor(tenant) + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temp, encrypted, ct); File.Move(temp, PathFor(tenant), true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public async Task<string?> LoadAsync(SdvnTenant tenant, CancellationToken ct = default)
    {
        tenant.RequireEnabled();
        byte[]? clear = null;
        try
        {
            var file = new FileInfo(PathFor(tenant));
            if (!file.Exists) return null;
            if (file.Length > 65536) { Clear(tenant); return null; }
            var encrypted = await File.ReadAllBytesAsync(file.FullName, ct);
            clear = WindowsDataProtection.Unprotect(encrypted, Entropy(tenant));
            var stored = JsonSerializer.Deserialize<Credential>(clear);
            if (stored?.TenantId == tenant.Id && IsUsableCookie(stored.Cookie, _clock.GetUtcNow())) return stored.Cookie;
            Clear(tenant); return null;
        }
        catch (Exception e) when (e is CryptographicException or JsonException) { Clear(tenant); return null; }
        catch (IOException) { return null; }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
    }
    public void Clear(SdvnTenant tenant) { if (File.Exists(PathFor(tenant))) File.Delete(PathFor(tenant)); }
    public SdvnTenant? LastTenant
    {
        get { try { var tenant = SdvnTenant.Find(File.ReadAllText(Path.Combine(_directory, "last-tenant.txt")).Trim()); return tenant?.Enabled == true ? tenant : null; } catch (IOException) { return null; } }
    }
    public void Remember(SdvnTenant tenant)
    {
        tenant.RequireEnabled(); Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "last-tenant.txt"), tenant.Id);
    }
    private sealed record Credential(string TenantId, string Cookie);
}
