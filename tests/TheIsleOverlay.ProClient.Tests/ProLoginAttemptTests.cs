using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class ProLoginAttemptTests
{
    [Fact]
    public void TryComplete_AcceptsMatchingOneTimeCallback()
    {
        var attempt = ProLoginAttempt.Create(new Uri("https://isle.klong.dev/"));
        var state = ReadQuery(attempt.LoginUri)["state"];
        var callback = $"islelivemap://auth/callback?code={new string('a', 43)}&state={state}";

        Assert.True(attempt.TryComplete(callback, out var code));
        Assert.Equal(new string('a', 43), code);
        Assert.False(attempt.TryComplete(callback, out _));
    }

    [Fact]
    public void TryComplete_RejectsMismatchedStateAndOrigin()
    {
        var attempt = ProLoginAttempt.Create(new Uri("https://isle.klong.dev/"));
        var code = new string('a', 43);

        Assert.False(attempt.TryComplete(
            $"islelivemap://auth/callback?code={code}&state={new string('b', 43)}",
            out _));
        Assert.False(attempt.TryComplete(
            $"https://evil.example/callback?code={code}&state={new string('b', 43)}",
            out _));
    }

    private static Dictionary<string, string> ReadQuery(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(
                item => Uri.UnescapeDataString(item[0]),
                item => Uri.UnescapeDataString(item[1]),
                StringComparer.Ordinal);
}
