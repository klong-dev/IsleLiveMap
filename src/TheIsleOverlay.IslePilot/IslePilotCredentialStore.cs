using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotCredentialStore
{
    private const int MaximumCredentialBytes = 1024 * 1024;

    private static readonly byte[] FileHeader = "ILM1"u8.ToArray();
    private static readonly byte[] OptionalEntropy = Encoding.UTF8.GetBytes(
        "KLongDev.IsleLiveMap.IslePilotOverlay.v1");

    private readonly string _credentialPath;

    public IslePilotCredentialStore(string credentialPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialPath);
        _credentialPath = Path.GetFullPath(credentialPath);
    }

    public async Task SaveAsync(
        IslePilotOverlayAuthResult credentials,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (!IslePilotOverlayAuthService.IsValidCredentials(
                credentials.SteamId,
                credentials.OverlayToken))
        {
            throw new ArgumentException("The IslePilot credentials are invalid.", nameof(credentials));
        }

        if (!IsValidOptionalCookie(credentials.PlayerCookie))
        {
            throw new ArgumentException("The IslePilot player cookie is invalid.", nameof(credentials));
        }

        var cleartext = JsonSerializer.SerializeToUtf8Bytes(new StoredCredential(
            credentials.SteamId,
            credentials.OverlayToken,
            credentials.PlayerCookie));
        byte[]? protectedData = null;
        try
        {
            protectedData = WindowsDataProtection.Protect(cleartext, OptionalEntropy);
            var fileData = new byte[FileHeader.Length + protectedData.Length];
            FileHeader.CopyTo(fileData, 0);
            protectedData.CopyTo(fileData, FileHeader.Length);

            var directory = Path.GetDirectoryName(_credentialPath)
                ?? throw new InvalidOperationException("The credential path has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_credentialPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, fileData, cancellationToken);
                File.Move(temporaryPath, _credentialPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                CryptographicOperations.ZeroMemory(fileData);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
            if (protectedData is not null)
            {
                CryptographicOperations.ZeroMemory(protectedData);
            }
        }
    }

    public async Task<IslePilotOverlayAuthResult?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        byte[] fileData;
        try
        {
            var file = new FileInfo(_credentialPath);
            if (!file.Exists
                || file.Length <= FileHeader.Length
                || file.Length > MaximumCredentialBytes)
            {
                return null;
            }

            fileData = await File.ReadAllBytesAsync(_credentialPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        byte[]? cleartext = null;
        try
        {
            if (!fileData.AsSpan(0, FileHeader.Length).SequenceEqual(FileHeader))
            {
                return null;
            }

            cleartext = WindowsDataProtection.Unprotect(
                fileData.AsSpan(FileHeader.Length),
                OptionalEntropy);
            var stored = JsonSerializer.Deserialize<StoredCredential>(cleartext);
            if (stored is null
                || !IslePilotOverlayAuthService.IsValidCredentials(
                    stored.SteamId,
                    stored.OverlayToken))
            {
                return null;
            }

            if (!IsValidOptionalCookie(stored.PlayerCookie))
            {
                return null;
            }

            return new IslePilotOverlayAuthResult(
                stored.SteamId,
                stored.OverlayToken,
                stored.PlayerCookie);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileData);
            if (cleartext is not null)
            {
                CryptographicOperations.ZeroMemory(cleartext);
            }
        }
    }

    public void Clear()
    {
        if (File.Exists(_credentialPath))
        {
            File.Delete(_credentialPath);
        }
    }

    private sealed record StoredCredential(
        string SteamId,
        string OverlayToken,
        string? PlayerCookie = null);

    private static bool IsValidOptionalCookie(string? value) =>
        value is null
        || value.Length is > 0 and <= 16_384
        && !value.Contains('\r')
        && !value.Contains('\n');
}

internal static class WindowsDataProtection
{
    private const uint CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(
        ReadOnlySpan<byte> cleartext,
        ReadOnlySpan<byte> optionalEntropy) =>
        Transform(cleartext, optionalEntropy, protect: true);

    public static byte[] Unprotect(
        ReadOnlySpan<byte> protectedData,
        ReadOnlySpan<byte> optionalEntropy) =>
        Transform(protectedData, optionalEntropy, protect: false);

    private static byte[] Transform(
        ReadOnlySpan<byte> input,
        ReadOnlySpan<byte> optionalEntropy,
        bool protect)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows DPAPI is required for IslePilot credentials.");
        }

        var inputBytes = input.ToArray();
        var entropyBytes = optionalEntropy.ToArray();
        var inputBlob = AllocateBlob(inputBytes);
        var entropyBlob = AllocateBlob(entropyBytes);
        DataBlob outputBlob = default;
        IntPtr description = IntPtr.Zero;

        try
        {
            var succeeded = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    out description,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);

            if (!succeeded)
            {
                var error = Marshal.GetLastWin32Error();
                throw new CryptographicException(new Win32Exception(error).Message);
            }

            if (outputBlob.Data == IntPtr.Zero || outputBlob.Length <= 0)
            {
                throw new CryptographicException("Windows DPAPI returned an empty result.");
            }

            var result = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(entropyBytes);
            ZeroAndFreeHGlobal(ref inputBlob);
            ZeroAndFreeHGlobal(ref entropyBlob);
            ZeroAndLocalFree(ref outputBlob);
            if (description != IntPtr.Zero)
            {
                LocalFree(description);
            }
        }
    }

    private static DataBlob AllocateBlob(byte[] data)
    {
        var blob = new DataBlob
        {
            Length = data.Length,
            Data = Marshal.AllocHGlobal(data.Length)
        };
        Marshal.Copy(data, 0, blob.Data, data.Length);
        return blob;
    }

    private static void ZeroAndFreeHGlobal(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    private static void ZeroAndLocalFree(ref DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        LocalFree(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        uint flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        out IntPtr dataDescription,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStructure,
        uint flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
