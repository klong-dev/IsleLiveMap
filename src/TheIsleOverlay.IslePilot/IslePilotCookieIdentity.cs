using System.Text;
using System.Text.Json;

namespace TheIsleOverlay.IslePilot;

public static class IslePilotCookieIdentity
{
    public static string? TryGetPersonaName(string cookie)
    {
        try
        {
            var token = cookie.Trim();
            if (token.StartsWith("islepilot_player=", StringComparison.OrdinalIgnoreCase))
            {
                token = token["islepilot_player=".Length..];
            }

            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return document.RootElement.TryGetProperty("personaName", out var name) ? name.GetString() : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }
}
