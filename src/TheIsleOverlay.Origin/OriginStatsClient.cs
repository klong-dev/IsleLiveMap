using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.Origin;

public enum OriginServerId
{
    Main,
    Voice
}

public sealed record OriginServer(OriginServerId Id, string ApiId, string DisplayName)
{
    public static IReadOnlyList<OriginServer> All { get; } =
    [
        new(OriginServerId.Main, "main", "Main Origin"),
        new(OriginServerId.Voice, "voice", "Voice Chat Server")
    ];
}

public sealed record OriginCommandResult(
    string Status,
    JsonElement? Result,
    string? Error,
    string? CommandId = null,
    DateTimeOffset? RequestedAt = null)
{
    public bool IsCompletedSuccessfully =>
        string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase)
        && Result is { } value
        && value.ValueKind == JsonValueKind.Object
        && (!value.TryGetProperty("success", out var success)
            || success.ValueKind != JsonValueKind.False);
}

/// <summary>
/// Small first-party client for the authenticated Origin dashboard command
/// API. It sends only the dashboard session cookie supplied by the host and
/// never reads another browser's storage.
/// </summary>
public interface IOriginStatsClient : IDisposable
{
    OriginServer? PreferredServer { get; }
    Task<OriginCommandResult> ExecuteHealthAsync(OriginServer server, CancellationToken cancellationToken = default);
    Task<OriginCommandResult> ExecutePrimeAsync(OriginServer server, CancellationToken cancellationToken = default);
}

public sealed class OriginStatsClient : IOriginStatsClient
{
    public static Uri BaseUri { get; } = new("https://playorigin.gg/");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _cookieHeader;
    private readonly TimeProvider _clock;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CommandLane> _lanes = new();
    private int _disposed;

