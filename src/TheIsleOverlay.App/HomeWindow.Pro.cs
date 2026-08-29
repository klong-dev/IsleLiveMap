using System.Windows;
using System.Windows.Media;
using TheIsleOverlay.Core;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private readonly ProAccessService _proAccessService = new();
    private ProAccessSnapshot _proAccess = ProAccessSnapshot.SignedOut;
    private bool _proAccessLoading;
    private bool _proAccessInitialized;

    private async void ProAccessPanel_Loaded(object sender, RoutedEventArgs e)
    {
        if (_proAccessInitialized)
        {
            return;
        }

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
            RefreshMapLaunchControls();
        }
    }

    private void ProAccessButton_Click(object sender, RoutedEventArgs e)
    {
        if (_proAccessLoading || _connecting || _islePilotConnecting)
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
            await _proAccessService.LogoutAsync(_shutdown.Token);
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

    private IRemotePlayerTelemetrySource? CreateProPlayerSource() =>
        _proAccessService.CreateRemotePlayerSource();

    private void ApplyProAccessState(ProAccessSnapshot access)
    {
        var checking = string.Equals(access.StatusCode, "checking", StringComparison.Ordinal);
        if (checking)
        {
            ProTierLabel.Text = "ACCESS / CHECKING";
            ProAccountLabel.Text = "  ·  ĐANG ĐỌC PHIÊN ĐÃ LƯU";
            ProAccessDetailLabel.Text = "Đang kiểm tra quyền và phiên bản Pro Agent…";
            ProAccessActionLabel.Text = "ĐỢI…";
            ProAccessStateBar.Fill = HomeBrush("#E7B74E");
            LogoutProButton.Visibility = Visibility.Collapsed;
            return;
        }

        ProAccountLabel.Text = access.IsAuthenticated
            ? $"  ·  STEAM ••••{access.SteamId64![^4..]}"
            : "  ·  CHƯA ĐĂNG NHẬP STEAM";
        LogoutProButton.Visibility = access.IsAuthenticated
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (access.IsPro && access.AgentReady)
        {
            ProTierLabel.Text = access.IsOffline ? "PRO / OFFLINE LICENSE" : "PRO / ACTIVE";
            ProAccessDetailLabel.Text = access.Entitlement.ExpiresAt is { } expiresAt
                ? $"Player + AI Tracking · hết hạn {expiresAt.ToLocalTime():dd/MM/yyyy HH:mm}"
                : "Player + AI Tracking · quyền vĩnh viễn";
            ProAccessActionLabel.Text = "XÁC MINH LẠI  ↻";
            ProAccessStateBar.Fill = HomeBrush("#37D4C6");
            return;
        }

        if (access.IsPro)
        {
            ProTierLabel.Text = "PRO / AGENT CHƯA SẴN SÀNG";
            ProAccessDetailLabel.Text = access.IsOffline
                ? "Không có mạng và chưa có Pro Agent tương thích trên máy"
                : "Chưa tải được Pro Agent tương thích; bấm để thử lại";
            ProAccessActionLabel.Text = "THỬ LẠI  →";
            ProAccessStateBar.Fill = HomeBrush("#E7B74E");
            return;
        }

        ProTierLabel.Text = access.IsAuthenticated ? "FREE / VERIFIED" : "FREE / PUBLIC";
        ProAccessDetailLabel.Text = access.StatusCode switch
        {
            "session_expired" => "Phiên Steam đã hết hạn; đăng nhập lại để kiểm tra quyền",
            "license_service_unavailable" => "Chưa kết nối được dịch vụ cấp phép; Free vẫn hoạt động",
            _ when access.IsAuthenticated => "Tài khoản này chưa được cấp Isle Live Map Pro",
            _ => "Đăng nhập Steam để kiểm tra quyền theo tài khoản"
        };
        ProAccessActionLabel.Text = access.IsAuthenticated ? "KIỂM TRA LẠI  →" : "ĐĂNG NHẬP  →";
        ProAccessStateBar.Fill = new SolidColorBrush(Color.FromRgb(111, 109, 85));
    }
}
