using System.Security.Cryptography;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class ProReleaseSignatureVerifierTests
{
    [Fact]
    public void Verify_AcceptsCanonicalSignatureAndRejectsMutation()
    {
        using var key = RSA.Create(2048);
        var unsigned = Manifest(signature: string.Empty);
        var signature = Convert.ToBase64String(key.SignData(
            ProReleaseSignatureVerifier.CreateCanonicalPayload(unsigned),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        using var verifier = new ProReleaseSignatureVerifier(key.ExportSubjectPublicKeyInfoPem());

        Assert.True(verifier.Verify(unsigned with { Signature = signature }));
        Assert.False(verifier.Verify(unsigned with
        {
            Version = "0.1.1",
            Signature = signature
        }));
    }

    private static ProReleaseManifest Manifest(string signature) => new(
        "0.1.0",
        1,
        "1.4.0",
        "2.0.0",
        123,
        new string('a', 64),
        signature,
        "https://isle.klong.dev/download",
        DateTimeOffset.Parse("2026-08-26T00:00:00Z"));
}
