using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TheIsleOverlay.EraGaming;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Pandora;

namespace TheIsleOverlay.App;

public partial class HomeWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly GitHubUpdateService _updateService = new();
    private readonly ReleaseHighlightsStore _releaseHighlightsStore = new();
    private bool _connecting;

    public HomeWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeTeamPanel();

        if (string.Equals(
                Environment.GetEnvironmentVariable("ISLELIVEMAP_DEV_AUTO_CONNECT"),
                "1",
                StringComparison.Ordinal))
        {
            var overlay = new MainWindow();
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
            return;
        }

        if (App.CurrentApp.TryMarkDonatePromptShown())
        {
            var donateWindow = new DonateWindow
            {
                Owner = this
            };
            donateWindow.ShowDialog();
        }

        if (App.CurrentApp.TryMarkGuidePromptShown())
        {
            var guideWindow = new GuideWindow
            {
                Owner = this
            };
            guideWindow.ShowDialog();
        }

        var currentVersion = CurrentVersion();
        if (_releaseHighlightsStore.ShouldShow(currentVersion))
        {
            var highlightsWindow = new ReleaseHighlightsWindow(currentVersion)
            {
                Owner = this
            };
            highlightsWindow.ShowDialog();
            _releaseHighlightsStore.MarkShown(currentVersion);
        }

        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var result = await _updateService.PrepareUpdateAsync(
            progress => Dispatcher.Invoke(() => UpdateStatusLabel.Text = $"ĐANG TẢI BẢN MỚI · {progress}%"),
            _shutdown.Token);

        switch (result.State)
        {
            case UpdatePreparationState.Ready:
                UpdateStatusLabel.Text = $"BẢN {result.Version} ĐÃ SẴN SÀNG";
                ApplyUpdateButton.Visibility = Visibility.Visible;
                break;
            case UpdatePreparationState.DevelopmentBuild:
                UpdateStatusLabel.Text = $"BẢN CHẠY THỬ · v{CurrentVersion()}";
                break;
            case UpdatePreparationState.Unavailable:
                UpdateStatusLabel.Text = $"v{CurrentVersion()} · KHÔNG KIỂM TRA ĐƯỢC UPDATE";
                break;
            default:
                UpdateStatusLabel.Text = $"v{CurrentVersion()} · ĐÃ CẬP NHẬT";
                break;
        }
    }

    private async void SourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_connecting || sender is not Button { Tag: string sourceId })
        {
            return;
        }

        var source = TelemetrySourceDefinition.FromId(sourceId);
        if (source is null)
        {
            return;
        }

        _connecting = true;
        SetSourceButtonsEnabled(false);
        SourceStatusLabel.Text = $"ĐANG MỞ PHIÊN {source.DisplayName.ToUpperInvariant()}…";

        try
        {
            var loginWindow = new LoginWindow(source, (cookie, token) => ValidateSessionAsync(source, cookie, token))
            {
                Owner = this
            };
            if (loginWindow.ShowDialog() != true || string.IsNullOrWhiteSpace(loginWindow.CookieValue))
            {
                SourceStatusLabel.Text = "Chưa nhận được phiên. Đăng nhập trong cửa sổ vừa mở rồi bấm KIỂM TRA PHIÊN.";
                return;
            }

            var overlay = new MainWindow(source, loginWindow.CookieValue);
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không kết nối được: {FriendlyError(exception)}";
        }
        finally
        {
            _connecting = false;
            SetSourceButtonsEnabled(true);
        }
    }

    private static async Task<LoginSessionValidationState> ValidateSessionAsync(
        TelemetrySourceDefinition source,
        string cookie,
        CancellationToken cancellationToken)
    {
        try
        {
            using var validationClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var provider = source.CreateProvider(validationClient, cookie);
            var snapshot = await provider.GetSnapshotAsync(cancellationToken);
            return snapshot.Success
                ? LoginSessionValidationState.Valid
                : LoginSessionValidationState.Invalid;
        }
        catch (EraGamingAuthenticationException)
        {
            return LoginSessionValidationState.Invalid;
        }
        catch (IslePilotAuthenticationException)
        {
            return LoginSessionValidationState.Invalid;
        }
        catch (PandoraAuthenticationException)
        {
            return LoginSessionValidationState.Invalid;
        }
        catch
        {
            // A slow or temporarily unavailable API must not destroy a valid
            // browser session. The overlay will keep retrying telemetry.
            return LoginSessionValidationState.Unavailable;
        }
    }

    private void SetSourceButtonsEnabled(bool enabled)
    {
        EraSourceButton.IsEnabled = enabled;
        PandoraSourceButton.IsEnabled = enabled;
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException => "Máy chưa có Microsoft Edge WebView2 Runtime.",
        HttpRequestException => "website/API không phản hồi.",
        _ => exception.Message
    };

    private static string CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private static void OpenExternal(string url) => Process.Start(new ProcessStartInfo(url)
    {
        UseShellExecute = true
    });

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            OpenExternal(url);
        }
    }

    private void ApplyUpdateButton_Click(object sender, RoutedEventArgs e) => _updateService.ApplyAndRestart();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        DetachTeamPanel();
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