    public OriginStatsClient(string cookieHeader, HttpClient? httpClient = null, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader)
            || cookieHeader.Length > 32_768
            || cookieHeader.Contains('\r')
            || cookieHeader.Contains('\n'))
        {
            throw new ArgumentException("Origin session cookie is invalid.", nameof(cookieHeader));
        }

        _cookieHeader = cookieHeader.Trim();
        _clock = timeProvider ?? TimeProvider.System;
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        _ownsClient = httpClient is null;
        PreferredServer = ResolvePreferredServer(_cookieHeader);
    }

    /// <summary>
    /// The dashboard persists its active server in the host-only
    /// <c>origin_server</c> cookie. It is only a hint: the session still
    /// probes the other server when the preferred one has no active dino.
    /// </summary>
    public OriginServer? PreferredServer { get; }

    public async Task<OriginCommandResult> ExecuteHealthAsync(
        OriginServer server,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteInLaneAsync("health", server, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OriginCommandResult> ExecutePrimeAsync(
        OriginServer server,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteInLaneAsync("getprime", server, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OriginCommandResult> ExecuteInLaneAsync(string command, OriginServer server, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var lane = _lanes.GetOrAdd($"{server.ApiId}/{command}", _ => new CommandLane());
        await lane.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A local timeout cannot cancel a command already queued on Origin.
            // Resume that id next time instead of enqueueing duplicate work.
            if (_clock.GetUtcNow() < lane.RetryAt)
                return new OriginCommandResult("pending", null, "Origin yêu cầu chờ trước khi thử lại.", lane.Pending?.CommandId);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(4));
            lane.Pending ??= await ExecuteCommandAsync(command, server, deadline.Token).ConfigureAwait(false);
            var result = (await PollCommandAsync(lane.Pending, deadline.Token).ConfigureAwait(false))
                with { RequestedAt = lane.Pending.RequestedAt };
            if (result.Status is "completed" or "failed") lane.Pending = null;
            return result;
        }
        catch (OriginRateLimitException exception)
        {
            lane.RetryAt = _clock.GetUtcNow() + exception.RetryAfter;
            return new OriginCommandResult("pending", null, "Origin đang giới hạn tần suất; sẽ tự thử lại.", lane.Pending?.CommandId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OriginCommandResult("pending", null, "Origin phản hồi chậm.", lane.Pending?.CommandId);
        }
        finally { lane.Gate.Release(); }
    }

    private sealed class CommandLane
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public OriginCommandResult? Pending { get; set; }
        public DateTimeOffset RetryAt { get; set; }
    }

    /// <summary>
    /// Resolves whether this authenticated account currently has an active
    /// dinosaur on either Origin server. The dashboard's active-server cookie
    /// is checked first, with a parallel fallback for the remaining server.
    /// </summary>
    public async Task<OriginServer?> DetectActiveServerAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (PreferredServer is { } preferred)
        {
            var preferredResult = await ExecuteHealthAsync(preferred, cancellationToken)
                .ConfigureAwait(false);
            if (HasActiveDinosaur(preferredResult))
            {
                return preferred;
            }
        }

        var candidates = OriginServer.All
            .Where(server => PreferredServer is null || !Equals(server, PreferredServer))
            .ToArray();
        var probes = await Task.WhenAll(candidates.Select(async server =>
        {
            try
            {
                var result = await ExecuteHealthAsync(server, cancellationToken)
                    .ConfigureAwait(false);
                return (Server: server, Active: HasActiveDinosaur(result));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OriginAuthenticationException)
            {
                throw;
            }
            catch
            {
                return (Server: server, Active: false);
            }
        })).ConfigureAwait(false);
        return probes.FirstOrDefault(probe => probe.Active).Server;
    }

    public async Task<OriginCommandResult> ExecuteCommandAsync(
        string command,
        OriginServer server,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var requestedAt = _clock.GetUtcNow();
        using var request = CreateRequest(HttpMethod.Post, "api/commands/execute");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { command, server = server.ApiId }),
            Encoding.UTF8,
            "application/json");
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureResponse(response);
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new OriginProtocolException("Origin command response is not an object.");
        }

        var id = ReadString(root, "id") ?? ReadString(root, "commandId");
        if (string.IsNullOrWhiteSpace(id))
        {
            var error = ReadString(root, "error") ?? "Origin did not return a command id.";
            throw new OriginProtocolException(error);
        }

        return new OriginCommandResult("queued", null, null, id, requestedAt);
    }

    private async Task<OriginCommandResult> PollCommandAsync(
        OriginCommandResult queued,
        CancellationToken cancellationToken)
    {
        var commandId = queued.CommandId;
        if (string.IsNullOrWhiteSpace(commandId))
        {
            throw new OriginProtocolException("Origin did not return a usable command id.");
        }
        var missing = 0;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), _clock, cancellationToken)
                .ConfigureAwait(false);
            using var request = CreateRequest(HttpMethod.Get, $"api/commands/{Uri.EscapeDataString(commandId)}");
            using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                missing++;
                continue;
            }

            EnsureResponse(response);
            var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var status = ReadString(root, "status") ?? ReadString(root, "state") ?? "failed";
            var result = root.TryGetProperty("result", out var resultValue)
                ? resultValue.Clone()
                : (JsonElement?)null;
            var error = result is { } resultObject
                ? ReadString(resultObject, "error") ?? ReadString(resultObject, "reason")
                : ReadString(root, "error");
            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return new OriginCommandResult(status.ToLowerInvariant(), result, error, commandId);
            }
        }

        if (missing == 12) return new OriginCommandResult("failed", null, "Origin command no longer exists.", commandId);
        return new OriginCommandResult(
            "pending", null, "Origin command is still pending.", commandId);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, path));
        request.Headers.TryAddWithoutValidation("Cookie", _cookieHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }

    private static bool HasActiveDinosaur(OriginCommandResult command)
    {
        if (!command.IsCompletedSuccessfully || command.Result is not { } result)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(ReadString(result, "species"))
               || !string.IsNullOrWhiteSpace(ReadString(result, "dino"));
    }

    internal static bool IsTrustedUri(Uri? uri) =>
        uri is not null
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Port == 443
        && string.Equals(uri.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase);

    private static OriginServer? ResolvePreferredServer(string cookieHeader)
    {
        foreach (var segment in cookieHeader.Split(';'))
        {
            var pair = segment.Trim();
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = pair[..separator].Trim();
            if (!string.Equals(name, "origin_server", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = pair[(separator + 1)..].Trim();
            try
            {
                value = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return null;
            }

            return OriginServer.All.FirstOrDefault(server =>
                string.Equals(server.ApiId, value, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static void EnsureResponse(HttpResponseMessage response)
    {
        var uri = response.RequestMessage?.RequestUri;
        if (!IsTrustedUri(uri))
        {
            throw new OriginProtocolException("Origin response came from an untrusted host.");
        }

        // The API is same-origin and should never redirect an authenticated
        // command. Reject redirects explicitly so a proxy or a compromised
        // endpoint cannot move the session cookie to another host.
        if ((int)response.StatusCode is >= 300 and <= 399)
        {
            var location = response.Headers.Location;
            var redirect = location is null
                ? null
                : location.IsAbsoluteUri ? location : new Uri(uri!, location);
            if (!IsTrustedUri(redirect))
            {
                throw new OriginProtocolException("Origin redirected to an untrusted host.");
            }

            throw new OriginProtocolException("Origin command returned an unexpected redirect.");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new OriginAuthenticationException("Origin session has expired.");
        }
        if ((int)response.StatusCode == 429)
        {
            var delay = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(10));
            throw new OriginRateLimitException(delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1));
        }

        response.EnsureSuccessStatusCode();
    }

    private static string? ReadString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

public sealed class OriginAuthenticationException(string message) : Exception(message);

public sealed class OriginProtocolException(string message) : Exception(message);

public sealed class OriginRateLimitException(TimeSpan retryAfter) : Exception("Origin rate limit")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}
