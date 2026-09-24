using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TheIsleOverlay.Core;
using TheIsleOverlay.Gacha;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.LocalTelemetry;
using TheIsleOverlay.Origin;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private readonly List<Button> _serverActionButtons = [];
    private string _launchStatus = "Chọn nguồn stats phù hợp với server đang chơi.";
    private readonly Dictionary<Button, string> _serverButtonLabels = [];
    private string? _launchingServer;
    private string _lastUpdateStatus = "ĐANG KIỂM TRA BẢN CẬP NHẬT…";
    private string _lastUpdateColor = "#E7B74E";

    private void UpdateReleaseRailLayout()
    {
        var show = _page == "home" && (ActualWidth > 0 ? ActualWidth : Width) >= 1100;
        ReleaseRail.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ReleaseRailColumn.Width = show ? new GridLength(270) : new GridLength(0);
    }

    private async void GachaServer_Click(object sender, RoutedEventArgs e) =>
        await LaunchServerAsync("GACHA", CreateGachaSessionAsync);
    private async void OriginServer_Click(object sender, RoutedEventArgs e) =>
        await LaunchServerAsync("ORIGIN 5X", CreateOriginSessionAsync);
    private async void SdvnServer_Click(object sender, RoutedEventArgs e) =>
        await LaunchServerAsync("SDVN", CreateSdvnSessionAsync);

    private void RefreshLaunchButtons()
    {
        var available = MapLaunchGatePolicy.AllowsMap(_mapLaunchGateState) && _mapOpenStarted == 0;
        if (_mapActionButton is not null) _mapActionButton.IsEnabled = available;
        foreach (var button in _serverActionButtons)
        {
            button.IsEnabled = available;
            if (_serverButtonLabels.TryGetValue(button, out var label)
                && button.Content is StackPanel panel && panel.Children.OfType<TextBlock>().LastOrDefault() is { } text)
                text.Text = _launchingServer == label ? "ĐANG KẾT NỐI…" : label;
        }
    }

    private async Task<bool> PrepareMapLaunchAsync()
    {
        if (_mapLaunchGateState == MapLaunchGateState.Checking)
        {
            SetStatus("Đang kiểm tra cập nhật; vui lòng chờ…");
            if (_updateTask is not null) await _updateTask;
        }
        _shutdown.Token.ThrowIfCancellationRequested();
        if (!MapLaunchGatePolicy.AllowsMap(_mapLaunchGateState))
        { SetStatus("Có bản mới đã tải. Hãy khởi động lại để cập nhật trước khi mở map."); return false; }
        if (!EnsureNpcapReady()) return false;
        await LoadProAsync();
        _shutdown.Token.ThrowIfCancellationRequested();
        return true;
    }

    private async Task LaunchServerAsync(string label, Func<Task<ITelemetrySession?>> sessionFactory)
    {
        if (Interlocked.Exchange(ref _mapOpenStarted, 1) != 0) return;
        var handedOff = false;
        _launchingServer = label;
        SetStatus($"{label} · ĐANG KẾT NỐI…");
        RefreshLaunchButtons();
        try
        {
            if (!await PrepareMapLaunchAsync()) return;
            var session = await sessionFactory();
            if (session is null) { SetStatus($"Đã hủy kết nối {label}. Bạn vẫn có thể chọn nguồn khác."); return; }
            handedOff = await OpenOverlaySessionAsync(session, label);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error)
        {
            SetStatus(error is OperationCanceledException or HttpRequestException or TimeoutException
                ? $"{label} chưa phản hồi. Kiểm tra mạng và thử lại; phiên đã lưu không bị xóa."
                : $"Không kết nối được {label}: {error.Message}");
        }
        finally
        {
            if (!handedOff) { _launchingServer = null; Interlocked.Exchange(ref _mapOpenStarted, 0); RefreshLaunchButtons(); }
        }
    }

    // This method owns the remote session from entry, even when Pro startup fails.
    private async Task<bool> OpenOverlaySessionAsync(ITelemetrySession? remoteSession, string label)
    {
        IRemotePlayerTelemetrySource? pro = null;
        ILocalMovementSource? gps = null;
        LocalPositionTelemetrySession? local = null;
        MainWindow? overlay = null;
        try
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            pro = await TakeProPlayerSourceAsync();
            gps = App.CurrentApp.TakeLocalTelemetrySource();
            local = new LocalPositionTelemetrySession(remoteSession, gps, label, pro);
            overlay = new MainWindow(local, label, ProFeatureAccessGrant.FromSnapshot(_pro, DateTimeOffset.UtcNow));
            overlay.Show();
            Application.Current.MainWindow = overlay;
            Close();
            return true;
        }
        catch
        {
            overlay?.Close();
            if (local is not null) await local.DisposeAsync();
            else
            {
                if (remoteSession is not null) await remoteSession.DisposeAsync();
                if (gps is not null) await gps.DisposeAsync();
                if (pro is not null) await pro.DisposeAsync();
            }
            await _proTelemetryWarmup.CancelHandoffAsync();
            Application.Current.MainWindow = this;
            throw;
        }
    }

    private async Task<ITelemetrySession?> CreateGachaSessionAsync()
    {
        var store = new GachaOverlayCredentialStore(AppPaths.GachaOverlayCredential);
        var credentials = await store.LoadAsync(_shutdown.Token);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
        if (credentials is not null)
        {
            SetStatus("GACHA · ĐANG KIỂM TRA PHIÊN…");
            var state = await GachaOverlayAuthService.ValidateAsync(http, credentials, _shutdown.Token);
            if (state == GachaOverlayAuthValidationState.Invalid && credentials.CanRefresh)
            {
                try
                {
                    credentials = await GachaOverlayAuthService.RefreshAsync(http, credentials, _shutdown.Token);
                    await store.SaveAsync(credentials, _shutdown.Token);
                    state = GachaOverlayAuthValidationState.Valid;
                }
                catch (GachaOverlayAuthenticationException) { state = GachaOverlayAuthValidationState.Invalid; }
            }
            if (state == GachaOverlayAuthValidationState.Invalid) { store.Clear(); credentials = null; }
        }
        if (credentials is null)
        {
            var login = new GachaSteamLoginWindow { Owner = this };
            if (login.ShowDialog() != true || login.Credentials is null) return null;
            credentials = login.Credentials;
            if (await GachaOverlayAuthService.ValidateAsync(http, credentials, _shutdown.Token) == GachaOverlayAuthValidationState.Invalid)
                throw new GachaOverlayAuthenticationException("Phiên GACHA không hợp lệ. Hãy đăng nhập lại.");
            await store.SaveAsync(credentials, _shutdown.Token);
        }
        var client = new GachaOverlayClient(credentials, credentialsRefreshed: (updated, token) => store.SaveAsync(updated, token));
        return new GachaTelemetrySession(new GachaStatsSession(client));
    }

    private async Task<ITelemetrySession?> CreateOriginSessionAsync()
    {
        SetStatus("ORIGIN · ĐANG KIỂM TRA PHIÊN…");
        var cookie = await OriginSessionCookieReader.ReadFromProfileAsync(new WindowInteropHelper(this).Handle, _shutdown.Token);
        if (cookie is null || await ValidateOriginCookieAsync(cookie, _shutdown.Token) != LoginSessionValidationState.Valid)
        {
            var login = new LoginWindow(TelemetrySourceDefinition.Origin, ValidateOriginCookieAsync) { Owner = this };
            if (login.ShowDialog() != true || login.CookieValue is null) return null;
            cookie = login.CookieValue;
        }
        return new OriginStatsSession(new OriginStatsClient(cookie));
    }

    private static async Task<LoginSessionValidationState> ValidateOriginCookieAsync(string cookie, CancellationToken ct)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
        return await OriginAuthService.ValidateAsync(http, cookie, ct) switch
        {
            OriginAuthValidationState.Valid => LoginSessionValidationState.Valid,
            OriginAuthValidationState.Invalid => LoginSessionValidationState.Invalid,
            _ => LoginSessionValidationState.Unavailable
        };
    }

    private Task<ITelemetrySession?> CreateSdvnSessionAsync()
    {
        var store = new SdvnCredentialStore(Path.Combine(AppPaths.Root, "SDVN"));
        var dialog = new SdvnTenantWindow(store) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedTenant is not { } tenant || dialog.Cookie is null)
            return Task.FromResult<ITelemetrySession?>(null);
        ITelemetrySession session = new SdvnTelemetrySession(new SdvnClient(tenant, dialog.Cookie), () => store.Clear(tenant));
        return Task.FromResult<ITelemetrySession?>(session);
    }
}
