using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public sealed record MapNotePresentation
{
    public required Guid Id { get; init; }
    public required double U { get; init; }
    public required double V { get; init; }
    public required double WorldX { get; init; }
    public required double WorldY { get; init; }
    public required MapNoteKind Kind { get; init; }
    public required bool IsTeamPing { get; init; }
    public required bool CanEdit { get; init; }
    public long Revision { get; init; }
    public string? OwnerDisplayName { get; init; }
}

public static class MapNotePresentationBuilder
{
    public static IReadOnlyList<MapNotePresentation> Merge(
        IReadOnlyList<MapNote> localNotes,
        TeamRelayState? teamState)
    {
        var notes = new List<MapNotePresentation>(localNotes.Count + (teamState?.MapPings.Count ?? 0));
        notes.AddRange(localNotes.Select(note => new MapNotePresentation
        {
            Id = note.Id,
            U = note.U,
            V = note.V,
            WorldX = note.WorldX,
            WorldY = note.WorldY,
            Kind = note.Kind,
            IsTeamPing = false,
            CanEdit = true
        }));

        var localMemberId = teamState?.Session?.MemberId;
        foreach (var ping in teamState?.MapPings ?? [])
        {
            if (!IsValid(ping))
            {
                continue;
            }

            notes.Add(new MapNotePresentation
            {
                Id = ping.PingId,
                U = ping.MapLeft,
                V = ping.MapTop,
                WorldX = ping.WorldX,
                WorldY = ping.WorldY,
                Kind = (MapNoteKind)ping.Kind,
                IsTeamPing = true,
                CanEdit = localMemberId == ping.OwnerMemberId,
                Revision = ping.Revision,
                OwnerDisplayName = ping.OwnerDisplayName
            });
        }

        return notes;
    }

    public static TeamMapPingMutation Mutation(
        Guid id,
        long expectedRevision,
        MapNoteKind kind,
        double u,
        double v)
    {
        var point = new TheIsleOverlay.Core.MapPoint(
            Math.Clamp(u, 0d, 1d),
            Math.Clamp(v, 0d, 1d));
        var world = TheIsleOverlay.Core.GatewayMapProjection.Unproject(point);
        return new TeamMapPingMutation
        {
            PingId = id,
            ExpectedRevision = expectedRevision,
            MapId = MapNoteStore.GatewayMapId,
            Kind = (int)kind,
            MapLeft = point.Left,
            MapTop = point.Top,
            WorldX = world.X,
            WorldY = world.Y
        };
    }

    private static bool IsValid(TeamMapPingSnapshot ping) =>
        ping.PingId != Guid.Empty
        && ping.Revision > 0
        && string.Equals(ping.MapId, MapNoteStore.GatewayMapId, StringComparison.OrdinalIgnoreCase)
        && Enum.IsDefined(typeof(MapNoteKind), ping.Kind)
        && double.IsFinite(ping.MapLeft)
        && double.IsFinite(ping.MapTop)
        && ping.MapLeft is >= 0d and <= 1d
        && ping.MapTop is >= 0d and <= 1d;
}
