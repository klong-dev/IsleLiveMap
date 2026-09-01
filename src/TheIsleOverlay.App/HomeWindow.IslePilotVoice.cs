using System.Net.Http;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private readonly IslePilotVoiceCredentialStore _islePilotVoiceCredentialStore = new(
        AppPaths.IslePilotVoiceCredential);

    private async Task<bool> EnsureIslePilotVoiceCredentialsAsync(string expectedSteamId64)
    {
        var stored = await _islePilotVoiceCredentialStore.LoadAsync(_shutdown.Token);
        if (stored is not null
            && !string.Equals(
                stored.SteamId64,
                expectedSteamId64,
                StringComparison.Ordinal))
        {
            _islePilotVoiceCredentialStore.Clear();
            stored = null;
        }

        if (stored is not null)
        {
            SourceStatusLabel.Text = "ĐANG XÁC MINH PLAYER LÂN CẬN…";
            var storedState = await ValidateIslePilotVoiceCredentialsAsync(stored);
            if (storedState != IslePilotVoiceAuthValidationState.Invalid)
            {
                return true;
            }

            _islePilotVoiceCredentialStore.Clear();
        }

        var loginWindow = new IslePilotVoiceLoginWindow { Owner = this };
        if (loginWindow.ShowDialog() != true || loginWindow.Credentials is null)
        {
            SourceStatusLabel.Text = "Đã bỏ qua xác thực player lân cận; Pro vẫn dùng inbound dự phòng.";
            return false;
        }

        var credentials = loginWindow.Credentials;
        if (!string.Equals(
                credentials.SteamId64,
                expectedSteamId64,
                StringComparison.Ordinal))
        {
            SourceStatusLabel.Text = "SteamID IsleVOIP không trùng tài khoản Pro đang kích hoạt.";
            return false;
        }

        var state = await ValidateIslePilotVoiceCredentialsAsync(credentials);
        if (state == IslePilotVoiceAuthValidationState.Invalid)
        {
            SourceStatusLabel.Text = "IsleVOIP từ chối phiên vừa xác thực.";
            return false;
        }

        await _islePilotVoiceCredentialStore.SaveAsync(credentials, _shutdown.Token);
        return true;
    }

    private async Task<IslePilotVoiceAuthValidationState> ValidateIslePilotVoiceCredentialsAsync(
        IslePilotVoiceAuthResult credentials)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return await IslePilotVoiceAuthService.ValidateAsync(
            httpClient,
            credentials,
            _shutdown.Token);
    }
}
