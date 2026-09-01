namespace TheIsleOverlay.IslePilot;

public static class IslePilotVoiceLoginNavigationPolicy
{
    private static readonly string[] HttpsHostSuffixes =
    [
        "islepilot.eu",
        "steamcommunity.com",
        "steampowered.com"
    ];

    public static bool IsAllowed(string? rawUri)
    {
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(
                uri.Scheme,
                IslePilotVoiceAuthService.CallbackScheme,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && HttpsHostSuffixes.Any(suffix =>
                   string.Equals(uri.IdnHost, suffix, StringComparison.OrdinalIgnoreCase)
                   || uri.IdnHost.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase));
    }
}
