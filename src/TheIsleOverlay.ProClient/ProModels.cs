using System.Net;

namespace TheIsleOverlay.ProClient;

public sealed record ProEntitlement(
    string Tier,
    string Status,
    DateTimeOffset? ExpiresAt)
{
    public bool IsPro =>
        string.Equals(Tier, "pro", StringComparison.Ordinal) &&
        string.Equals(Status, "active", StringComparison.Ordinal);
}

public sealed record ProTokenResponse(
    string TokenType,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    string? OfflineLicenseToken,
    DateTimeOffset? OfflineLicenseExpiresAt,
    ProEntitlement Entitlement);

public sealed record ProEntitlementResponse(
    string SteamId64,
    ProEntitlement Entitlement,
    string? OfflineLicenseToken,
    DateTimeOffset? OfflineLicenseExpiresAt);

public sealed record ProReleaseManifest(
    string Version,
    int IpcApiMajor,
    string MinHostVersion,
    string MaxHostVersionExclusive,
    long Size,
    string Sha256,
    string Signature,
    string DownloadUrl,
    DateTimeOffset PublishedAt);

public sealed class ProApiException(
    string message,
    HttpStatusCode? statusCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record ProAccessSnapshot(
    string? SteamId64,
    ProEntitlement Entitlement,
    bool IsOffline,
    bool AgentReady,
    string? AgentVersion,
    DateTimeOffset? OfflineLicenseExpiresAt,
    string? StatusCode)
{
    public static ProAccessSnapshot SignedOut { get; } = new(
        null,
        new ProEntitlement("free", "signed_out", null),
        false,
        false,
        null,
        null,
        "signed_out");

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(SteamId64);

    public bool IsPro => Entitlement.IsPro;
}

public sealed record ProAgentInstallation(
    string Version,
    int IpcApiMajor,
    string MinHostVersion,
    string MaxHostVersionExclusive,
    string ExecutablePath,
    string ArtifactSha256,
    string ArtifactSignature);
