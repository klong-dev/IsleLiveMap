using System.Net;
using System.Net.Http;
using System.Text;

namespace TheIsleOverlay.App.Tests;

public sealed class PersonalMarkerSyncTests
{
    [Fact]
    public async Task OfflineDeleteIsReconciledAfterRestartAndGoneIsNeverReuploaded()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var handler = new Handler(id, other);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://fixture/") };
        using var sync = new PersonalMarkerSync(http);
        var note = new MapNote { Id = id, Kind = MapNoteKind.LastKnown, ServerKey = "server", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        await sync.ReconcileAsync([note], CancellationToken.None);
        Assert.Contains("DELETE /me/" + other, handler.Calls);
        await sync.ReconcileAsync([note with { Kind = MapNoteKind.Death }], CancellationToken.None);
        Assert.Contains("POST /me/" + id + "/confirm-death", handler.Calls);
        await sync.ReconcileAsync([], CancellationToken.None);
        var calls = handler.Calls.Count;
        await sync.ReconcileAsync([note], CancellationToken.None); // replay receives 410
        await sync.ReconcileAsync([note], CancellationToken.None);
        Assert.Equal(calls + 1, handler.Calls.Count);
    }

    private sealed class Handler(Guid id, Guid other) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var route = request.RequestUri!.AbsolutePath;
            Calls.Add(request.Method + " " + route);
            if (route == "/sessions")
                return Task.FromResult(Json("{\"token\":\"" + new string('A', 64) + "\"}"));
            Assert.Equal(new string('A', 64), request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains("Cookie"));
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Json($"[{{\"id\":\"{id}\",\"deathConfirmed\":false}},{{\"id\":\"{other}\",\"deathConfirmed\":false}}]"));
            if (request.Method == HttpMethod.Put) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone));
            return Task.FromResult(Json("{}"));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
