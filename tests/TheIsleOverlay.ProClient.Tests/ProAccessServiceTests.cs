using System.Net;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class ProAccessServiceTests
{
    [Fact]
    public async Task InitializeAsync_RefreshUnauthorizedKeepsUsableOfflineProSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            $"isle-pro-access-service-{Guid.NewGuid():N}");
        var credentialPath = Path.Combine(directory, "pro-access.credential");
        var now = DateTimeOffset.UtcNow;
        var stored = new StoredProSession(
            "76561198000000000",
            new string('r', 64),
            now.AddDays(30),
            "header.payload.signature",
            now.AddMinutes(10),
            new ProEntitlement("pro", "active", null));

        try
        {
            var store = new ProCredentialStore(credentialPath);
            await store.SaveAsync(stored, TestContext.Current.CancellationToken);

            using var httpClient = new HttpClient(new UnauthorizedRefreshHandler());
            using var service = new ProAccessService(
                new ProClientOptions
                {
                    BaseUri = new Uri("https://isle.test/"),
                    CredentialPath = credentialPath,
                    InstallationRoot = Path.Combine(directory, "agent")
                },
                httpClient);

            var access = await service.InitializeAsync(
                "2.2.3",
                TestContext.Current.CancellationToken);

            Assert.True(access.IsAuthenticated);
            Assert.True(access.Entitlement.IsProAt(now));
            Assert.True(access.IsOffline);
            Assert.Equal("offline_agent_unavailable", access.StatusCode);
            Assert.True(File.Exists(credentialPath));
            Assert.NotNull(await store.LoadAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class UnauthorizedRefreshHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
}
