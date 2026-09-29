using System.Text;
using System.Text.Json;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class TrackingRecorderTests
{
    [Fact]
    public void HostPathCannotAliasAgentPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "tracking-agent.jsonl");
        Assert.Null(ProAgentRemotePlayerSource.ResolveHostComparisonPath(path, path.ToUpperInvariant()));
        Assert.Null(ProAgentRemotePlayerSource.ResolveHostComparisonPath(null, path));
        var host = Path.ChangeExtension(path, "host.jsonl");
        Assert.Equal(host, ProAgentRemotePlayerSource.ResolveHostComparisonPath(host, path));
    }

    [Fact]
    public async Task ConcurrentStageWritersProduceSeparateWholeRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracking-recorder-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var agentPath = Path.Combine(directory, "agent-live-compare.jsonl");
            var hostPath = Path.Combine(directory, "host-ipc-compare.jsonl");
            await using var source = new ProAgentRemotePlayerSource(
                Path.Combine(directory, "agent.exe"), "2.3.5", "fixture-account", "fixture-token", hostPath);
            var at = DateTimeOffset.UtcNow;
            // Long records cross the StreamWriter buffer, reproducing the
            // boundary at which two independent writers used to interleave.
            await Task.WhenAll(
                Task.Run(() =>
                {
                    using var agent = new StreamWriter(new FileStream(agentPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                    for (var i = 1; i <= 30; i++)
                        agent.WriteLine(JsonSerializer.Serialize(new { stage = "agent", sequence = i, payload = new string('x', 8192) }));
                }, TestContext.Current.CancellationToken),
                Task.Run(() =>
                {
                    for (var i = 1; i <= 30; i++) source.WriteLiveCompare(Frame(i, at), "session-a");
                }, TestContext.Current.CancellationToken));
            await source.DisposeAsync();
            var agentLines = File.ReadAllLines(agentPath);
            var hostLines = File.ReadAllLines(hostPath);
            Assert.Equal(30, agentLines.Length);
            Assert.Equal(30, hostLines.Length);
            foreach (var line in agentLines)
            {
                using var row = JsonDocument.Parse(line);
                Assert.Equal("agent", row.RootElement.GetProperty("stage").GetString());
            }
            foreach (var line in hostLines)
            {
                using var row = JsonDocument.Parse(line);
                Assert.Equal("host-ipc-received", row.RootElement.GetProperty("Stage").GetString());
                Assert.Equal("session-a", row.RootElement.GetProperty("SessionId").GetString());
                Assert.DoesNotContain("fixture-token", line);
                Assert.DoesNotContain("fixture-account", line);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SessionIdentitySurvivesIpcMappingAndSequenceRestart()
    {
        var frame = Frame(1, DateTimeOffset.UtcNow);
        var first = ProAgentRemotePlayerSource.MapFrame(frame, "session-a");
        var restarted = ProAgentRemotePlayerSource.MapFrame(frame, "session-b");
        Assert.Equal(first.Sequence, restarted.Sequence);
        Assert.NotEqual(first.SessionId, restarted.SessionId);
        Assert.Equal("session-b", restarted.SessionId);
        Assert.Null(Assert.Single(first.RemoteEntities).PlayerProofName);
        Assert.Equal(42UL, first.RemoteEntities[0].ActorNetRefHandle);
    }

    private static ProTelemetryFrame Frame(long sequence, DateTimeOffset at) => new(
        sequence, at, "fixture:7777", new WorldPosition(100, 200, 30), 90,
        [new VerifiedMapEntity(42, MapEntityKind.Player, null, "rex", new string('r', 8192),
            MapCreatureDiet.Carnivore, null, new WorldPosition(110, 210, 30), 14, 3, at,
            LocationObservedAt: at, ActorNetRefHandle: 42, PlayerStateNetRefHandle: 43, PawnNetRefHandle: 44)]);
}
