using System.Windows.Media;
using System.Windows.Shapes;

namespace TheIsleOverlay.App;

public sealed record MapNotePaletteItem(
    MapNoteKind? Kind,
    string Label,
    string Color,
    string GeometryData)
{
    public bool IsDelete => Kind is null;
}

public static class MapNoteIconCatalog
{
    public static IReadOnlyList<MapNotePaletteItem> Palette { get; } =
    [
        new(null, "Xóa mốc", "#EF6461", "M4,5 L16,5 L15,19 L5,19 Z M2,2 L18,2 L18,5 L2,5 Z M7,0 L13,0 L14,2 L6,2 Z"),
        new(MapNoteKind.Pin, "Mốc thường", "#E7B74E", "M10,0 C4.5,0 1,4 1,9 C1,15 10,21 10,21 C10,21 19,15 19,9 C19,4 15.5,0 10,0 Z M10,5 C7.8,5 6,6.8 6,9 C6,11.2 7.8,13 10,13 C12.2,13 14,11.2 14,9 C14,6.8 12.2,5 10,5 Z"),
        new(MapNoteKind.Rally, "Điểm tập hợp", "#37D4C6", "M4,1 L6,1 L6,21 L4,21 Z M6,2 L18,5 L6,10 Z M1,21 L11,21 L11,23 L1,23 Z"),
        new(MapNoteKind.Water, "Nguồn nước", "#4B9BD4", "M10,0 C10,0 2,10 2,15 C2,20 5.6,23 10,23 C14.4,23 18,20 18,15 C18,10 10,0 10,0 Z"),
        new(MapNoteKind.Meat, "Thức ăn ăn thịt", "#D8874D", "M6,5 C3,2 0,4 2,7 L7,12 C9,14 12,14 14,12 L18,8 C21,5 18,1 15,3 L11,6 C9,8 8,7 6,5 Z M3,14 L6,17 L3,20 C1,22 -1,19 1,17 Z"),
        new(MapNoteKind.Plant, "Thức ăn ăn cỏ", "#83C66B", "M19,1 C10,1 3,5 3,12 C3,16 6,19 10,19 C16,19 20,12 19,1 Z M3,22 C7,14 11,10 17,5"),
        new(MapNoteKind.Nest, "Vị trí tổ", "#E8D9A9", "M1,15 C5,19 15,19 20,15 L18,20 C13,23 7,23 3,20 Z M6,12 C6,6 8,2 11,2 C14,2 16,6 16,12 C13,14 9,14 6,12 Z"),
        new(MapNoteKind.Danger, "Nguy hiểm", "#F04444", "M11,1 L21,20 L1,20 Z M10,7 L12,7 L12,14 L10,14 Z M10,16 L12,16 L12,18 L10,18 Z"),
        new(MapNoteKind.Sighting, "Phát hiện player", "#B09CEC", "M1,11 C5,4 17,4 21,11 C17,18 5,18 1,11 Z M11,7 C8.8,7 7,8.8 7,11 C7,13.2 8.8,15 11,15 C13.2,15 15,13.2 15,11 C15,8.8 13.2,7 11,7 Z")
    ];

    public static MapNotePaletteItem For(MapNoteKind kind) =>
        Palette.First(item => item.Kind == kind);

    public static Path CreatePath(MapNotePaletteItem item, double size)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(item.Color));
        brush.Freeze();
        return new Path
        {
            Data = Geometry.Parse(item.GeometryData),
            Fill = brush,
            Stroke = brush,
            StrokeThickness = item.Kind == MapNoteKind.Plant ? 1.15d : 0.35d,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            IsHitTestVisible = false
        };
    }
}
