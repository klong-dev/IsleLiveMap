namespace TheIsleOverlay.IslePilot;

/// <summary>Shared credential recovery; independent of UI and Pro entitlement.</summary>
public sealed class IslePilotOverlayLoginFlow(HttpClient http, IslePilotCredentialStore store)
{
    public async Task<IslePilotOverlayAuthResult?> ResolveAsync(
        Func<CancellationToken, Task<IslePilotOverlayAuthResult?>> login,
        Action<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        var credentials = await store.LoadAsync(cancellationToken);
        if (credentials is not null)
        {
            status?.Invoke("ISLEPILOT · ĐANG KIỂM TRA PHIÊN…");
            var validation = await IslePilotOverlayAuthService.ValidateAsync(http, credentials, cancellationToken);
            if (validation != IslePilotOverlayAuthValidationState.Invalid)
            {
                // A network failure is not proof of an expired token. The
                // realtime session will retry without losing the saved login.
                return credentials;
            }
            store.Clear();
            status?.Invoke("Phiên IslePilot đã hết hạn. Hãy đăng nhập Steam lại để nhận stats.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        credentials = await login(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (credentials is null) return null;

        status?.Invoke("ISLEPILOT · ĐANG XÁC MINH PHIÊN VỪA ĐĂNG NHẬP…");
        if (await IslePilotOverlayAuthService.ValidateAsync(http, credentials, cancellationToken)
            == IslePilotOverlayAuthValidationState.Invalid)
        {
            throw new IslePilotOverlayAuthenticationException("IslePilot từ chối phiên vừa đăng nhập. Hãy đăng nhập lại.");
        }
        await store.SaveAsync(credentials, cancellationToken);
        return credentials;
    }
}
