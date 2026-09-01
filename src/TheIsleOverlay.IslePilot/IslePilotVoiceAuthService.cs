using System.Net;
using System.Text.Json;

namespace TheIsleOverlay.IslePilot;

public static class IslePilotVoiceAuthService
{
    public const string CallbackScheme = "isle-voip";

    public static Uri LoginUri { get; } = new(
        "https://voip.islepilot.eu/api/auth/steam?client=app");

    private static Uri TicketUri(string token) => new(
        $"https://voip.islepilot.eu/api/voice/ticket?token={Uri.EscapeDataString(token)}");

    public static async Task<IslePilotVoiceAuthValidationState> ValidateAsync(
        HttpClient httpClient,
        IslePilotVoiceAuthResult credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentials);
        if (!IsValidCredentials(credentials.SteamId64, credentials.AccessToken))
        {
            throw new ArgumentException("The IsleVOIP credentials are invalid.", nameof(credentials));
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                TicketUri(credentials.AccessToken));
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return IslePilotVoiceAuthValidationState.Invalid;
            }

            if (!response.IsSuccessStatusCode)
            {
                return IslePilotVoiceAuthValidationState.Unavailable;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("ticket", out var ticket)
                   && ticket.ValueKind == JsonValueKind.String
                   && !string.IsNullOrWhiteSpace(ticket.GetString())
                   && root.TryGetProperty("steamId64", out var steamId)
                   && steamId.ValueKind == JsonValueKind.String
                   && string.Equals(
                       steamId.GetString(),
                       credentials.SteamId64,
                       StringComparison.Ordinal)
                ? IslePilotVoiceAuthValidationState.Valid
                : IslePilotVoiceAuthValidationState.Invalid;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                           or IOException
                                           or JsonException
                                           or OperationCanceledException)
        {
            return IslePilotVoiceAuthValidationState.Unavailable;
        }
    }

    public static bool TryParseCallback(
        string? callback,
        out IslePilotVoiceAuthResult? result)
    {
        result = null;
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, CallbackScheme, StringComparison.OrdinalIgnoreCase)
            || !TryParseQuery(uri.Query, out var query)
            || !query.TryGetValue("sid", out var steamId64)
            || !query.TryGetValue("token", out var accessToken)
            || !IsValidCredentials(steamId64, accessToken))
        {
            return false;
        }

        result = new IslePilotVoiceAuthResult(steamId64, accessToken);
        return true;
    }

    internal static bool IsValidCredentials(string? steamId64, string? accessToken) =>
        steamId64 is { Length: 17 }
        && steamId64.All(character => character is >= '0' and <= '9')
        && accessToken is { Length: > 0 and <= 16_384 }
        && !string.IsNullOrWhiteSpace(accessToken)
        && !accessToken.Contains('\r')
        && !accessToken.Contains('\n');

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

            try
            {
                var name = Uri.UnescapeDataString(field[..separator]);
                var value = Uri.UnescapeDataString(field[(separator + 1)..]);
                if (!query.TryAdd(name, value))
                {
                    return false;
                }
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record IslePilotVoiceAuthResult(
    string SteamId64,
    string AccessToken);

public enum IslePilotVoiceAuthValidationState
{
    Valid,
    Invalid,
    Unavailable
}
