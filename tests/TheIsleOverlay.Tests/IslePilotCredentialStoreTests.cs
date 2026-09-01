using System.Text;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IsleLiveMap.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsForTheCurrentWindowsUser()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        var expected = new IslePilotOverlayAuthResult(
            "76561198000000000",
            "header.payload.signature",
            "signed-player-cookie");

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Save_DoesNotWriteTheTokenOrSteamIdInPlaintext()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        const string steamId = "76561198000000000";
        const string token = "plain-text-token-must-not-leak";
        const string playerCookie = "plain-text-cookie-must-not-leak";

        await store.SaveAsync(new IslePilotOverlayAuthResult(steamId, token, playerCookie));
        var stored = await File.ReadAllBytesAsync(path);

        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(token)));
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.Unicode.GetBytes(token)));
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(steamId)));
        Assert.Equal(-1, stored.AsSpan().IndexOf(Encoding.UTF8.GetBytes(playerCookie)));
    }

    [Fact]
    public async Task Clear_RemovesTheSavedCredential()
    {
        var path = Path.Combine(_directory, "islepilot.credential");
        var store = new IslePilotCredentialStore(path);
        await store.SaveAsync(new IslePilotOverlayAuthResult(
            "76561198000000000",
            "secret"));

        store.Clear();

        Assert.False(File.Exists(path));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Load_ReturnsNullForCorruptData()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "islepilot.credential");
        await File.WriteAllBytesAsync(path, [0x49, 0x4C, 0x4D, 0x31, 0x01]);
        var store = new IslePilotCredentialStore(path);

        Assert.Null(await store.LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
