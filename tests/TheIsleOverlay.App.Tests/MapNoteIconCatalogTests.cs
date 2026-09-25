namespace TheIsleOverlay.App.Tests;

public sealed class MapNoteIconCatalogTests
{
    [Fact]
    public void Palette_StartsWithDeleteAndCoversEveryPersistedKindExactlyOnce()
    {
        Assert.True(MapNoteIconCatalog.Palette[0].IsDelete);
        Assert.Equal("Xóa mốc", MapNoteIconCatalog.Palette[0].Label);

        var kinds = MapNoteIconCatalog.Palette
            .Where(item => item.Kind is not null)
            .Select(item => item.Kind!.Value)
            .ToArray();
        Assert.Equal(Enum.GetValues<MapNoteKind>().Where(k => k is not MapNoteKind.LastKnown and not MapNoteKind.Death).Order(), kinds.Order());
        Assert.NotNull(MapNoteIconCatalog.For(MapNoteKind.LastKnown));
        Assert.NotNull(MapNoteIconCatalog.For(MapNoteKind.Death));
        Assert.Equal(kinds.Length, kinds.Distinct().Count());
    }
}
