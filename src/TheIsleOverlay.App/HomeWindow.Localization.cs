using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Media;
using TheIsleOverlay.Localization;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private bool _localizationBusy;

    private void InitializeLocalizationPanel()
    {
        try
        {
            var active = File.Exists(GameLanguageSettings.DefaultConfigPath)
                ? GameLanguageSettings.Read(File.ReadAllText(GameLanguageSettings.DefaultConfigPath))
                : GameLocale.English;
            ApplyLocalizationState(active, "Đổi ngôn ngữ khi The Isle đã tắt. Resource Việt được xác minh trước khi cài.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LocalizationStatusLabel.Text = $"Chưa đọc được cấu hình ngôn ngữ: {exception.Message}";
            LocalizationTabStatusLabel.Text = LocalizationStatusLabel.Text;
        }
    }

    private async void EnglishLocaleButton_Click(object sender, RoutedEventArgs e) =>
        await ChangeGameLocaleAsync(GameLocale.English);

    private async void VietnameseLocaleButton_Click(object sender, RoutedEventArgs e) =>
        await ChangeGameLocaleAsync(GameLocale.Vietnamese);

    private async Task ChangeGameLocaleAsync(GameLocale locale)
    {
        if (_localizationBusy)
        {
            return;
        }

        _localizationBusy = true;
        EnglishLocaleButton.IsEnabled = false;
        VietnameseLocaleButton.IsEnabled = false;
        LocalizationStatusLabel.Foreground = (Brush)FindResource("HomeMuted");
        LocalizationStatusLabel.Text = locale == GameLocale.Vietnamese
            ? "Đang xác minh build, tải và cài resource Tiếng Việt…"
            : "Đang chuyển game về English…";
        LocalizationTabStatusLabel.Text = LocalizationStatusLabel.Text;
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var options = new LocalizationOptions();
            var coordinator = new LocalizationCoordinator(
                new TranslationDownloadClient(httpClient, options),
                new TranslationPackageInstaller(options));
            await coordinator.ApplyLocaleAsync(locale, waitForGameExit: false, _shutdown.Token);
            ApplyLocalizationState(
                locale,
                locale == GameLocale.Vietnamese
                    ? "Đã chọn Tiếng Việt. Mở lại game để áp dụng."
                    : "Đã chọn English. Mở lại game để áp dụng.");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LocalizationStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x8D, 0x7C));
            LocalizationStatusLabel.Text = FriendlyLocalizationError(exception);
            LocalizationTabStatusLabel.Text = LocalizationStatusLabel.Text;
        }
        finally
        {
            _localizationBusy = false;
            EnglishLocaleButton.IsEnabled = true;
            VietnameseLocaleButton.IsEnabled = true;
        }
    }

    private void ApplyLocalizationState(GameLocale locale, string detail)
    {
        var activeBrush = (Brush)FindResource("HomeAccent");
        var inactiveBrush = (Brush)FindResource("HomeLineStrong");
        EnglishLocaleButton.BorderBrush = locale == GameLocale.English ? activeBrush : inactiveBrush;
        VietnameseLocaleButton.BorderBrush = locale == GameLocale.Vietnamese ? activeBrush : inactiveBrush;
        EnglishLocaleButton.Opacity = locale == GameLocale.English ? 1d : 0.72d;
        VietnameseLocaleButton.Opacity = locale == GameLocale.Vietnamese ? 1d : 0.72d;
        LocalizationStatusLabel.Foreground = (Brush)FindResource("HomeMuted");
        LocalizationStatusLabel.Text = $"ĐANG DÙNG: {(locale == GameLocale.Vietnamese ? "TIẾNG VIỆT" : "ENGLISH")} · {detail}";
        LocalizationTabStatusLabel.Text = LocalizationStatusLabel.Text;
    }

    private static string FriendlyLocalizationError(Exception exception) => exception switch
    {
        InvalidOperationException when exception.Message.Contains("tắt The Isle", StringComparison.OrdinalIgnoreCase) =>
            "Hãy tắt The Isle và cửa sổ khởi động Steam, sau đó chọn lại ngôn ngữ.",
        DirectoryNotFoundException => "Không tự tìm thấy thư mục The Isle trong Steam. Hãy mở Steam một lần rồi thử lại; nếu game nằm ở thư viện phụ, ứng dụng sẽ tự quét các thư viện Steam đã đăng ký.",
        HttpRequestException => "Chưa kết nối được máy chủ Việt hóa. Không có file game nào bị thay đổi.",
        InvalidDataException => $"Gói Việt hóa không vượt qua xác minh: {exception.Message} Không có file game nào bị thay đổi.",
        _ => $"Không thể đổi ngôn ngữ: {exception.Message}"
    };

    private void MutationGuideButton_Click(object sender, RoutedEventArgs e)
    {
        var locale = File.Exists(GameLanguageSettings.DefaultConfigPath)
            ? GameLanguageSettings.Read(File.ReadAllText(GameLanguageSettings.DefaultConfigPath))
            : GameLocale.English;
        var shortcut = new ShortcutSettingsStore().Load().MutationGuide;
        var guide = new MutationGuideWindow(
            vietnamesePrimary: locale == GameLocale.Vietnamese,
            shortcutDisplay: shortcut)
        {
            Owner = this
        };
        guide.ShowDialog();
    }
}
