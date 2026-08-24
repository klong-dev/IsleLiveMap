using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.App;

public enum NpcapSetupStage
{
    Downloading,
    Verifying,
    Installing,
    Checking
}

public readonly record struct NpcapSetupProgress(
    NpcapSetupStage Stage,
    int? Percent,
    string Message);

public enum NpcapSetupOutcome
{
    Ready,
    Cancelled,
    RebootRequired,
    Failed
}

public sealed record NpcapSetupResult(NpcapSetupOutcome Outcome, string Message);

public interface INpcapSetupService
{
    Task<NpcapSetupResult> InstallAsync(
        IProgress<NpcapSetupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class NpcapSetupService : INpcapSetupService
{
    public const string InstallerVersion = "1.88";
    public const string OfficialInstallerSha256 =
        "A2F4EC1E5EA353FF67EFD24B2EBF081BA44532410FAE8D5E146AF0310AA4F56B";
    public static readonly Uri OfficialInstallerUri = new(
        $"https://npcap.com/dist/npcap-{InstallerVersion}.exe");

    private const long MaximumInstallerBytes = 8 * 1024 * 1024;
    private const string OfficialSignerName = "Nmap Software LLC";
    private const string OfficialSignerThumbprint = "0629C303220B256580AABA536A1A3C060B87E3A2";
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly HttpClient _httpClient;
    private readonly Func<string, bool> _signerVerifier;
    private readonly Func<string, Task<int>> _installerRunner;
    private readonly Func<bool, NpcapAvailability> _availabilityProbe;
    private readonly string _downloadDirectory;
    private readonly string _expectedSha256;

    public NpcapSetupService()
        : this(
            SharedHttpClient,
            HasOfficialSigner,
            RunInstallerAsync,
            NpcapAvailabilityProbe.Check,
            Path.Combine(Path.GetTempPath(), "IsleLiveMap", "Npcap"),
            OfficialInstallerSha256)
    {
    }

    internal NpcapSetupService(
        HttpClient httpClient,
        Func<string, bool> signerVerifier,
        Func<string, Task<int>> installerRunner,
        Func<bool, NpcapAvailability> availabilityProbe,
        string downloadDirectory,
        string expectedSha256)
    {
        _httpClient = httpClient;
        _signerVerifier = signerVerifier;
        _installerRunner = installerRunner;
        _availabilityProbe = availabilityProbe;
        _downloadDirectory = downloadDirectory;
        _expectedSha256 = expectedSha256;
    }

    public async Task<NpcapSetupResult> InstallAsync(
        IProgress<NpcapSetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var installerPath = Path.Combine(
            _downloadDirectory,
            $"npcap-{InstallerVersion}-{Guid.NewGuid():N}.exe");

        try
        {
            Directory.CreateDirectory(_downloadDirectory);
            await DownloadInstallerAsync(installerPath, progress, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new NpcapSetupProgress(
                NpcapSetupStage.Verifying,
                null,
                "ĐANG KIỂM TRA FILE VÀ CHỮ KÝ SỐ…"));

            if (!await HasExpectedHashAsync(installerPath, cancellationToken).ConfigureAwait(false)
                || !_signerVerifier(installerPath))
            {
                return new NpcapSetupResult(
                    NpcapSetupOutcome.Failed,
                    "File Npcap tải xuống không hợp lệ và đã bị xóa.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new NpcapSetupProgress(
                NpcapSetupStage.Installing,
                null,
                "HOÀN TẤT CÀI ĐẶT TRONG CỬA SỔ NPCAP…"));
            var exitCode = await _installerRunner(installerPath).ConfigureAwait(false);

            progress?.Report(new NpcapSetupProgress(
                NpcapSetupStage.Checking,
                null,
                "ĐANG KIỂM TRA NPCAP…"));
            var availability = _availabilityProbe(true);
            if (availability.IsAvailable)
            {
                return new NpcapSetupResult(
                    NpcapSetupOutcome.Ready,
                    "Npcap đã sẵn sàng. Đang tiếp tục mở map…");
            }

            return exitCode switch
            {
                1 => new NpcapSetupResult(
                    NpcapSetupOutcome.Cancelled,
                    "Bạn đã hủy cài đặt Npcap."),
                3010 or 350 => new NpcapSetupResult(
                    NpcapSetupOutcome.RebootRequired,
                    "Npcap đã cài nhưng Windows cần khởi động lại máy."),
                1618 => new NpcapSetupResult(
                    NpcapSetupOutcome.Failed,
                    "Windows đang chạy một trình cài đặt khác. Hoàn tất nó rồi thử lại."),
                1633 => new NpcapSetupResult(
                    NpcapSetupOutcome.Failed,
                    "Phiên bản Windows này không được Npcap hỗ trợ."),
                _ => new NpcapSetupResult(
                    NpcapSetupOutcome.Failed,
                    "Npcap chưa hoạt động. Hãy thử cài lại hoặc khởi động lại máy.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Cancelled,
                "Đã hủy tải Npcap.");
        }
        catch (OperationCanceledException)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Failed,
                "Tải Npcap quá thời gian chờ. Kiểm tra mạng rồi thử lại.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Cancelled,
                "Bạn đã từ chối quyền quản trị để cài Npcap.");
        }
        catch (HttpRequestException)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Failed,
                "Không tải được Npcap từ trang chính thức. Kiểm tra mạng rồi thử lại.");
        }
        catch (IOException)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Failed,
                "Windows không cho phép lưu hoặc mở bộ cài Npcap.");
        }
        catch (UnauthorizedAccessException)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Failed,
                "Windows từ chối quyền tạo bộ cài Npcap tạm thời.");
        }
        catch (CryptographicException)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Failed,
                "Không xác minh được chữ ký số của bộ cài Npcap.");
        }
        catch (InvalidOperationException)
        {
            return new NpcapSetupResult(
                NpcapSetupOutcome.Failed,
                "Windows không khởi chạy được bộ cài Npcap.");
        }
        finally
        {
            TryDeleteInstaller(installerPath);
        }
    }

    private async Task DownloadInstallerAsync(
        string installerPath,
        IProgress<NpcapSetupProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new NpcapSetupProgress(
            NpcapSetupStage.Downloading,
            0,
            $"ĐANG TẢI NPCAP {InstallerVersion} TỪ TRANG CHÍNH THỨC…"));

        using var response = await _httpClient.GetAsync(
                OfficialInstallerUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.IdnHost, "npcap.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Npcap download redirected outside the official host.");
        }

        var expectedLength = response.Content.Headers.ContentLength;
        if (expectedLength > MaximumInstallerBytes)
        {
            throw new HttpRequestException("Npcap installer exceeded the download size limit.");
        }

        await using var input = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            installerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        long received = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                received += read;
                if (received > MaximumInstallerBytes)
                {
                    throw new HttpRequestException("Npcap installer exceeded the download size limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                int? percent = expectedLength is > 0
                    ? (int)Math.Min(100, received * 100 / expectedLength.Value)
                    : null;
                progress?.Report(new NpcapSetupProgress(
                    NpcapSetupStage.Downloading,
                    percent,
                    $"ĐANG TẢI NPCAP {InstallerVersion} TỪ TRANG CHÍNH THỨC…"));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<bool> HasExpectedHashAsync(
        string installerPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            installerPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var expected = Convert.FromHexString(_expectedSha256);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool HasOfficialSigner(string installerPath)
    {
        using var certificate = X509Certificate.CreateFromSignedFile(installerPath);
        return certificate.Subject.Contains(
                   $"CN={OfficialSignerName}",
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(
                   certificate.GetCertHashString(),
                   OfficialSignerThumbprint,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RunInstallerAsync(string installerPath)
    {
        using var process = Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Verb = "runas"
        }) ?? throw new InvalidOperationException("Windows did not start the Npcap installer.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    private static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    private static void TryDeleteInstaller(string installerPath)
    {
        try
        {
            if (File.Exists(installerPath))
            {
                File.Delete(installerPath);
            }
        }
        catch (IOException)
        {
            // The signed installer may still be releasing its file handle.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows will eventually clear the user's temporary directory.
        }
    }
}
