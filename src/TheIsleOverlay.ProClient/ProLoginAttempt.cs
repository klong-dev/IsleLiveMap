using System.Security.Cryptography;
using System.Text;

namespace TheIsleOverlay.ProClient;

public sealed class ProLoginAttempt
{
    public const string CallbackScheme = "islelivemap";

    private readonly string _state;
    private int _completed;

    private ProLoginAttempt(Uri loginUri, string state, string codeVerifier)
    {
        LoginUri = loginUri;
        _state = state;
        CodeVerifier = codeVerifier;
    }

    public Uri LoginUri { get; }

    internal string CodeVerifier { get; }

    public static ProLoginAttempt Create(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The backend base URI must be absolute.", nameof(baseUri));
        }

        var state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var relative = "auth/steam/login" +
                       $"?state={Uri.EscapeDataString(state)}" +
                       $"&code_challenge={Uri.EscapeDataString(challenge)}";
        return new ProLoginAttempt(new Uri(baseUri, relative), state, verifier);
    }

    internal bool TryComplete(string? callback, out string authorizationCode)
    {
        authorizationCode = string.Empty;
        if (Volatile.Read(ref _completed) != 0 ||
            !Uri.TryCreate(callback, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, CallbackScheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "auth", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.AbsolutePath, "/callback", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("state", out var returnedState) ||
            !FixedTimeEquals(_state, returnedState) ||
            !query.TryGetValue("code", out var code) ||
            !IsBase64Url(code, 32, 160) ||
            Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return false;
        }

        authorizationCode = code;
        return true;
    }

    public static bool IsCallback(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) &&
        string.Equals(parsed.Scheme, CallbackScheme, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(item[..separator].Replace('+', ' '));
            var value = Uri.UnescapeDataString(item[(separator + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }

        return result;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var actualBytes = Encoding.ASCII.GetBytes(actual);
        try
        {
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
        }
    }

    private static bool IsBase64Url(string value, int minimumLength, int maximumLength) =>
        value.Length >= minimumLength &&
        value.Length <= maximumLength &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
