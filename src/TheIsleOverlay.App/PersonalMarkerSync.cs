using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

/// <summary>Sequential reconciliation; no website credentials or background tracking upload.</summary>
public sealed class PersonalMarkerSync : IDisposable
{
    private readonly HttpClient _http;
    private readonly Dictionary<Guid, MapNoteKind> _uploaded = [];
    private readonly HashSet<Guid> _rejected = [];
    private string? _token;
    private readonly PersonalMarkerCredentialStore? _credentials;
    private bool _loadedRemote;
    private DateTimeOffset _nextProbe;
    public PersonalMarkerSync(HttpClient? http = null, PersonalMarkerCredentialStore? credentials = null)
    {
        _credentials = credentials ?? (http is null ? new PersonalMarkerCredentialStore(Path.Combine(AppPaths.Root, "personal-markers.credential")) : null);
        _token = _credentials?.Load();
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("https://isle-relay.klong.dev/api/v1/personal-markers/"),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task ReconcileAsync(IReadOnlyList<MapNote> notes, CancellationToken cancellationToken)
    {
        if (_token is null)
        {
            using var create = await _http.PostAsync("sessions", null, cancellationToken);
            create.EnsureSuccessStatusCode();
            var session = await create.Content.ReadFromJsonAsync<Session>(cancellationToken);
            _token = session?.Token ?? throw new HttpRequestException("Relay không trả phiên.");
            _credentials?.Save(_token);
            _uploaded.Clear(); _rejected.Clear();
            _loadedRemote = false;
        }
        if (!_loadedRemote || DateTimeOffset.UtcNow >= _nextProbe)
        {
            using var response = await SendAsync(HttpMethod.Get, "me/", null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var existing = await response.Content.ReadFromJsonAsync<RemoteMarker[]>(cancellationToken) ?? [];
            foreach (var marker in existing) _uploaded[marker.Id] = marker.DeathConfirmed ? MapNoteKind.Death : MapNoteKind.LastKnown;
            _loadedRemote = true;
            _nextProbe = DateTimeOffset.UtcNow.AddSeconds(30);
        }
        var active = notes.Where(n => n.IsPersonalHistory && n.ExpiresAt > DateTimeOffset.UtcNow).Take(20).ToArray();
        foreach (var id in _uploaded.Keys.Where(id => active.All(n => n.Id != id)).ToArray())
        {
            using var response = await SendAsync(HttpMethod.Delete, $"me/{id}", null, cancellationToken);
            if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
            _uploaded.Remove(id);
        }
        foreach (var note in active)
        {
            if (_rejected.Contains(note.Id)) continue;
            if (!_uploaded.ContainsKey(note.Id))
            {
                using var response = await SendAsync(HttpMethod.Put, $"me/{note.Id}",
                    new { note.ServerKey, note.MapId, X = note.WorldX, Y = note.WorldY, ObservedAt = note.CreatedAt }, cancellationToken);
                if (response.StatusCode == HttpStatusCode.Gone) { _rejected.Add(note.Id); continue; }
                response.EnsureSuccessStatusCode();
                _uploaded[note.Id] = MapNoteKind.LastKnown;
            }
            if (note.Kind == MapNoteKind.Death && _uploaded[note.Id] != MapNoteKind.Death)
            {
                using var response = await SendAsync(HttpMethod.Post, $"me/{note.Id}/confirm-death", null, cancellationToken);
                response.EnsureSuccessStatusCode();
                _uploaded[note.Id] = note.Kind;
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) _token = null;
        return response;
    }
    public void Dispose() => _http.Dispose();
    private sealed record Session(string Token);
    private sealed record RemoteMarker(Guid Id, bool DeathConfirmed);
}
