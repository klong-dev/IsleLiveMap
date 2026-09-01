using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TheIsleOverlay.EraGaming;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.LocalTelemetry;
using TheIsleOverlay.Pandora;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

public partial class HomeWindow : Window
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly GitHubUpdateService _updateService = new();
    private bool _connecting;
    private MapLaunchGateState _mapLaunchGateState = MapLaunchGateState.Checking;
    private string _mapLaunchAccentColor = "#E7B74E";

    public HomeWindow()
    {
        InitializeComponent();
        InitializeProPresentation();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeTeamPanel();
        if (NpcapAvailabilityProbe.Check().IsAvailable)
        {
            // Begin outbound movement capture before update/donate/help modals
            // so direct GPS is ready as soon as the overlay opens.
            App.CurrentApp.EnsureLocalTelemetryWarmup();
        }

        if (string.Equals(
                Environment.GetEnvironmentVariable("ISLELIVEMAP_DEV_AUTO_CONNECT"),
                "1",
                StringComparison.Ordinal))
        {
            Environment.SetEnvironmentVariable("ISLELIVEMAP_DEV_AUTO_CONNECT", null);
            try
            {
                _proAccess = await EnsureProAccessInitializedAsync();
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Development auto-connect must still open local telemetry if
                // the license/update service is temporarily unavailable.
                _proAccess = ProAccessSnapshot.SignedOut with
                {
                    IsOffline = true,
                    StatusCode = "license_service_unavailable"
                };
            }

            var overlay = new MainWindow(
                new LocalPositionTelemetrySession(
                    localSource: App.CurrentApp.TakeLocalTelemetrySource(),
                    remotePlayerSource: CreateProPlayerSource()),
                "DIRECT",
                ProFeatureAccessGrant.FromSnapshot(_proAccess, DateTimeOffset.UtcNow));
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
            return;
        }

        // Start the network check before the modal sequence. ShowDialog keeps a
        // dispatcher frame alive, so update I/O continues while the user reads
        // donate/help content instead of adding another wait afterwards.
        var updateCheckTask = CheckForUpdatesAsync();

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

        var access = await EnsureProAccessInitializedAsync();
        var proPresentation = HomeProPresentationPolicy.Evaluate(
            access,
            DateTimeOffset.UtcNow);
        var highlightsStore = new ReleaseHighlightsPreferenceStore();
        if (highlightsStore.ShouldShow(ReleaseHighlightsWindow.ReleaseVersion))
        {
            var currentVersion = CurrentVersion();
            var highlightsWindow = new ReleaseHighlightsWindow(
                currentVersion,
                proPresentation.HasCurrentProAccess,
                highlightsStore)
            {
                Owner = this
            };
            highlightsWindow.ShowDialog();
        }

        try
        {
            await updateCheckTask;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        ApplyMapLaunchGate(
            MapLaunchGateState.Checking,
            "ĐANG KIỂM TRA CẬP NHẬT",
            "MỞ MAP sẽ tự mở khóa ngay khi kiểm tra hoàn tất.",
            "#E7B74E");
        ApplyUpdateButton.Visibility = Visibility.Collapsed;

        var result = await _updateService.PrepareUpdateAsync(
            progress => Dispatcher.Invoke(() =>
            {
                UpdateStatusLabel.Text = $"ĐANG TẢI BẢN MỚI · {progress}%";
                ApplyMapLaunchGate(
                    MapLaunchGateState.Checking,
                    $"ĐANG TẢI BẢN MỚI · {progress}%",
                    "Hoàn tất bản cập nhật trước khi mở map.",
                    "#E7B74E");
            }),
            _shutdown.Token);

        ApplyCompletedUpdateResult(result);
    }

    private void ApplyCompletedUpdateResult(UpdatePreparationResult result)
    {
        switch (result.State)
        {
            case UpdatePreparationState.Ready:
                UpdateStatusLabel.Text = $"BẢN {result.Version} ĐÃ SẴN SÀNG";
                ApplyUpdateButton.Visibility = Visibility.Visible;
                ApplyMapLaunchGate(
                    MapLaunchGatePolicy.FromUpdate(result.State),
                    $"CÓ BẢN {result.Version}",
                    "Bấm CẬP NHẬT & KHỞI ĐỘNG LẠI trước khi mở map.",
                    "#E7B74E");
                break;
            case UpdatePreparationState.DevelopmentBuild:
                UpdateStatusLabel.Text = $"BẢN CHẠY THỬ · v{CurrentVersion()}";
                ApplyMapLaunchGate(
                    MapLaunchGatePolicy.FromUpdate(result.State),
                    "BẢN CHẠY THỬ · MỞ MAP ĐÃ SẴN SÀNG",
                    "Inbound và outbound được đọc trực tiếp từ game trên mọi server.",
                    "#37D4C6");
                break;
            case UpdatePreparationState.Unavailable:
                UpdateStatusLabel.Text = $"v{CurrentVersion()} · KHÔNG KIỂM TRA ĐƯỢC UPDATE";
                ApplyMapLaunchGate(
                    MapLaunchGatePolicy.FromUpdate(result.State),
                    "KHÔNG KIỂM TRA ĐƯỢC CẬP NHẬT",
                    "Bạn vẫn có thể mở map; app sẽ thử kiểm tra lại ở lần khởi động sau.",
                    "#E7B74E");
                break;
            default:
                UpdateStatusLabel.Text = $"v{CurrentVersion()} · ĐÃ CẬP NHẬT";
                ApplyMapLaunchGate(
                    MapLaunchGatePolicy.FromUpdate(result.State),
                    "MỞ MAP ĐÃ SẴN SÀNG",
                    "Inbound và outbound được đọc trực tiếp từ game trên mọi server.",
                    "#37D4C6");
                break;
        }
    }

    private async void SourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMapLaunchAvailable()
            || _connecting
            || sender is not Button { Tag: string sourceId })
        {
            return;
        }

        var source = TelemetrySourceDefinition.FromId(sourceId);
        if (source is null)
        {
            return;
        }

        if (!EnsureLocalCaptureAvailable())
        {
            return;
        }

        _connecting = true;
        RefreshMapLaunchControls();
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

            var overlay = new MainWindow(
                source,
                loginWindow.CookieValue,
                CreateProPlayerSource(),
                App.CurrentApp.TakeLocalTelemetrySource(),
                ProFeatureAccessGrant.FromSnapshot(_proAccess, DateTimeOffset.UtcNow));
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
            RefreshMapLaunchControls();
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

    private bool EnsureMapLaunchAvailable()
    {
        if (MapLaunchGatePolicy.AllowsMap(_mapLaunchGateState))
        {
            return true;
        }

        SourceStatusLabel.Text = _mapLaunchGateState == MapLaunchGateState.UpdateRequired
            ? "Có bản mới đã tải xong. Hãy cập nhật và khởi động lại trước khi mở map."
            : "App đang kiểm tra cập nhật. MỞ MAP sẽ tự mở khóa ngay khi hoàn tất.";
        return false;
    }

    private bool EnsureLocalCaptureAvailable()
    {
        var availability = NpcapAvailabilityProbe.Check();
        if (availability.IsAvailable)
        {
            return true;
        }

        SourceStatusLabel.Text = availability.ErrorMessage
            ?? "Chưa thể đọc vị trí trực tiếp từ game.";
        var prompt = new NpcapRequiredWindow { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return false;
        }

        availability = NpcapAvailabilityProbe.Check(refresh: true);
        if (availability.IsAvailable)
        {
            App.CurrentApp.EnsureLocalTelemetryWarmup();
            SourceStatusLabel.Text = "Npcap đã sẵn sàng. Đang tiếp tục mở map…";
            return true;
        }

        SourceStatusLabel.Text = availability.ErrorMessage
            ?? "Npcap chưa hoạt động. Hãy khởi động lại Windows rồi thử lại.";
        return false;
    }

    private void ApplyMapLaunchGate(
        MapLaunchGateState state,
        string title,
        string detail,
        string accentColor)
    {
        _mapLaunchGateState = state;
        _mapLaunchAccentColor = accentColor;
        MapLaunchStateLabel.Text = title;
        MapLaunchStateDetail.Text = detail;
        ApplyMapLaunchAccent();
        RefreshMapLaunchControls();
    }

    private void ApplyMapLaunchAccent()
    {
        var accent = _premiumHomeTheme
            ? (Brush)FindResource("HomeAccent")
            : HomeBrush(_mapLaunchAccentColor);
        MapLaunchStateLabel.Foreground = accent;
        MapLaunchStateDot.Fill = accent;
        MapLaunchStateBar.Fill = accent;
    }

    private void RefreshMapLaunchControls()
    {
        var proPresentation = HomeProPresentationPolicy.Evaluate(
            _proAccess,
            DateTimeOffset.UtcNow);
        var enabled = MapLaunchGatePolicy.AllowsMap(_mapLaunchGateState)
                      && !_connecting;
        SteamLoginButton.IsEnabled = enabled
                                     && !_islePilotConnecting
                                     && _proAccessInitialized
                                     && !_proAccessLoading;
        LogoutSteamButton.IsEnabled = !_islePilotConnecting && _islePilotCredentials is not null;
        ProAccessButton.IsEnabled = !_proAccessLoading
                                    && !_connecting
                                    && !_islePilotConnecting
                                    && !proPresentation.HasCurrentProAccess;
        ProAccessButton.Opacity = 1d;
        ProAccessButton.Cursor = proPresentation.HasCurrentProAccess
            ? Cursors.Arrow
            : Cursors.Hand;
        LogoutProButton.IsEnabled = !_proAccessLoading;
        SteamLoginTitleLabel.Text = proPresentation.MapTitle;

        SteamLoginActionLabel.Text = _islePilotConnecting
            ? "ĐANG KẾT NỐI…"
            : !_proAccessInitialized || _proAccessLoading
                ? "ĐANG NẠP PRO…"
            : _mapLaunchGateState switch
        {
            MapLaunchGateState.Checking => "ĐỢI KIỂM TRA…",
            MapLaunchGateState.UpdateRequired => "CẬP NHẬT TRƯỚC",
            _ => _islePilotCredentials is null ? "ĐĂNG NHẬP  →" : proPresentation.MapAction
        };
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
        StopProPresentationMonitoring();
        _shutdown.Cancel();
        _proAccessService.Dispose();
        _shutdown.Dispose();
    }
}
