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
                    IsProvisional: true,
                    ActorNetRefHandle: 9012,
                    PlayerStateNetRefHandle: 9014,
                    PawnNetRefHandle: 9016)
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
        Assert.Equal((ulong)9012, actual.RemoteEntities[1].ActorNetRefHandle);
        Assert.Equal((ulong)9014, actual.RemoteEntities[1].PlayerStateNetRefHandle);
        Assert.Equal((ulong)9016, actual.RemoteEntities[1].PawnNetRefHandle);
    }

    [Fact]
    public async Task RoundTrip_PreservesAnonymousStructuralPlayerProof()
    {
        await using var memory = new MemoryStream();
        await using var ipc = new IpcJsonStream(memory);
        var observedAt = DateTimeOffset.Parse("2026-09-22T07:00:00Z");
        var expected = new ProTelemetryFrame(
            43,
            observedAt,
            "127.0.0.1:7777",
            new WorldPosition(100, 200, 30),
            0,
            [
                new VerifiedMapEntity(
                    112854,
                    MapEntityKind.Player,
                    null,
                    "rex",
                    "T-Rex",
                    MapCreatureDiet.Carnivore,
                    null,
                    new WorldPosition(101, 201, 30),
                    1.41,
                    4,
                    observedAt,
                    IsProvisional: false,
                    LocationObservedAt: observedAt,
                    ActorNetRefHandle: 112854,
                    PlayerStateNetRefHandle: 53328,
                    PawnNetRefHandle: 53330)
            ]);

        await ipc.WriteAsync(expected, TestContext.Current.CancellationToken);
        memory.Position = 0;
        var actual = await ipc.ReadAsync<ProTelemetryFrame>(
            TestContext.Current.CancellationToken);

        var player = Assert.Single(actual.RemoteEntities);
        Assert.Equal(MapEntityKind.Player, player.Kind);
        Assert.Null(player.PlayerProofName);
        Assert.Equal((ulong)112854, player.ActorNetRefHandle);
        Assert.Equal((ulong)53328, player.PlayerStateNetRefHandle);
        Assert.Equal((ulong)53330, player.PawnNetRefHandle);
        Assert.Equal("T-Rex", player.SpeciesShortName);
    }

    [Fact]
    public async Task RoundTrip_PreservesVerifiedPositionProofWithoutName()
    {
        await using var memory = new MemoryStream();
        await using var ipc = new IpcJsonStream(memory);
        var observedAt = DateTimeOffset.Parse("2026-09-23T07:00:00Z");
        var expected = new ProTelemetryFrame(
            44, observedAt, "127.0.0.1:7777", new WorldPosition(1, 2, 3), 0,
            [new VerifiedMapEntity(
                7, MapEntityKind.Player, null, "triceratops", "Trice",
                MapCreatureDiet.Herbivore, null, new WorldPosition(4, 5, 6), 0,
                3, observedAt, LocationObservedAt: observedAt,
                ActorNetRefHandle: 7, PlayerStateNetRefHandle: 8,
                PawnNetRefHandle: 9, HasVerifiedPosition: true)]);

        await ipc.WriteAsync(expected, TestContext.Current.CancellationToken);
        memory.Position = 0;
        var actual = await ipc.ReadAsync<ProTelemetryFrame>(TestContext.Current.CancellationToken);

        var entity = Assert.Single(actual.RemoteEntities);
        Assert.True(entity.HasVerifiedPosition);
        Assert.Null(entity.PlayerProofName);
        Assert.Equal((ulong)8, entity.PlayerStateNetRefHandle);
    }

    [Fact]
    public async Task RoundTrip_PreservesCaptureHealthStatus()
    {
        await using var memory = new MemoryStream();
        await using var ipc = new IpcJsonStream(memory);
        var observedAt = DateTimeOffset.Parse("2026-09-08T07:30:00Z");
        var expected = new AgentMessage(
            "capture-status",
            null,
            null,
            null,
            new AgentCaptureStatus(
                "receiving",
                true,
                2,
                3,
                1_024,
                observedAt,
                null));

        await ipc.WriteAsync(expected, TestContext.Current.CancellationToken);
        memory.Position = 0;
        var actual = await ipc.ReadAsync<AgentMessage>(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RoundTrip_PreservesEarlyDinoPositionEvidence()
    {
        var at = DateTimeOffset.UtcNow;
        var entity = new VerifiedMapEntity(500, MapEntityKind.Player, null, "", "",
            MapCreatureDiet.Unknown, null, new WorldPosition(100, 200, 30), 0, 1, at,
            IsProvisional: true, LocationObservedAt: at, ActorNetRefHandle: 500,
            HasVerifiedPosition: true, LocationEvidenceSource: "SerializedActorCreation",
            LocationEvidenceEndBitOffset: 100);
        var expected = new ProTelemetryFrame(1, at, "server:7777", new WorldPosition(0, 0, 0), 0, [entity]);
        await using var memory = new MemoryStream();
        await using var ipc = new IpcJsonStream(memory);
        await ipc.WriteAsync(expected, TestContext.Current.CancellationToken);
        memory.Position = 0;
        var wire = await ipc.ReadAsync<ProTelemetryFrame>(TestContext.Current.CancellationToken);
        var mapped = Assert.Single(ProAgentRemotePlayerSource.MapFrame(wire, "session").RemoteEntities);
        Assert.True(mapped.IsProvisional);
        Assert.Equal("", mapped.SpeciesId);
        Assert.Equal("SerializedActorCreation", mapped.LocationEvidenceSource);
        Assert.Equal(100, mapped.LocationEvidenceEndBitOffset);
        Assert.Equal(at, mapped.LocationObservedAt);
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
