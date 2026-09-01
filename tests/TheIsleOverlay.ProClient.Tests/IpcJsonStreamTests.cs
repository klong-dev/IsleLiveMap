using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.ProClient.Tests;

public sealed class IpcJsonStreamTests
{
    [Fact]
    public async Task RoundTrip_PreservesHostHello()
    {
        await using var memory = new MemoryStream();
        await using var ipc = new IpcJsonStream(memory);
        var expected = new HostHello(
            ProAgentProtocol.IpcApiMajor,
            "1.4.0",
            "signed-license");

        await ipc.WriteAsync(expected, TestContext.Current.CancellationToken);
        memory.Position = 0;
        var actual = await ipc.ReadAsync<HostHello>(TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RoundTrip_PreservesClassifiedEntityMetadata()
    {
        await using var memory = new MemoryStream();
        await using var ipc = new IpcJsonStream(memory);
        var observedAt = DateTimeOffset.Parse("2026-08-26T10:15:30Z");
        var expected = new ProTelemetryFrame(
            42,
            observedAt,
            "127.0.0.1:7777",
            new WorldPosition(100, 200, 30),
            135,
            [
                new VerifiedMapEntity(
                    17,
                    MapEntityKind.Player,
                    "internal-proof-only",
                    "Carnotaurus",
                    "Carno",
                    MapCreatureDiet.Carnivore,
                    2_300,
                    new WorldPosition(125, 225, 31),
                    35.36,
                    9,
                    observedAt),
                new VerifiedMapEntity(
                    18,
                    MapEntityKind.Ai,
                    null,
                    "Boar",
                    "Boar",
                    MapCreatureDiet.Omnivore,
                    null,
                    new WorldPosition(80, 220, 30),
                    28.28,
                    1,
                    observedAt,
                    IsProvisional: true)
            ],
            "carnotaurus",
            "Carno",
            new PlayerSyncState(
                true, 1, 1, 4, 2, 2, 0, 3));

        await ipc.WriteAsync(expected, TestContext.Current.CancellationToken);
        memory.Position = 0;
        var actual = await ipc.ReadAsync<ProTelemetryFrame>(TestContext.Current.CancellationToken);

        Assert.Equal(expected.Sequence, actual.Sequence);
        Assert.Equal(expected.ObservedAt, actual.ObservedAt);
        Assert.Equal(expected.ServerEndpoint, actual.ServerEndpoint);
        Assert.Equal(expected.LocalLocation, actual.LocalLocation);
        Assert.Equal(expected.MapHeadingDegrees, actual.MapHeadingDegrees);
        Assert.Equal(expected.RemoteEntities, actual.RemoteEntities);
        Assert.Equal(expected.LocalSpeciesId, actual.LocalSpeciesId);
        Assert.Equal(expected.LocalSpeciesShortName, actual.LocalSpeciesShortName);
        Assert.Equal(expected.PlayerSync, actual.PlayerSync);
        Assert.True(actual.RemoteEntities[1].IsProvisional);
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedLength()
    {
        await using var memory = new MemoryStream(
            BitConverter.GetBytes(ProAgentProtocol.MaximumFrameBytes + 1));
        await using var ipc = new IpcJsonStream(memory);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await ipc.ReadAsync<AgentMessage>(TestContext.Current.CancellationToken));
    }
}
