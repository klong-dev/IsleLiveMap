namespace TheIsleOverlay.App;

public static class LoginNavigationPolicy
{
    public static bool IsAllowed(TelemetrySourceDefinition source, string rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443) return false;
        if (uri.IdnHost.Equals(source.BaseUri.IdnHost, StringComparison.OrdinalIgnoreCase)) return true;
        // Explicit SDVN tenants never navigate to sibling tenants or use their cookies.
        string[] authHosts = ["steamcommunity.com", "steampowered.com"];
        if (authHosts.Any(host => uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) return true;
        // Preserve the existing Discord login path for Era/Pandora only.
        return source.Kind is TelemetrySourceKind.EraGaming or TelemetrySourceKind.Pandora
            && (uri.IdnHost == "discord.com" || uri.IdnHost == "www.discord.com");
    }
}
