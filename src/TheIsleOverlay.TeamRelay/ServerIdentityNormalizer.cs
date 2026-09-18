using System.Net;

namespace TheIsleOverlay.TeamRelay;

/// <summary>Compares endpoint identity before falling back to display names.</summary>
public static class ServerIdentityNormalizer
{
    public static string? Normalize(string? endpoint, string? serverName = null)
    {
        var normalizedEndpoint = NormalizeEndpoint(endpoint);
        return normalizedEndpoint ?? NormalizeName(serverName);
    }

    public static bool AreSame(string? leftEndpoint, string? leftName, string? rightEndpoint, string? rightName)
    {
        var left = NormalizeEndpoint(leftEndpoint);
        var right = NormalizeEndpoint(rightEndpoint);
        if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        return string.Equals(NormalizeName(leftName ?? leftEndpoint), NormalizeName(rightName ?? rightEndpoint), StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(NormalizeName(leftName ?? leftEndpoint));
    }

    public static string? NormalizeEndpoint(string? value)
    {
        var text = Clean(value);
        if (text is null) return null;
        text = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        if (IPAddress.TryParse(text.Trim('[', ']'), out var addressOnly))
            return FormatEndpoint(addressOnly, 7777);
        if (Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : $"udp://{text}", UriKind.Absolute, out var uri)
            && uri.Host.Length > 0)
        {
            var host = uri.Host.Trim('[', ']');
            var port = uri.Port > 0 ? uri.Port : 7777;
            if (IPAddress.TryParse(host, out var address))
                return FormatEndpoint(address, port);
            host = host.TrimEnd('.').ToLowerInvariant();
            return host.Length == 0 ? null : $"{host}:{port}";
        }
        return null;
    }

    private static string FormatEndpoint(IPAddress address, int port) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address.ToString().ToLowerInvariant()}]:{port}"
            : $"{address.ToString().ToLowerInvariant()}:{port}";

    public static string? NormalizeName(string? value)
    {
        var text = Clean(value);
        return text is null ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var chars = value.Where(character => !char.IsControl(character)).ToArray();
        var text = new string(chars).Trim();
        return text.Length == 0 || text.Length > 192 ? null : text;
    }
}
