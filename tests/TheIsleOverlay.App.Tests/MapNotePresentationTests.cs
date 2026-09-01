using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App.Tests;

public sealed class MapNotePresentationTests
{
    [Fact]
    public void MergeMarksOnlyTheCurrentMembersTeamPingsAsEditable()
    {
        var ownerId = Guid.NewGuid();
        var peerId = Guid.NewGuid();
        var local = new MapNote { U = 0.1, V = 0.2, Kind = MapNoteKind.Water };
        var state = new TeamRelayState
        {
            ConnectionState = TeamRelayConnectionState.Live,
            Session = new TeamSession(Guid.NewGuid(), ownerId, "ABC123", "token", 10, 5),
            MapPings =
            [
                Ping(Guid.NewGuid(), ownerId, "Owner", 0.3, 0.4),
                Ping(Guid.NewGuid(), peerId, "Peer", 0.5, 0.6)
            ]
        };

        var notes = MapNotePresentationBuilder.Merge([local], state);

        Assert.Equal(3, notes.Count);
        Assert.True(Assert.Single(notes, note => !note.IsTeamPing).CanEdit);
        Assert.True(Assert.Single(notes, note => note.OwnerDisplayName == "Owner").CanEdit);
        Assert.False(Assert.Single(notes, note => note.OwnerDisplayName == "Peer").CanEdit);
    }

    [Fact]
    public void MutationUsesCalibratedWorldCoordinatesAndExpectedRevision()
    {
        var id = Guid.NewGuid();
        var mutation = MapNotePresentationBuilder.Mutation(
            id,
            expectedRevision: 4,
            MapNoteKind.Danger,
            0.25,
            0.75);

        var projected = TheIsleOverlay.Core.GatewayMapProjection.Project(
            new TheIsleOverlay.Core.WorldLocation
            {
                X = mutation.WorldX,
                Y = mutation.WorldY
            });
        Assert.Equal(id, mutation.PingId);
        Assert.Equal(4, mutation.ExpectedRevision);
        Assert.Equal((int)MapNoteKind.Danger, mutation.Kind);
        Assert.Equal(0.25, projected.Left, 8);
        Assert.Equal(0.75, projected.Top, 8);
    }

    private static TeamMapPingSnapshot Ping(
        Guid id,
        Guid ownerId,
        string owner,
        double left,
        double top) => new(
        id,
        ownerId,
        owner,
        1,
        "gateway",
        (int)MapNoteKind.Pin,
        left,
        top,
        10,
        20,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);
}
