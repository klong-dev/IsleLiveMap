namespace TheIsleOverlay.IslePilot;

public sealed record SdvnTenant
{
    private SdvnTenant(string id, string name, string host, string? slug, bool enabled = true)
    { Id = id; DisplayName = name; Host = host; ServerSlug = slug; Enabled = enabled; }
    public string Id { get; }
    public string DisplayName { get; }
    public string Host { get; }
    public Uri BaseUri => new($"https://{Host}/");
    public Uri LoginUri => new(BaseUri, "api/player/steam/login?redirect=%2Fme");
    public string? ServerSlug { get; }
    public bool Enabled { get; }
    public string? DisabledReason => Enabled ? null : "TENANT TẠM KHÓA — LỖI TLS";
    public static IReadOnlyList<SdvnTenant> All { get; } = Array.AsReadOnly(new[]
    {
        new SdvnTenant("sdvn-1", "SDVN #1 · Low Rules", "1.sdvn.org", "sdvn"),
        new SdvnTenant("sdvn-2", "SDVN #2 · No Rules", "2.sdvn.org", "sdvn2234234"),
        new SdvnTenant("sdvn-3", "SDVN #3 · X3 Grow", "3.sdvn.org", "sdvn3123123123"),
        new SdvnTenant("sdvn-4", "SDVN #4", "4.sdvn.org", null, false)
    });
    public static IEnumerable<SdvnTenant> Available => All.Where(tenant => tenant.Enabled);
    public static SdvnTenant? Find(string? id) => All.FirstOrDefault(tenant => tenant.Id == id);
    public void RequireEnabled()
    {
        if (!Enabled || ServerSlug is null) throw new InvalidOperationException(DisabledReason);
    }
    public bool IsTrusted(Uri uri) => uri.Scheme == "https" && uri.Port == 443
        && string.Equals(uri.IdnHost, Host, StringComparison.OrdinalIgnoreCase);
}
