using System.Net;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.ProClient;

public sealed class ProAccessService : IDisposable
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ProApiClient _apiClient;
    private readonly ProCredentialStore _credentialStore;
    private readonly ProReleaseManager _releaseManager;
    private readonly TimeProvider _timeProvider;
    private StoredProSession? _session;
    private ProAgentInstallation? _installation;
    private ProAccessSnapshot _current = ProAccessSnapshot.SignedOut;
    private int _disposed;

    public ProAccessService(
        ProClientOptions? options = null,
        HttpClient? httpClient = null,
        TimeProvider? timeProvider = null,
        string? updatePublicKeyPem = null)
    {
        options ??= new ProClientOptions();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _ownsHttpClient = httpClient is null;
        _apiClient = new ProApiClient(_httpClient, options.BaseUri);
        _credentialStore = new ProCredentialStore(options.CredentialPath);
        _releaseManager = new ProReleaseManager(
            _apiClient,
            options.InstallationRoot,
            updatePublicKeyPem);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ProAccessSnapshot Current
    {
        get
        {
            lock (_stateGate)
            {
                return _current;
            }
        }
    }

    public ProLoginAttempt CreateLoginAttempt() => _apiClient.CreateLoginAttempt();

    public async Task<ProAccessSnapshot> InitializeAsync(
        string hostVersion,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var stored = await _credentialStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
            return SetState(null, null, ProAccessSnapshot.SignedOut);
            }

            return await RefreshStoredSessionAsync(stored, hostVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ProAccessSnapshot> CompleteLoginAsync(
        ProLoginAttempt attempt,
        string callbackUri,
        string hostVersion,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var tokens = await _apiClient.ExchangeAsync(attempt, callbackUri, cancellationToken)
                .ConfigureAwait(false);
            var account = await _apiClient.GetEntitlementAsync(tokens.AccessToken, cancellationToken)
                .ConfigureAwait(false);
            var normalized = tokens with
            {
                Entitlement = account.Entitlement,
                OfflineLicenseToken = account.OfflineLicenseToken,
                OfflineLicenseExpiresAt = account.OfflineLicenseExpiresAt
            };
            return await ApplyOnlineTokensAsync(
                    account.SteamId64,
                    normalized,
                    hostVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _credentialStore.Clear();
            SetState(null, null, ProAccessSnapshot.SignedOut);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public IRemotePlayerTelemetrySource? CreateRemotePlayerSource()
    {
        lock (_stateGate)
        {
            if (_session is null ||
                _installation is null ||
                !_current.IsPro ||
                !_current.AgentReady ||
                !_session.HasUsableOfflineLicense(_timeProvider.GetUtcNow()))
            {
                return null;
            }

            return new ProAgentRemotePlayerSource(
                _installation.ExecutablePath,
                _currentHostVersion,
                _session.SteamId64,
                _session.OfflineLicenseToken!);
        }
    }

    private string _currentHostVersion = "0.0.0";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _operationGate.Dispose();
        _releaseManager.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<ProAccessSnapshot> RefreshStoredSessionAsync(
        StoredProSession stored,
        string hostVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            if (stored.RefreshTokenExpiresAt <= _timeProvider.GetUtcNow())
            {
                throw new ProApiException("The saved Steam session has expired.", HttpStatusCode.BadRequest);
            }

            var tokens = await _apiClient.RefreshAsync(stored.RefreshToken, cancellationToken)
                .ConfigureAwait(false);
            return await ApplyOnlineTokensAsync(
                    stored.SteamId64,
                    tokens,
                    hostVersion,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProApiException exception) when (
            exception.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            _credentialStore.Clear();
            return SetState(null, null, ProAccessSnapshot.SignedOut with
            {
                StatusCode = "session_expired"
            });
        }
        catch (ProApiException)
        {
            return await ApplyOfflineFallbackAsync(stored, hostVersion, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<ProAccessSnapshot> ApplyOnlineTokensAsync(
        string steamId64,
        ProTokenResponse tokens,
        string hostVersion,
        CancellationToken cancellationToken)
    {
        ValidateHostVersion(hostVersion);
        var stored = new StoredProSession(
            steamId64,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt,
            tokens.OfflineLicenseToken,
            tokens.OfflineLicenseExpiresAt,
            tokens.Entitlement);
        await _credentialStore.SaveAsync(stored, cancellationToken).ConfigureAwait(false);

        ProAgentInstallation? installation = null;
        string? statusCode = null;
        if (stored.HasUsableOfflineLicense(_timeProvider.GetUtcNow()))
        {
            try
            {
                installation = await _releaseManager.EnsureLatestAsync(
                        hostVersion,
                        tokens.AccessToken,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is ProApiException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                installation = await _releaseManager.LoadInstalledAsync(hostVersion, cancellationToken)
                    .ConfigureAwait(false);
                statusCode = installation is null ? "agent_unavailable" : "agent_update_unavailable";
            }
        }

        var snapshot = BuildSnapshot(
            stored,
            installation,
            isOffline: false,
            statusCode);
        return SetState(stored, installation, snapshot, hostVersion);
    }

    private async Task<ProAccessSnapshot> ApplyOfflineFallbackAsync(
        StoredProSession stored,
        string hostVersion,
        CancellationToken cancellationToken)
    {
        ValidateHostVersion(hostVersion);
        var installation = stored.HasUsableOfflineLicense(_timeProvider.GetUtcNow())
            ? await _releaseManager.LoadInstalledAsync(hostVersion, cancellationToken).ConfigureAwait(false)
            : null;
        var statusCode = installation is not null
            ? "offline_license"
            : stored.Entitlement.IsPro
                ? "offline_agent_unavailable"
                : "license_service_unavailable";
        var snapshot = BuildSnapshot(stored, installation, isOffline: true, statusCode);
        return SetState(stored, installation, snapshot, hostVersion);
    }

    private static ProAccessSnapshot BuildSnapshot(
        StoredProSession session,
        ProAgentInstallation? installation,
        bool isOffline,
        string? statusCode) => new(
        session.SteamId64,
        session.Entitlement,
        isOffline,
        installation is not null,
        installation?.Version,
        session.OfflineLicenseExpiresAt,
        statusCode);

    private ProAccessSnapshot SetState(
        StoredProSession? session,
        ProAgentInstallation? installation,
        ProAccessSnapshot snapshot,
        string? hostVersion = null)
    {
        lock (_stateGate)
        {
            _session = session;
            _installation = installation;
            _current = snapshot;
            if (hostVersion is not null)
            {
                _currentHostVersion = hostVersion;
            }

            return snapshot;
        }
    }

    private static void ValidateHostVersion(string hostVersion)
    {
        if (!SemanticVersion.TryParse(hostVersion, out _))
        {
            throw new ArgumentException("The host version is invalid.", nameof(hostVersion));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
}
