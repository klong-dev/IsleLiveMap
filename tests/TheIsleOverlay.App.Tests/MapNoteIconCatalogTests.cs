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
        Assert.Equal(Enum.GetValues<MapNoteKind>().Order(), kinds.Order());
        Assert.Equal(kinds.Length, kinds.Distinct().Count());
    }
}
