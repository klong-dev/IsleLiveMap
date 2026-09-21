using System.Net.Http;
using System.Windows;
using System.Windows.Interop;
using TheIsleOverlay.LocalTelemetry;
using TheIsleOverlay.Origin;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private bool _originConnecting;

    private async void OriginStatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureMapLaunchAvailable()
            || _originConnecting
            || _gachaConnecting
            || _islePilotConnecting
            || _connecting)
        {
            return;
        }

        if (!EnsureLocalCaptureAvailable())
        {
            return;
        }

        _originConnecting = true;
        OriginStatsButton.Content = "ORIGIN x5 · ĐANG KẾT NỐI…";
        RefreshMapLaunchControls();
        try
        {
            var source = TelemetrySourceDefinition.Origin;
            var loginWindow = new LoginWindow(
                source,
                ValidateOriginSessionAsync)
            {
                Owner = this
            };
            if (loginWindow.ShowDialog() != true
                || string.IsNullOrWhiteSpace(loginWindow.CookieValue))
            {
                SourceStatusLabel.Text =
                    "Chưa nhận được phiên Origin. Hãy đăng nhập Steam trên playorigin.gg rồi thử lại.";
                return;
            }

            SourceStatusLabel.Text =
                "ĐANG TÌM DINO TRÊN MAIN ORIGIN VÀ VOICE CHAT SERVER…";
            await OpenOriginOverlayAsync(
                new OriginStatsClient(loginWindow.CookieValue));
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            SourceStatusLabel.Text = $"Không kết nối được Origin stats: {FriendlyError(exception)}";
        }
        finally
        {
            _originConnecting = false;
            if (OriginStatsButton is not null)
            {
                OriginStatsButton.Content = "ORIGIN";
            }

            RefreshMapLaunchControls();
        }
    }

    /// <summary>
    /// Reuses the Origin session saved in Isle Live Map's own WebView2
    /// profile. The unified map button selects Origin only after the official
    /// health command proves that a dinosaur is active on Main or Voice.
    /// </summary>
    private async Task<bool> TryOpenActiveOriginFromUnifiedMapAsync()
    {
        string? cookieHeader;
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            cookieHeader = await OriginSessionCookieReader.ReadFromProfileAsync(
                handle,
                _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cookieHeader))
        {
            return false;
        }

        var validation = await ValidateOriginSessionAsync(cookieHeader, _shutdown.Token);
        if (validation != LoginSessionValidationState.Valid)
        {
            return false;
        }

        var client = new OriginStatsClient(cookieHeader);
        try
        {
            SourceStatusLabel.Text = "ĐANG KIỂM TRA ORIGIN MAIN / VOICE…";
            var activeServer = await client.DetectActiveServerAsync(_shutdown.Token);
            if (activeServer is null)
            {
                client.Dispose();
                return false;
            }

            SourceStatusLabel.Text =
                $"TỰ ĐỘNG CHỌN ORIGIN · {activeServer.DisplayName.ToUpperInvariant()}";
            await OpenOriginOverlayAsync(client, activeServer);
            return true;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            client.Dispose();
            throw;
        }
        catch
        {
            client.Dispose();
            return false;
        }
    }

    private async Task OpenOriginOverlayAsync(
        OriginStatsClient client,
        OriginServer? activeServer = null)
    {
        var originSession = new OriginStatsSession(client, activeServer);
        try
        {
            var overlay = new MainWindow(
                new LocalPositionTelemetrySession(
                    originSession,
                    App.CurrentApp.TakeLocalTelemetrySource(),
                    "ORIGIN x5",
                    TakeProPlayerSource()),
                "ORIGIN x5",
                ProFeatureAccessGrant.FromSnapshot(
                    _proAccess,
                    DateTimeOffset.UtcNow));
            Application.Current.MainWindow = overlay;
            overlay.Show();
            Close();
        }
        catch
        {
            await originSession.DisposeAsync();
            throw;
        }
    }

    private static async Task<LoginSessionValidationState> ValidateOriginSessionAsync(
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        return await OriginAuthService.ValidateAsync(client, cookieHeader, cancellationToken)
            switch
            {
                OriginAuthValidationState.Valid => LoginSessionValidationState.Valid,
                OriginAuthValidationState.Invalid => LoginSessionValidationState.Invalid,
                _ => LoginSessionValidationState.Unavailable
            };
    }
}
