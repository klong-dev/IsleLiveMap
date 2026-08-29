namespace TheIsleOverlay.ProClient;

public static class ProLoginNavigationPolicy
{
    private static readonly HashSet<string> SteamHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "steamcommunity.com",
        "store.steampowered.com"
    };

    public static bool IsAllowed(string? value, Uri backendBaseUri)
    {
        ArgumentNullException.ThrowIfNull(backendBaseUri);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (ProLoginAttempt.IsCallback(value))
        {
            return true;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(uri.Host, backendBaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                SteamHosts.Contains(uri.Host));
    }
}
