using System.Security.Cryptography;
using System.Text;

namespace TheIsleOverlay.IslePilot;

// Uses the same Windows DPAPI primitive, but a separate file and entropy.
// This credential is never an IslePilot/Steam token.
public sealed class PersonalMarkerCredentialStore(string path)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("IsleLiveMap.PersonalMarkers.v1");
    public string? Load()
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 4096) return null;
            var clear = WindowsDataProtection.Unprotect(File.ReadAllBytes(path), Entropy);
            try
            {
                var token = Encoding.UTF8.GetString(clear);
                return token.Length == 64 && token.All(Uri.IsHexDigit) ? token : null;
            }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException) { return null; }
    }
    public void Save(string token)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid marker token", nameof(token));
        var bytes = Encoding.UTF8.GetBytes(token);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, WindowsDataProtection.Protect(bytes, Entropy));
            File.Move(temp, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
