using System.Net.Http;
using System.Windows;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private readonly IslePilotCredentialStore _islePilotCredentialStore = new(
        AppPaths.IslePilotCredential);
    private IslePilotOverlayAuthResult? _islePilotCredentials;
    private bool _islePilotConnecting;

    private async void SteamLoginPanel_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _islePilotCredentials = await _islePilotCredentialStore.LoadAsync(_shutdown.Token);
            ApplySteamLoginState(_islePilotCredentials);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không đọc được phiên Steam đã lưu: {FriendlyError(exception)}";
            ApplySteamLoginState(null);
        }
    }

    private async void SteamLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_islePilotConnecting || _connecting)
        {
            return;
        }

        _islePilotConnecting = true;
        SetSteamLoginControlsEnabled(false);
        try
        {
            var credentials = _islePilotCredentials
                ?? await _islePilotCredentialStore.LoadAsync(_shutdown.Token);
            if (credentials is not null)
            {
                SourceStatusLabel.Text = "ĐANG XÁC MINH PHIÊN ISLEPILOT…";
                var savedState = await ValidateIslePilotCredentialsAsync(credentials);
                if (savedState == IslePilotOverlayAuthValidationState.Invalid)
                {
                    _islePilotCredentialStore.Clear();
                    credentials = null;
                    _islePilotCredentials = null;
                    ApplySteamLoginState(null);
                    SourceStatusLabel.Text = "Phiên đã hết hạn. Hãy đăng nhập Steam lại.";
                }
            }

            if (credentials is null)
            {
                var loginWindow = new IslePilotSteamLoginWindow { Owner = this };
                if (loginWindow.ShowDialog() != true || loginWindow.Credentials is null)
                {
                    SourceStatusLabel.Text = "Chưa đăng nhập Steam. Không có token nào được lưu.";
                    return;
                }

                credentials = loginWindow.Credentials;
                SourceStatusLabel.Text = "ĐÃ NHẬN PHIÊN · ĐANG XÁC MINH /ME…";
                var newState = await ValidateIslePilotCredentialsAsync(credentials);
                if (newState == IslePilotOverlayAuthValidationState.Invalid)
                {
                    SourceStatusLabel.Text = "IslePilot từ chối phiên vừa đăng nhập. Hãy thử lại.";
                    return;
                }

                await _islePilotCredentialStore.SaveAsync(credentials, _shutdown.Token);
                _islePilotCredentials = credentials;
                ApplySteamLoginState(credentials);
            }

            SourceStatusLabel.Text = "ĐANG KHỞI TẠO ISLEPILOT REALTIME…";
            await OpenIslePilotOverlayAsync(credentials);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không kết nối được IslePilot: {FriendlyError(exception)}";
        }
        finally
        {
            _islePilotConnecting = false;
            SetSteamLoginControlsEnabled(true);
        }
    }

    private void LogoutSteamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_islePilotConnecting)
        {
            return;
        }

        _islePilotCredentialStore.Clear();
        _islePilotCredentials = null;
        ApplySteamLoginState(null);
        SourceStatusLabel.Text = "Đã đăng xuất IslePilot và xóa token đã lưu.";
    }

    private async Task<IslePilotOverlayAuthValidationState> ValidateIslePilotCredentialsAsync(
        IslePilotOverlayAuthResult credentials)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        return await IslePilotOverlayAuthService.ValidateAsync(
            httpClient,
            credentials,
            _shutdown.Token);
    }

    private async Task OpenIslePilotOverlayAsync(IslePilotOverlayAuthResult credentials)
    {
        var session = IslePilotRealtimeSession.Create(new IslePilotOverlayOptions
        {
            OverlayToken = credentials.OverlayToken
        });

        try
        {
            var overlay = new MainWindow(session, "ISLEPILOT");
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private void ApplySteamLoginState(IslePilotOverlayAuthResult? credentials)
    {
        var authenticated = credentials is not null;
        SteamAccountLabel.Text = authenticated
            ? $"STEAM · ••••{credentials!.SteamId[^4..]}"
            : "CHƯA ĐĂNG NHẬP STEAM";
        SteamLoginDetailLabel.Text = authenticated
            ? "Phiên IslePilot đã được mã hóa bằng Windows DPAPI"
            : "Một phiên cho mọi server đã cài IslePilot";
        SteamLoginActionLabel.Text = authenticated ? "MỞ OVERLAY  →" : "ĐĂNG NHẬP  →";
        LogoutSteamButton.Visibility = authenticated ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetSteamLoginControlsEnabled(bool enabled)
    {
        SteamLoginButton.IsEnabled = enabled;
        LogoutSteamButton.IsEnabled = enabled && _islePilotCredentials is not null;
    }
}
