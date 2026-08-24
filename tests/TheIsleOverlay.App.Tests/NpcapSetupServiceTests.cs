using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapSetupServiceTests
{
    [Fact]
    public async Task ValidOfficialDownload_InstallsChecksAndDeletesTemporaryFile()
    {
        var bytes = Encoding.UTF8.GetBytes("signed-npcap-fixture");
        var directory = TestDirectory();
        string? installerPath = null;
        var refreshed = false;
        var progress = new InlineProgress<NpcapSetupProgress>();
        try
        {
            var service = CreateService(
                bytes,
                directory,
                Convert.ToHexString(SHA256.HashData(bytes)),
                signerVerifier: path => File.Exists(path),
                installerRunner: path =>
                {
                    installerPath = path;
                    Assert.True(File.Exists(path));
                    return Task.FromResult(0);
                },
                availabilityProbe: refresh =>
                {
                    refreshed = refresh;
                    return new NpcapAvailability(true);
                });

            var result = await service.InstallAsync(progress);

            Assert.Equal(NpcapSetupOutcome.Ready, result.Outcome);
            Assert.True(refreshed);
            Assert.NotNull(installerPath);
            Assert.False(File.Exists(installerPath));
            Assert.Equal(
                new[]
                {
                    NpcapSetupStage.Downloading,
                    NpcapSetupStage.Verifying,
                    NpcapSetupStage.Installing,
                    NpcapSetupStage.Checking
                },
                progress.Values.Select(value => value.Stage).Distinct());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task HashMismatch_RejectsDownloadBeforeSignerOrInstaller()
    {
        var directory = TestDirectory();
        var signerCalled = false;
        var installerCalled = false;
        try
        {
            var service = CreateService(
                Encoding.UTF8.GetBytes("tampered"),
                directory,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("official"))),
                signerVerifier: _ => signerCalled = true,
                installerRunner: _ =>
                {
                    installerCalled = true;
                    return Task.FromResult(0);
                });

            var result = await service.InstallAsync();

            Assert.Equal(NpcapSetupOutcome.Failed, result.Outcome);
            Assert.False(signerCalled);
            Assert.False(installerCalled);
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RedirectOutsideNpcapHost_IsRejected()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture");
        var directory = TestDirectory();
        try
        {
            var service = CreateService(
                bytes,
                directory,
                Convert.ToHexString(SHA256.HashData(bytes)),
                finalUri: new Uri("https://downloads.example.test/npcap.exe"));

            var result = await service.InstallAsync();

            Assert.Equal(NpcapSetupOutcome.Failed, result.Outcome);
            Assert.Contains("trang chính thức", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Theory]
    [InlineData(1, NpcapSetupOutcome.Cancelled)]
    [InlineData(3010, NpcapSetupOutcome.RebootRequired)]
    [InlineData(350, NpcapSetupOutcome.RebootRequired)]
    [InlineData(1618, NpcapSetupOutcome.Failed)]
    [InlineData(1633, NpcapSetupOutcome.Failed)]
    public async Task InstallerExitCode_ProducesActionableOutcome(
        int exitCode,
        NpcapSetupOutcome expectedOutcome)
    {
        var bytes = Encoding.UTF8.GetBytes("fixture");
        var directory = TestDirectory();
        try
        {
            var service = CreateService(
                bytes,
                directory,
                Convert.ToHexString(SHA256.HashData(bytes)),
                installerRunner: _ => Task.FromResult(exitCode));

            var result = await service.InstallAsync();

            Assert.Equal(expectedOutcome, result.Outcome);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void SetupModal_PresentsOneClickOfficialInstallInsteadOfManualDownload()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "NpcapRequiredWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        var allCopy = string.Join(
            " ",
            document.Descendants().SelectMany(element => new[]
            {
                (string?)element.Attribute("Text"),
                (string?)element.Attribute("Content")
            }));

        Assert.Equal("TẢI & CÀI NPCAP  →", (string?)Control("InstallButton").Attribute("Content"));
        Assert.Contains("NPCAP.COM", allCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NMAP SOFTWARE LLC", allCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tự tiếp tục", allCopy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MỞ TRANG TẢI", allCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https", NpcapSetupService.OfficialInstallerUri.Scheme);
        Assert.Equal("npcap.com", NpcapSetupService.OfficialInstallerUri.Host);
    }

    private static NpcapSetupService CreateService(
        byte[] bytes,
        string directory,
        string expectedSha256,
        Func<string, bool>? signerVerifier = null,
        Func<string, Task<int>>? installerRunner = null,
        Func<bool, NpcapAvailability>? availabilityProbe = null,
        Uri? finalUri = null)
    {
        var client = new HttpClient(new FixtureHandler(bytes, finalUri));
        return new NpcapSetupService(
            client,
            signerVerifier ?? (_ => true),
            installerRunner ?? (_ => Task.FromResult(0)),
            availabilityProbe ?? (_ => new NpcapAvailability(false)),
            directory,
            expectedSha256);
    }

    private static string TestDirectory() => Path.Combine(
        Path.GetTempPath(),
        "IsleLiveMap.Tests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixtureHandler(byte[] bytes, Uri? finalUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var responseRequest = new HttpRequestMessage(
                HttpMethod.Get,
                finalUri ?? NpcapSetupService.OfficialInstallerUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
                RequestMessage = responseRequest
            });
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
