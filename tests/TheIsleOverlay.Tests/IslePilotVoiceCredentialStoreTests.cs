using System.Text;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotVoiceCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IsleLiveMap.Voice.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsWithoutWritingIdentityInPlaintext()
    {
        var path = Path.Combine(_directory, "voice.credential");
        var store = new IslePilotVoiceCredentialStore(path);
        var expected = new IslePilotVoiceAuthResult(
            "76561198000000000",
            "voice-token-must-remain-private");

        await store.SaveAsync(expected);
        var bytes = await File.ReadAllBytesAsync(path);
        var actual = await store.LoadAsync();

        Assert.Equal(expected, actual);
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(expected.SteamId64)));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(expected.AccessToken)));
    }

    [Fact]
    public async Task Load_ReturnsNullForCorruptOrWrongPurposeData()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "voice.credential");
        await File.WriteAllBytesAsync(path, "ILM1not-a-voice-credential"u8.ToArray());

        Assert.Null(await new IslePilotVoiceCredentialStore(path).LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
