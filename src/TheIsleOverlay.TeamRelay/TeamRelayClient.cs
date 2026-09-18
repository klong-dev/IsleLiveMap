using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace TheIsleOverlay.TeamRelay;

public sealed class TeamRelayClient : IAsyncDisposable
{
    public static readonly Uri DefaultBaseUri = new("https://isle-relay.klong.dev/");
    public static readonly TimeSpan SessionStartTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan FailedSessionCleanupTimeout = TimeSpan.FromSeconds(3);

    private readonly Uri _baseUri;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly TimeSpan _sessionStartTimeout;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<Guid, TeamMemberSnapshot> _members = [];
    private readonly Dictionary<Guid, TeamMapPingSnapshot> _mapPings = [];
    private readonly Dictionary<Guid, long> _memberRevisions = [];
    private readonly Dictionary<Guid, long> _memberSequences = [];
    private readonly Dictionary<Guid, long> _memberRemovalRevisions = [];
    private long _stateRevision;
    private long _snapshotRevision;
    private long _mapPingRevision;
    private bool _receivedVersionedPingBatch;
    private bool _receivedVersionedRemoval;

    private TeamRelayState _state = new();
    private TeamSession? _session;
    private HubConnection? _connection;
    private CancellationTokenSource? _heartbeatCancellation;
    private Task? _heartbeatTask;
    private bool _intentionalStop;
    private bool _disposed;

