using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class ProReleaseManagerTests
{
    [Fact]
    public async Task EnsureLatestAsync_InstallsSignedCompatibleArtifact()
    {
        var archive = CreateArchive(("IsleLiveMap.Pro.Agent.exe", "agent-binary"));
        using var key = RSA.Create(2048);
        var manifest = SignManifest(key, archive);
        using var httpClient = new HttpClient(new ReleaseHandler(manifest, archive));
        var api = new ProApiClient(httpClient, new Uri("https://isle.test/"));
        var root = TemporaryDirectory();

        try
        {
            using var manager = new ProReleaseManager(api, root, key.ExportSubjectPublicKeyInfoPem());
            var installation = await manager.EnsureLatestAsync(
                "1.4.0",
                "access-token",
                TestContext.Current.CancellationToken);

            Assert.Equal("0.1.0", installation.Version);
            Assert.True(File.Exists(installation.ExecutablePath));
            Assert.Equal("agent-binary", await File.ReadAllTextAsync(
                installation.ExecutablePath,
                TestContext.Current.CancellationToken));
            Assert.NotNull(await manager.LoadInstalledAsync(
                "1.9.9",
                TestContext.Current.CancellationToken));
            Assert.Null(await manager.LoadInstalledAsync(
                "2.0.0",
                TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnsureLatestAsync_RejectsSignedArchiveWithTraversalPath()
    {
        var archive = CreateArchive(
            ("IsleLiveMap.Pro.Agent.exe", "agent-binary"),
            ("../escaped.txt", "must-not-extract"));
        using var key = RSA.Create(2048);
        var manifest = SignManifest(key, archive);
        using var httpClient = new HttpClient(new ReleaseHandler(manifest, archive));
        var api = new ProApiClient(httpClient, new Uri("https://isle.test/"));
        var root = TemporaryDirectory();

        try
        {
            using var manager = new ProReleaseManager(api, root, key.ExportSubjectPublicKeyInfoPem());

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await manager.EnsureLatestAsync(
                    "1.4.0",
                    "access-token",
                    TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(root, "versions", "escaped.txt")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static byte[] CreateArchive(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        }

        return stream.ToArray();
    }

    private static ProReleaseManifest SignManifest(RSA key, byte[] archive)
    {
        var unsigned = new ProReleaseManifest(
            "0.1.0",
            ProReleaseManager.IpcApiMajor,
            "1.4.0",
            "2.0.0",
            archive.Length,
            Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant(),
            string.Empty,
            "https://isle.test/artifact.zip",
            DateTimeOffset.Parse("2026-08-26T00:00:00Z"));
        var signature = Convert.ToBase64String(key.SignData(
            ProReleaseSignatureVerifier.CreateCanonicalPayload(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        return unsigned with { Signature = signature };
    }

    private static string TemporaryDirectory() => Path.Combine(
        Path.GetTempPath(),
        $"isle-pro-release-{Guid.NewGuid():N}");

    private sealed class ReleaseHandler(
        ProReleaseManifest manifest,
        byte[] artifact) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/v1/pro/manifest")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(manifest)
                });
            }

            if (request.RequestUri?.AbsolutePath == "/artifact.zip")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(artifact)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
