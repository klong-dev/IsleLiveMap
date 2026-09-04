using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TheIsleOverlay.Core;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private readonly ProAccessService _proAccessService = new();
    private ProAccessSnapshot _proAccess = ProAccessSnapshot.SignedOut;
    private bool _proAccessLoading;
    private bool _proAccessInitialized;
    private Task<ProAccessSnapshot>? _proAccessInitializationTask;
    private PrewarmedRemotePlayerTelemetrySource? _warmProTelemetry;
    private bool _premiumHomeTheme;
    private DispatcherTimer? _proExpiryTimer;

    private void InitializeProPresentation()
    {
        _proExpiryTimer = new DispatcherTimer(DispatcherPriority.Background);
        _proExpiryTimer.Tick += ProExpiryTimer_Tick;
        ApplyHomePresentationTheme(premium: false);
    }

    private void StopProPresentationMonitoring()
    {
        if (_proExpiryTimer is null)
        {
            return;
        }

        _proExpiryTimer.Stop();
        _proExpiryTimer.Tick -= ProExpiryTimer_Tick;
    }

    private async void ProAccessPanel_Loaded(object sender, RoutedEventArgs e)
    {
        await EnsureProAccessInitializedAsync();
    }

    private Task<ProAccessSnapshot> EnsureProAccessInitializedAsync()
    {
        _proAccessInitializationTask ??= InitializeProAccessAsync();
        return _proAccessInitializationTask;
    }

    private async Task<ProAccessSnapshot> InitializeProAccessAsync()
    {
        _proAccessInitialized = true;
        _proAccessLoading = true;
        ApplyProAccessState(ProAccessSnapshot.SignedOut with { StatusCode = "checking" });
        RefreshMapLaunchControls();
        try
        {
            _proAccess = await _proAccessService.InitializeAsync(
                CurrentVersion(),
                _shutdown.Token);
            ApplyProAccessState(_proAccess);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _proAccess = ProAccessSnapshot.SignedOut with
            {
                IsOffline = true,
                StatusCode = "license_service_unavailable"
            };
            ApplyProAccessState(_proAccess);
        }
        finally
        {
            _proAccessLoading = false;
            await RefreshProTelemetryWarmupAsync();
            RefreshMapLaunchControls();
        }

        return _proAccess;
    }

    private async void ProAccessButton_Click(object sender, RoutedEventArgs e)
    {
        var presentation = HomeProPresentationPolicy.Evaluate(
            _proAccess,
            DateTimeOffset.UtcNow);
        if (_proAccessLoading
            || _connecting
            || _islePilotConnecting
            || presentation.HasCurrentProAccess)
        {
            return;
        }

        var loginWindow = new ProSteamLoginWindow(_proAccessService, CurrentVersion())
        {
            Owner = this
        };
        if (loginWindow.ShowDialog() == true && loginWindow.Access is { } access)
        {
            _proAccess = access;
            ApplyProAccessState(access);
            await RefreshProTelemetryWarmupAsync();
            SourceStatusLabel.Text = access.IsPro
                ? access.AgentReady
                    ? "Steam đã xác minh. Player + AI Tracking Pro đã sẵn sàng."
                    : "Tài khoản có Pro nhưng chưa tải được agent tương thích."
                : "Steam đã xác minh nhưng tài khoản chưa có quyền Pro.";
        }

        RefreshMapLaunchControls();
    }

    private async void LogoutProButton_Click(object sender, RoutedEventArgs e)
    {
        if (_proAccessLoading)
        {
            return;
        }

        _proAccessLoading = true;
        RefreshMapLaunchControls();
        try
        {
            await StopProTelemetryWarmupAsync();
            await _proAccessService.LogoutAsync(_shutdown.Token);
            _islePilotVoiceCredentialStore.Clear();
            _proAccess = ProAccessSnapshot.SignedOut;
            ApplyProAccessState(_proAccess);
            SourceStatusLabel.Text = "Đã xóa phiên Isle Live Map Pro trên máy này.";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            _proAccessLoading = false;
            RefreshMapLaunchControls();
        }
    }

    private IRemotePlayerTelemetrySource? TakeProPlayerSource()
    {
        if (_warmProTelemetry is not null)
        {
            var source = _warmProTelemetry;
            _warmProTelemetry = null;
            return source;
        }

        return _proAccessService.CreateRemotePlayerSource();
    }

    private Task RefreshProTelemetryWarmupAsync()
    {
        var presentation = HomeProPresentationPolicy.Evaluate(
            _proAccess,
            DateTimeOffset.UtcNow);
        if (presentation.IsVerified)
        {
            if (_warmProTelemetry is null
                && _proAccessService.CreateRemotePlayerSource() is { } source)
            {
                _warmProTelemetry = new PrewarmedRemotePlayerTelemetrySource(source);
                _warmProTelemetry.Start();
            }

            return Task.CompletedTask;
        }

        return StopProTelemetryWarmupAsync();
    }

    private async Task StopProTelemetryWarmupAsync()
    {
        if (_warmProTelemetry is not { } source)
        {
            return;
        }

        _warmProTelemetry = null;
        await source.DisposeAsync();
    }

    private void ApplyProAccessState(ProAccessSnapshot access)
    {
        var now = DateTimeOffset.UtcNow;
        var presentation = HomeProPresentationPolicy.Evaluate(access, now);
        ApplyHomePresentationTheme(presentation.HasCurrentProAccess);
        ScheduleProExpiryRefresh(access, presentation, now);

        var checking = string.Equals(access.StatusCode, "checking", StringComparison.Ordinal);
        if (checking)
        {
            ProTierLabel.Text = "ACCESS / CHECKING";
            ProAccountLabel.Text = "  ·  ĐANG ĐỌC PHIÊN ĐÃ LƯU";
            ProAccessDetailLabel.Text = "Đang kiểm tra quyền và phiên bản Pro Agent…";
            ProAccessActionLabel.Text = "ĐỢI…";
            ProAccessStateBar.Fill = HomeBrush("#E7B74E");
            ProAccessFootnoteLabel.Text = "Đang đọc quyền đã lưu an toàn trên thiết bị này.";
            LogoutProButton.Visibility = Visibility.Collapsed;
            return;
        }

        ProAccountLabel.Text = access.IsAuthenticated
            ? $"  ·  STEAM ••••{access.SteamId64![^4..]}"
            : "  ·  CHƯA ĐĂNG NHẬP STEAM";
        LogoutProButton.Visibility = access.IsAuthenticated
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (presentation.IsVerified)
        {
            ProTierLabel.Text = access.IsOffline ? "PRO / OFFLINE LICENSE" : "PRO / ACTIVE";
            ProAccessDetailLabel.Text = access.Entitlement.ExpiresAt is { } activeUntil
                ? $"Player + AI Tracking · hết hạn {activeUntil.ToLocalTime():dd/MM/yyyy HH:mm}"
                : "Player + AI Tracking · quyền vĩnh viễn";
            ProAccessActionLabel.Text = "ĐÃ XÁC MINH  ✓";
            ProAccessStateBar.Fill = (Brush)FindResource("HomeAccent");
            ProAccessFootnoteLabel.Text = "Quyền Pro đã sẵn sàng: player, AI, loài và cân nặng được ghép vào Live Map.";
            SourceStatusLabel.Text = access.IsOffline
                ? "Pro đang dùng giấy phép offline còn hiệu lực. Player + AI Tracking đã sẵn sàng."
                : "Pro đã xác minh. Player + AI Tracking đã sẵn sàng trên mọi server.";
            return;
        }

        if (presentation.HasCurrentProAccess)
        {
            ProTierLabel.Text = "PRO / AGENT CHƯA SẴN SÀNG";
            ProAccessDetailLabel.Text = access.IsOffline
                ? "Không có mạng và chưa có Pro Agent tương thích trên máy"
                : "Chưa tải được Pro Agent tương thích; đăng xuất rồi đăng nhập lại để thử lại";
            ProAccessActionLabel.Text = "AGENT CHƯA SẴN SÀNG";
            ProAccessStateBar.Fill = HomeBrush("#E7B74E");
            ProAccessFootnoteLabel.Text = "Quyền Pro còn hiệu lực; Agent cần hoàn tất trước khi ghép player và AI vào map.";
            SourceStatusLabel.Text = "Tài khoản có Pro nhưng Agent tương thích chưa sẵn sàng.";
            return;
        }

        ProTierLabel.Text = access.IsAuthenticated ? "FREE / VERIFIED" : "FREE / PUBLIC";
        var entitlementExpired = access.Entitlement.ExpiresAt is { } expiresAt
                                 && expiresAt <= now;
        ProAccessDetailLabel.Text = entitlementExpired
            ? "Quyền Pro đã hết hạn; Home đã trở về chế độ Free"
            : access.StatusCode switch
        {
            "session_expired" => "Phiên Steam đã hết hạn; đăng nhập lại để kiểm tra quyền",
            "license_service_unavailable" => "Chưa kết nối được dịch vụ cấp phép; Free vẫn hoạt động",
            _ when access.IsAuthenticated => "Tài khoản này chưa được cấp Isle Live Map Pro",
            _ => "Đăng nhập Steam để kiểm tra quyền theo tài khoản"
        };
        ProAccessActionLabel.Text = access.IsAuthenticated ? "KIỂM TRA LẠI  →" : "ĐĂNG NHẬP  →";
        ProAccessStateBar.Fill = new SolidColorBrush(Color.FromRgb(111, 109, 85));
        ProAccessFootnoteLabel.Text = "Nâng cấp tùy chọn: phân loại player, AI, loài và cân nặng. Free luôn hoạt động độc lập.";
        SourceStatusLabel.Text = entitlementExpired
            ? "Quyền Pro đã hết hạn. Free vẫn sẵn sàng trên mọi server."
            : "Free đã sẵn sàng cho mọi server. Pro chỉ được ghép thêm sau khi xác minh quyền.";
    }

    private void ScheduleProExpiryRefresh(
        ProAccessSnapshot access,
        HomeProPresentationState presentation,
        DateTimeOffset now)
    {
        if (_proExpiryTimer is null)
        {
            return;
        }

        _proExpiryTimer.Stop();
        if (!presentation.HasCurrentProAccess
            || access.Entitlement.ExpiresAt is not { } expiresAt
            || expiresAt <= now)
        {
            return;
        }

        var untilExpiry = expiresAt - now;
        _proExpiryTimer.Interval = untilExpiry < TimeSpan.FromSeconds(30)
            ? TimeSpan.FromMilliseconds(Math.Max(250, untilExpiry.TotalMilliseconds + 100))
            : TimeSpan.FromSeconds(30);
        _proExpiryTimer.Start();
    }

    private async void ProExpiryTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        if (_proAccess.Entitlement.ExpiresAt is not { } expiresAt)
        {
            _proExpiryTimer?.Stop();
            return;
        }

        if (expiresAt > now)
        {
            ScheduleProExpiryRefresh(
                _proAccess,
                HomeProPresentationPolicy.Evaluate(_proAccess, now),
                now);
            return;
        }

        _proExpiryTimer?.Stop();
        await StopProTelemetryWarmupAsync();
        ApplyProAccessState(_proAccess);
        RefreshMapLaunchControls();
        SourceStatusLabel.Text = "Quyền Pro đã hết hạn. Home đã tự chuyển về giao diện Free.";
    }

    private void ApplyHomePresentationTheme(bool premium)
    {
        _premiumHomeTheme = premium;
        var palette = premium
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HomeSurface"] = "#F20C0A06",
                ["HomePanel"] = "#D0151008",
                ["HomePanelSoft"] = "#B81A1408",
                ["HomeInputSurface"] = "#AD100D07",
                ["HomeBone"] = "#FFF5D8",
                ["HomeMuted"] = "#C9B984",
                ["HomeSubtle"] = "#927F55",
                ["HomeLine"] = "#5C4720",
                ["HomeLineStrong"] = "#8E6C24",
                ["HomeShellLine"] = "#8A745131",
                ["HomeAccent"] = "#E6B94C",
                ["HomeAccentBright"] = "#FFE5A0",
                ["HomeAccentDeep"] = "#3B2A07",
                ["HomeSelection"] = "#6AE6B94C",
                ["HomeButtonFill"] = "#D03A2A08",
                ["HomeHover"] = "#2B2008",
                ["HomePressed"] = "#47330A"
            }
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HomeSurface"] = "#F2071716",
                ["HomePanel"] = "#C50B2421",
                ["HomePanelSoft"] = "#A50A211F",
                ["HomeInputSurface"] = "#A5071716",
                ["HomeBone"] = "#E9F4EE",
                ["HomeMuted"] = "#9EB3AD",
                ["HomeSubtle"] = "#78938C",
                ["HomeLine"] = "#35554F",
                ["HomeLineStrong"] = "#486C64",
                ["HomeShellLine"] = "#6A58736C",
                ["HomeAccent"] = "#37D4C6",
                ["HomeAccentBright"] = "#E8FFFA",
                ["HomeAccentDeep"] = "#163C38",
                ["HomeSelection"] = "#6A37D4C6",
                ["HomeButtonFill"] = "#C5193E38",
                ["HomeHover"] = "#183631",
                ["HomePressed"] = "#265048"
            };

        foreach (var (key, color) in palette)
        {
            Resources[key] = HomeBrush(color);
        }

        HomeModeLabel.Text = premium ? "ISLE · PRO" : "ISLE";
        HomeClientModeLabel.Text = premium
            ? "  VERIFIED PREMIUM TELEMETRY"
            : "  OPEN TELEMETRY CLIENT";
        ProSectionHeading.Text = premium
            ? "PRO ACCESS · ĐÃ KÍCH HOẠT"
            : "KÍCH HOẠT PRO";
        ApplyMapLaunchAccent();
    }
}
