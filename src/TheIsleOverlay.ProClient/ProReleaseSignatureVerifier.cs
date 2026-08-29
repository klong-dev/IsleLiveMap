using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TheIsleOverlay.ProClient;

public sealed class ProReleaseSignatureVerifier : IDisposable
{
    private readonly RSA _rsa;

    public ProReleaseSignatureVerifier(string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        _rsa = RSA.Create();
        _rsa.ImportFromPem(publicKeyPem);
    }

    public bool Verify(ProReleaseManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = CreateCanonicalPayload(manifest);
        return _rsa.VerifyData(
            payload,
            signature,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
    }

    public void Dispose() => _rsa.Dispose();

    public static byte[] CreateCanonicalPayload(ProReleaseManifest manifest)
    {
        var value = string.Join('\n',
            "isle-pro-release-v1",
            manifest.Version,
            manifest.IpcApiMajor.ToString(CultureInfo.InvariantCulture),
            manifest.MinHostVersion,
            manifest.MaxHostVersionExclusive,
            manifest.Sha256,
            manifest.Size.ToString(CultureInfo.InvariantCulture),
            string.Empty);
        return Encoding.UTF8.GetBytes(value);
    }
}