    public TeamRelayClient(
        Uri? baseUri = null,
        HttpClient? httpClient = null,
        TimeSpan? sessionStartTimeout = null)
    {
        _baseUri = EnsureTrailingSlash(baseUri ?? DefaultBaseUri);
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _ownsHttpClient = httpClient is null;
        _sessionStartTimeout = sessionStartTimeout ?? SessionStartTimeout;
        if (_sessionStartTimeout <= TimeSpan.Zero
            || _sessionStartTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sessionStartTimeout),
                "Session start timeout must be between zero and one minute.");
        }
    }

    public event EventHandler<TeamRelayState>? StateChanged;

    public TeamRelayState CurrentState
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public Task<TeamSession> CreateAsync(
        string displayName,
        CancellationToken cancellationToken = default) =>
        StartNewSessionAsync(
            "api/v1/teams",
            new CreateTeamRequest(displayName),
            cancellationToken);

    public Task<TeamSession> JoinAsync(
        string inviteCode,
        string displayName,
        CancellationToken cancellationToken = default) =>
        StartNewSessionAsync(
            "api/v1/teams/join",
            new JoinTeamRequest(inviteCode.Trim().ToUpperInvariant(), displayName),
            cancellationToken);

    public async Task<bool> PublishTelemetryAsync(
        TeamTelemetryUpdate telemetry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ThrowIfDisposed();

        var connection = _connection;
        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            return false;
        }

        try
        {
            return await connection.InvokeAsync<bool>(
                    "PublishTelemetry",
                    telemetry,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsMemberExpired(exception))
        {
            MarkExpired("Phiên nhóm đã hết hạn.");
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TeamMapPingSnapshot> UpsertMapPingAsync(
        TeamMapPingMutation mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ThrowIfDisposed();
        var connection = RequireLiveConnection();
        try
        {
            return await connection.InvokeAsync<TeamMapPingSnapshot>(
                    "UpsertMapPing",
                    mutation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapPingException(exception);
        }
    }

    public async Task DeleteMapPingAsync(
        Guid pingId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var connection = RequireLiveConnection();
        try
        {
            await connection.InvokeAsync<bool>(
                    "DeleteMapPing",
                    pingId,
                    expectedRevision,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw MapPingException(exception);
        }
    }

    public async Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EndCurrentSessionAsync(sendLeave: true, cancellationToken).ConfigureAwait(false);
            SetState(TeamRelayConnectionState.None, clearSession: true, clearMembers: true);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await EndCurrentSessionAsync(sendLeave: true, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Relay cleanup is also guaranteed by the short in-memory expiry.
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }
        }
    }

    private async Task<TeamSession> StartNewSessionAsync<TRequest>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_sessionStartTimeout);
        var operationToken = deadline.Token;
        var gateEntered = false;
        try
        {
            await _lifecycleGate.WaitAsync(operationToken).ConfigureAwait(false);
            gateEntered = true;
            TeamSession? session = null;
            try
            {
                await EndCurrentSessionAsync(sendLeave: true, operationToken).ConfigureAwait(false);
                SetState(
                    TeamRelayConnectionState.Connecting,
                    "Đang kết nối relay…",
                    clearSession: true,
                    clearMembers: true);

                session = await PostSessionAsync(path, request, operationToken).ConfigureAwait(false);
                _session = session;
                SetState(TeamRelayConnectionState.Connecting, "Đang mở kênh nhóm…");

                var connection = BuildConnection(session);
                _connection = connection;
                _intentionalStop = false;
                await connection.StartAsync(operationToken).ConfigureAwait(false);
                await RequestSnapshotAsync(connection, operationToken).ConfigureAwait(false);
                StartHeartbeat(session);
                SetState(TeamRelayConnectionState.Live);
                return session;
            }
            catch (Exception exception)
            {
                var failure = TimeoutFailure(exception, cancellationToken, deadline);
                using var cleanup = new CancellationTokenSource(FailedSessionCleanupTimeout);
                if (session is not null)
                {
                    await TryDeleteSessionAsync(session, cleanup.Token).ConfigureAwait(false);
                }

                await EndCurrentSessionAsync(sendLeave: false, cleanup.Token).ConfigureAwait(false);
                SetState(
                    TeamRelayConnectionState.Error,
                    failure.Message,
                    clearSession: true,
                    clearMembers: true);
                throw failure;
            }
        }
        catch (OperationCanceledException exception) when (
            !gateEntered
            && !cancellationToken.IsCancellationRequested
            && deadline.IsCancellationRequested)
        {
            var failure = new TimeoutException(
                TimeoutMessage(),
                exception);
            SetState(
                TeamRelayConnectionState.Error,
                failure.Message,
                clearSession: true,
                clearMembers: true);
            throw failure;
        }
        finally
        {
            if (gateEntered)
            {
                _lifecycleGate.Release();
            }
        }
    }

    private Exception TimeoutFailure(
        Exception exception,
        CancellationToken callerToken,
        CancellationTokenSource deadline) =>
        exception is OperationCanceledException
        && !callerToken.IsCancellationRequested
        && deadline.IsCancellationRequested
            ? new TimeoutException(
                TimeoutMessage(),
                exception)
            : exception;

    private string TimeoutMessage() =>
        $"Relay không phản hồi trong {_sessionStartTimeout.TotalSeconds:0.#} giây. Hãy kiểm tra mạng rồi thử lại.";

    private HubConnection BuildConnection(TeamSession session)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_baseUri, "hubs/team"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(session.MemberToken);
            })
            .WithAutomaticReconnect(new TeamRelayRetryPolicy())
            .Build();

        connection.On<TeamSnapshot>("ReceiveSnapshot", ReceiveSnapshot);
        connection.On<TeamMemberSnapshot>("MemberUpdated", MemberUpdated);
        connection.On<Guid>("MemberRemoved", MemberRemoved);
        connection.On<TeamMemberRemoval>("MemberRemovedV2", MemberRemoved);
        connection.On<IReadOnlyList<TeamMapPingSnapshot>>("MapPingsChanged", MapPingsChanged);
        connection.On<TeamMapPingBatch>("MapPingsChangedV2", MapPingsChanged);
        connection.On("TeamClosed", () => MarkExpired("Nhóm đã kết thúc."));

        connection.Reconnecting += _ =>
        {
            if (!_intentionalStop)
            {
                SetState(TeamRelayConnectionState.Reconnecting, "Mất kết nối, đang nối lại…");
            }

            return Task.CompletedTask;
        };
        connection.Reconnected += _ =>
        {
            if (!_intentionalStop)
            {
                SetState(TeamRelayConnectionState.Live);
            }
            return RequestSnapshotAsync(connection);
        };
        connection.Closed += exception =>
        {
            if (!_intentionalStop)
            {
                MarkExpired(exception is null
                    ? "Kênh nhóm đã đóng."
                    : "Không thể nối lại relay; hãy tạo hoặc vào nhóm lại.");
            }

            return Task.CompletedTask;
        };

        return connection;
    }

    private async Task<TeamSession> PostSessionAsync<TRequest>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
                new Uri(_baseUri, path),
                request,
                _jsonOptions,
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return await response.Content.ReadFromJsonAsync<TeamSession>(_jsonOptions, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new TeamRelayApiException(
                   "invalid_response",
                   "Relay trả về dữ liệu phiên không hợp lệ.",
                   (int)response.StatusCode);
    }

    private async Task EndCurrentSessionAsync(bool sendLeave, CancellationToken cancellationToken)
    {
        _intentionalStop = true;
        var heartbeatCancellation = Interlocked.Exchange(ref _heartbeatCancellation, null);
        var heartbeatTask = Interlocked.Exchange(ref _heartbeatTask, null);
        heartbeatCancellation?.Cancel();

        var connection = Interlocked.Exchange(ref _connection, null);
        var session = Interlocked.Exchange(ref _session, null);
        var leaveSent = false;

        if (sendLeave && connection?.State == HubConnectionState.Connected)
        {
            try
            {
                await connection.InvokeAsync("Leave", cancellationToken).ConfigureAwait(false);
                leaveSent = true;
            }
            catch
            {
            }
        }

        if (sendLeave && !leaveSent && session is not null)
        {
            await TryDeleteSessionAsync(session, cancellationToken).ConfigureAwait(false);
        }

        if (connection is not null)
        {
            try
            {
                await connection.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await connection.DisposeAsync()
                    .AsTask()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Cleanup must never keep create/join or app shutdown waiting forever.
            }
        }

        if (heartbeatTask is not null && heartbeatTask.Id != Task.CurrentId)
        {
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        heartbeatCancellation?.Dispose();
        lock (_stateGate)
        {
            _members.Clear();
            _mapPings.Clear();
        }
    }

    private void StartHeartbeat(TeamSession session)
    {
        var cancellation = new CancellationTokenSource();
        _heartbeatCancellation = cancellation;
        _heartbeatTask = RunHeartbeatAsync(
            TimeSpan.FromSeconds(Math.Max(1, session.HeartbeatIntervalSeconds)),
            cancellation.Token);
    }

    private async Task RequestSnapshotAsync(
        HubConnection? connection = null,
        CancellationToken cancellationToken = default)
    {
        connection ??= _connection;
        if (connection?.State != HubConnectionState.Connected)
            return;
        try
        {
            var snapshot = await connection.InvokeAsync<TeamSnapshot>("GetSnapshot", cancellationToken)
                .ConfigureAwait(false);
            ReceiveSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // A reconnect/next heartbeat will retry. Do not tear down the team
            // session for a single snapshot request failure.
        }
    }

    private async Task RunHeartbeatAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        var lastSnapshotAt = DateTimeOffset.UtcNow;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var connection = _connection;
                if (connection?.State != HubConnectionState.Connected)
                {
                    continue;
                }

                try
                {
                    await connection.InvokeAsync("Heartbeat", cancellationToken).ConfigureAwait(false);
                    if (DateTimeOffset.UtcNow - lastSnapshotAt >= TimeSpan.FromSeconds(10))
                    {
                        await RequestSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
                        lastSnapshotAt = DateTimeOffset.UtcNow;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (IsMemberExpired(exception))
                {
                    MarkExpired("Phiên nhóm đã hết hạn.");
                    break;
                }
                catch
                {
                    // SignalR reconnect handles transient network failures.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryDeleteSessionAsync(TeamSession session, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                new Uri(_baseUri, "api/v1/teams/me"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.MemberToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    internal void ReceiveSnapshot(TeamSnapshot snapshot)
    {
        lock (_stateGate)
        {
            if (snapshot.StateRevision > 0 && snapshot.StateRevision < _stateRevision)
                return;
            _stateRevision = Math.Max(_stateRevision, snapshot.StateRevision);
            _snapshotRevision = Math.Max(_snapshotRevision, snapshot.StateRevision);
            _mapPingRevision = Math.Max(_mapPingRevision, snapshot.StateRevision);
            if (snapshot.StateRevision > 0)
            {
                _receivedVersionedPingBatch = true;
                _receivedVersionedRemoval = true;
            }
            _members.Clear();
            _memberRevisions.Clear();
            _memberSequences.Clear();
            _memberRemovalRevisions.Clear();
            foreach (var member in snapshot.Members)
            {
                var observed = ObserveTelemetry(
                    member,
                    _state.Members.FirstOrDefault(previous => previous.MemberId == member.MemberId),
                    DateTimeOffset.UtcNow);
                _members[observed.MemberId] = observed;
                _memberRevisions[observed.MemberId] = observed.StateRevision;
                _memberSequences[observed.MemberId] = observed.Telemetry?.Sequence ?? -1;
            }
            _mapPings.Clear();
            foreach (var ping in snapshot.MapPings ?? [])
            {
                _mapPings[ping.PingId] = ping;
            }
        }

        SetState(TeamRelayConnectionState.Live);
    }

    internal void MemberUpdated(TeamMemberSnapshot member)
    {
        bool isLocalMember;
        lock (_stateGate)
        {
            var revision = member.StateRevision;
            var sequence = member.Telemetry?.Sequence ?? -1;
            if (_memberRemovalRevisions.TryGetValue(member.MemberId, out var removalRevision)
                && revision > 0
                && revision <= removalRevision)
                return;
            // The room revision is shared by every member. Updates from two
            // members can arrive in the opposite order without either being stale.
            if (revision > 0
                && !_members.ContainsKey(member.MemberId)
                && revision <= _snapshotRevision)
                return;
            if (_memberRevisions.TryGetValue(member.MemberId, out var previousRevision)
                && revision > 0 && previousRevision > revision)
                return;
            if (_memberSequences.TryGetValue(member.MemberId, out var previousSequence)
                && sequence >= 0 && previousSequence > sequence)
                return;
            if (_members.TryGetValue(member.MemberId, out var previousMember)
                && revision == previousMember.StateRevision
                && member.LastSeenAt < previousMember.LastSeenAt)
                return;
            _stateRevision = Math.Max(_stateRevision, revision);
            var observed = ObserveTelemetry(member, previousMember, DateTimeOffset.UtcNow);
            _members[member.MemberId] = observed;
            _memberRevisions[member.MemberId] = revision;
            _memberSequences[member.MemberId] = sequence;
            _memberRemovalRevisions.Remove(member.MemberId);
            isLocalMember = _session?.MemberId == member.MemberId;
        }

        // The sender already owns its local telemetry. Echoing that member back
        // into WPF at 10 Hz only invalidates the overlay while the camera turns.
        if (isLocalMember)
        {
            return;
        }

        SetState(CurrentState.ConnectionState is TeamRelayConnectionState.Reconnecting
            ? TeamRelayConnectionState.Reconnecting
            : TeamRelayConnectionState.Live);
    }

    internal static TeamMemberSnapshot ObserveTelemetry(
        TeamMemberSnapshot member,
        TeamMemberSnapshot? previous,
        DateTimeOffset now)
    {
        if (member.Telemetry is null)
            return member with { ClientTelemetryObservedAt = default };
        if (previous?.Telemetry?.Sequence == member.Telemetry.Sequence
            && previous.ClientTelemetryObservedAt != default)
            return member with { ClientTelemetryObservedAt = previous.ClientTelemetryObservedAt };
        return member with { ClientTelemetryObservedAt = now };
    }

    internal void MemberRemoved(Guid memberId)
    {
        lock (_stateGate)
        {
            if (_receivedVersionedRemoval)
                return;
            _members.Remove(memberId);
            _memberRevisions.Remove(memberId);
            _memberSequences.Remove(memberId);
        }

        SetState(CurrentState.ConnectionState);
    }

    internal void MemberRemoved(TeamMemberRemoval removal)
    {
        lock (_stateGate)
        {
            _receivedVersionedRemoval = true;
            if (removal.StateRevision > 0
                && (removal.StateRevision < _snapshotRevision
                    || _memberRevisions.TryGetValue(removal.MemberId, out var memberRevision)
                    && memberRevision > removal.StateRevision))
                return;
            if (removal.StateRevision > 0)
            {
                _stateRevision = Math.Max(_stateRevision, removal.StateRevision);
                _memberRemovalRevisions[removal.MemberId] = removal.StateRevision;
            }
            _members.Remove(removal.MemberId);
            _memberRevisions.Remove(removal.MemberId);
            _memberSequences.Remove(removal.MemberId);
        }

        SetState(CurrentState.ConnectionState);
    }

    internal void MapPingsChanged(IReadOnlyList<TeamMapPingSnapshot> mapPings)
    {
        lock (_stateGate)
        {
            if (_receivedVersionedPingBatch)
                return;
            _mapPings.Clear();
            foreach (var ping in mapPings)
            {
                _mapPings[ping.PingId] = ping;
            }
        }

        SetState(CurrentState.ConnectionState);
    }

    internal void MapPingsChanged(TeamMapPingBatch batch)
    {
        lock (_stateGate)
        {
            _receivedVersionedPingBatch = true;
            if (batch.StateRevision > 0 && batch.StateRevision < _mapPingRevision)
                return;
            _mapPingRevision = Math.Max(_mapPingRevision, batch.StateRevision);
            _stateRevision = Math.Max(_stateRevision, batch.StateRevision);
            _mapPings.Clear();
            foreach (var ping in batch.MapPings)
            {
                _mapPings[ping.PingId] = ping;
            }
        }

        SetState(CurrentState.ConnectionState);
    }

    private void MarkExpired(string message)
    {
        _heartbeatCancellation?.Cancel();
        SetState(
            TeamRelayConnectionState.Expired,
            message,
            clearSession: true,
            clearMembers: true);
    }

    private void SetState(
        TeamRelayConnectionState connectionState,
        string? message = null,
        bool clearSession = false,
        bool clearMembers = false)
    {
        TeamRelayState state;
        lock (_stateGate)
        {
            if (clearSession)
            {
                _session = null;
            }

            if (clearMembers)
            {
                _members.Clear();
                _mapPings.Clear();
                _memberRevisions.Clear();
                _memberSequences.Clear();
                _memberRemovalRevisions.Clear();
                _stateRevision = 0;
                _snapshotRevision = 0;
                _mapPingRevision = 0;
                _receivedVersionedPingBatch = false;
                _receivedVersionedRemoval = false;
            }

            state = new TeamRelayState
            {
                ConnectionState = connectionState,
                Session = _session,
                Members = _members.Values
                    .OrderBy(member => member.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray(),
                MapPings = _mapPings.Values
                    .OrderBy(ping => ping.CreatedAt)
                    .ThenBy(ping => ping.PingId)
                    .ToArray(),
                StateRevision = _stateRevision,
                Message = message
            };
            _state = state;
        }

        var handlers = StateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<TeamRelayState> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, state);
            }
            catch
            {
                // A view cannot be allowed to terminate the realtime session.
            }
        }
    }

    private async Task<TeamRelayApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        TeamApiError? apiError = null;
        try
        {
            apiError = await response.Content.ReadFromJsonAsync<TeamApiError>(
                    _jsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
        }

        var code = apiError?.Code ?? response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "rate_limited",
            HttpStatusCode.NotFound => "invite_not_found",
            HttpStatusCode.Conflict => "team_full",
            _ => "relay_error"
        };
        var message = apiError?.Message ?? $"Relay trả về HTTP {(int)response.StatusCode}.";
        return new TeamRelayApiException(code, message, (int)response.StatusCode);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private HubConnection RequireLiveConnection()
    {
        var connection = _connection;
        if (connection is null || connection.State != HubConnectionState.Connected)
        {
            throw new TeamMapPingException("relay_not_connected", "Relay nhóm chưa kết nối.");
        }

        return connection;
    }

    private TeamMapPingException MapPingException(Exception exception)
    {
        if (IsMemberExpired(exception))
        {
            MarkExpired("Phiên nhóm đã hết hạn.");
        }

        var codes = new[]
        {
            "invalid_ping",
            "ping_limit_reached",
            "ping_not_found",
            "ping_not_owned",
            "stale_ping_revision",
            "member_expired"
        };
        var code = codes.FirstOrDefault(value =>
            exception.Message.Contains(value, StringComparison.OrdinalIgnoreCase))
            ?? "relay_error";
        return new TeamMapPingException(code, code switch
        {
            "ping_limit_reached" => "Bạn đã đạt giới hạn ping của nhóm.",
            "ping_not_found" => "Ping không còn tồn tại.",
            "ping_not_owned" => "Chỉ chủ ping mới được sửa hoặc xóa.",
            "stale_ping_revision" => "Ping vừa được cập nhật ở nơi khác; hãy thử lại.",
            "member_expired" => "Phiên nhóm đã hết hạn.",
            "invalid_ping" => "Vị trí ping không hợp lệ.",
            _ => "Không thể đồng bộ ping qua relay."
        }, exception);
    }

    private static bool IsMemberExpired(Exception exception) =>
        exception.Message.Contains("member_expired", StringComparison.OrdinalIgnoreCase);

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.AbsoluteUri;
        return value.EndsWith('/') ? uri : new Uri(value + "/");
    }
}
