using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal static class RemotePlayerMapMarkerResolver
{
    public static IReadOnlyList<RemotePlayerMapMarker> Resolve(
        MapTelemetry? map,
        PlayerTelemetry? localPlayer)
    {
        if (map?.Markers is not { Count: > 0 } markers)
        {
            return [];
        }

        var duplicateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<RemotePlayerMapMarker>(markers.Count);

        foreach (var marker in markers)
        {
            if (marker.SteamId is null ||
                !marker.SteamId.StartsWith("pro-entity:", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(marker.Label)
                || marker.ProEntityKind is not { } entityKind
                || !TryResolveCategory(
                    marker,
                    localPlayer,
                    entityKind,
                    out var category))
            {
                continue;
            }

            var point = GatewayMapProjection.ResolveForBundledTexture(
                marker.Location,
                marker.MapLocation);
            if (point is not { } resolvedPoint
                || IsExplicitlyLocalMarker(marker, localPlayer))
            {
                continue;
            }

            var baseKey = MarkerBaseKey(marker, resolvedPoint);
            var duplicateIndex = duplicateCounts.GetValueOrDefault(baseKey);
            duplicateCounts[baseKey] = duplicateIndex + 1;
            result.Add(new RemotePlayerMapMarker(
                $"{baseKey}#{duplicateIndex}",
                marker.Label,
                resolvedPoint,
                category,
                entityKind,
                marker.ProEntityIsProvisional,
                marker.ProEntityIsStale));
        }

        return result;
    }

    private static bool TryResolveCategory(
        MapMarkerTelemetry marker,
        PlayerTelemetry? localPlayer,
        RemoteEntityKind entityKind,
        out RemoteEntityMapCategory category)
    {
        if (entityKind == RemoteEntityKind.Ai)
        {
            category = RemoteEntityMapCategory.Ai;
            return true;
        }

        if (entityKind != RemoteEntityKind.Player)
        {
            category = default;
            return false;
        }

        if (string.IsNullOrWhiteSpace(marker.CreatureSpeciesId))
        {
            category = RemoteEntityMapCategory.UnclassifiedPlayer;
            return true;
        }

        if (CreatureSpeciesIdentity.Normalize(localPlayer?.Class) is { Length: > 0 }
            && CreatureSpeciesIdentity.AreSame(
                marker.CreatureSpeciesId,
                localPlayer?.Class))
        {
            category = RemoteEntityMapCategory.SameSpecies;
            return true;
        }

        switch (marker.ProCreatureDiet)
        {
            case CreatureDiet.Carnivore:
                category = RemoteEntityMapCategory.OtherCarnivore;
                return true;
            case CreatureDiet.Herbivore:
                category = RemoteEntityMapCategory.OtherHerbivore;
                return true;
            default:
                category = RemoteEntityMapCategory.UnclassifiedPlayer;
                return true;
        }
    }

    private static bool IsExplicitlyLocalMarker(
        MapMarkerTelemetry marker,
        PlayerTelemetry? localPlayer)
    {
        if (marker.Self)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(marker.SteamId)
            && !string.IsNullOrWhiteSpace(localPlayer?.SteamId)
            && string.Equals(
                marker.SteamId.Trim(),
                localPlayer.SteamId.Trim(),
                StringComparison.Ordinal))
        {
            return true;
        }

        // Never infer local identity from coordinate overlap. Several real
        // remote actors can occupy the same projected point as the local
        // player, and hiding them here creates a silent missing-marker bug.
        // A marker is local only when the upstream pipeline explicitly marks
        // it Self or supplies the same authoritative identity.
        return false;
    }

    private static string MarkerBaseKey(MapMarkerTelemetry marker, MapPoint point)
    {
        if (!string.IsNullOrWhiteSpace(marker.SteamId))
        {
            return $"steam:{marker.SteamId.Trim()}";
        }

        return $"label:{marker.Label!.Trim()}";
    }
}

internal readonly record struct RemotePlayerMapMarker(
    string Key,
    string? Label,
    MapPoint Point,
    RemoteEntityMapCategory Category,
    RemoteEntityKind EntityKind,
    bool IsProvisional,
    bool IsStale);

internal enum RemoteEntityMapCategory
{
    OtherCarnivore,
    SameSpecies,
    OtherHerbivore,
    Ai,
    UnclassifiedPlayer
}
