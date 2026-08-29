using System.Reflection;
using System.Text;

namespace TheIsleOverlay.ProClient;

public static class EmbeddedProUpdatePublicKey
{
    private const string ResourceName = "TheIsleOverlay.ProClient.pro-update-public-key.pem";

    public static string Load()
    {
        using var stream = typeof(EmbeddedProUpdatePublicKey).Assembly
                               .GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException("The Pro update public key is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
