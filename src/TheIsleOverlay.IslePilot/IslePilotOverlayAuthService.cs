namespace TheIsleOverlay.IslePilot;

public static class IslePilotOverlayAuthService
{
    public const string CallbackScheme = "isle-overlay";

    public static Uri LoginUri { get; } = new(
        IslePilotOverlayOptions.ServiceBaseUri,
        "api/overlay/auth/steam");

    public static async Task<IslePilotOverlayAuthValidationState> ValidateAsync(
        HttpClient httpClient,
        IslePilotOverlayAuthResult credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentials);
        if (!IsValidCredentials(credentials.SteamId, credentials.OverlayToken))
        {
            throw new ArgumentException("The IslePilot credentials are invalid.", nameof(credentials));
        }

        try
        {
            var options = new IslePilotOverlayOptions
            {
                OverlayToken = credentials.OverlayToken
            };
            var apiClient = new IslePilotOverlayApiClient(httpClient, options);
            _ = await apiClient.GetMeAsync(cancellationToken);
            return IslePilotOverlayAuthValidationState.Valid;
        }
        catch (IslePilotOverlayAuthenticationException)
        {
            return IslePilotOverlayAuthValidationState.Invalid;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return IslePilotOverlayAuthValidationState.Unavailable;
        }
    }

    public static bool TryParseCallback(
        string? callback,
        out IslePilotOverlayAuthResult? result)
    {
        result = null;
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, CallbackScheme, StringComparison.OrdinalIgnoreCase)
            || !TryParseQuery(uri.Query, out var query)
            || !query.TryGetValue("sid", out var steamId)
            || !query.TryGetValue("token", out var overlayToken)
            || !IsValidCredentials(steamId, overlayToken))
        {
            return false;
        }

        result = new IslePilotOverlayAuthResult(steamId, overlayToken);
        return true;
    }

    internal static bool IsValidCredentials(string? steamId, string? overlayToken) =>
        steamId is { Length: 17 }
        && steamId.All(character => character is >= '0' and <= '9')
        && !string.IsNullOrWhiteSpace(overlayToken)
        && !overlayToken.Contains('\r')
        && !overlayToken.Contains('\n');

    private static bool TryParseQuery(
        string queryString,
        out Dictionary<string, string> query)
    {
        query = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in queryString.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = field.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            string name;
            string value;
            try
            {
                name = Uri.UnescapeDataString(field[..separator]);
                value = Uri.UnescapeDataString(field[(separator + 1)..]);
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (!query.TryAdd(name, value))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record IslePilotOverlayAuthResult(
    string SteamId,
    string OverlayToken);

public enum IslePilotOverlayAuthValidationState
{
    Valid,
    Invalid,
    Unavailable
}
