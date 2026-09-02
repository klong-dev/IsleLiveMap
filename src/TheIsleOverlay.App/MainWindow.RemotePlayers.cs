using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private const double RemotePlayerDotSize = 7d;
    private const double RemotePlayerMarkerHeight = 18d;

    private readonly Dictionary<string, RemotePlayerMapDot> _remotePlayerMapDots =
        new(StringComparer.Ordinal);
    private IReadOnlyList<RemotePlayerMapMarker> _renderedRemotePlayerMarkers = [];

    private bool SyncRemotePlayerMarkers(TelemetrySnapshot snapshot)
    {
        if (!HasCurrentProFeatures)
        {
            var changed = _renderedRemotePlayerMarkers.Count > 0
                          || _remotePlayerMapDots.Count > 0
                          || RemotePlayerCountLabel.Visibility != Visibility.Collapsed
                          || RemoteEntityLegend.Visibility != Visibility.Collapsed;
            if (changed)
            {
                ClearRemotePlayerMarkers();
            }
            return changed;
        }

        var markers = RemotePlayerMapMarkerResolver.Resolve(snapshot.Map, snapshot.Player);
        var visibleKeys = new HashSet<string>(StringComparer.Ordinal);
        var playerCount = markers.Count(marker =>
            marker.EntityKind == RemoteEntityKind.Player && !marker.IsProvisional);
        var provisionalCount = markers.Count(marker =>
            marker.EntityKind == RemoteEntityKind.Player && marker.IsProvisional);
        var aiCount = markers.Count(marker => marker.EntityKind == RemoteEntityKind.Ai);
        var isSynchronizing = snapshot.ProPlayerSync?.IsSynchronizing == true;
        RemotePlayerCountLabel.Text = provisionalCount > 0
            ? $"PLAYER {playerCount} · ĐANG XÁC MINH {provisionalCount} · AI {aiCount}"
            : isSynchronizing
                ? $"PLAYER {playerCount} · ĐANG ĐỒNG BỘ · AI {aiCount}"
            : $"PLAYER {playerCount} · AI {aiCount}";
        RemotePlayerCountLabel.Visibility = snapshot.ProPlayerTrackingActive
            ? Visibility.Visible
            : Visibility.Collapsed;
        RemoteEntityLegend.Visibility = snapshot.ProPlayerTrackingActive
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_renderedRemotePlayerMarkers.SequenceEqual(markers))
        {
            return false;
        }

        foreach (var marker in markers)
        {
            visibleKeys.Add(marker.Key);
            if (!_remotePlayerMapDots.TryGetValue(marker.Key, out var dot))
            {
                dot = CreateRemotePlayerDot(
                    marker.Label!,
                    marker.Category,
                    marker.IsProvisional);
                _remotePlayerMapDots.Add(marker.Key, dot);
                RemotePlayerMarkerLayer.Children.Add(dot.Visual);
            }

            dot.Point = marker.Point;
            dot.Shape.Tag = marker.Label;
            dot.Label.Text = marker.Label;
            if (dot.Category != marker.Category)
            {
                ApplyPalette(dot, marker.Category);
            }
            ApplyProvisionalStyle(dot, marker.IsProvisional);
        }

        foreach (var key in _remotePlayerMapDots.Keys
                     .Where(key => !visibleKeys.Contains(key))
                     .ToArray())
        {
            RemotePlayerMarkerLayer.Children.Remove(_remotePlayerMapDots[key].Visual);
            _remotePlayerMapDots.Remove(key);
        }

        _renderedRemotePlayerMarkers = markers.ToArray();
        return true;
    }

    private static RemotePlayerMapDot CreateRemotePlayerDot(
        string label,
        RemoteEntityMapCategory category,
        bool isProvisional)
    {
        var shape = new Ellipse
        {
            Width = RemotePlayerDotSize,
            Height = RemotePlayerDotSize,
            StrokeThickness = 1d,
            IsHitTestVisible = false,
            Tag = label
        };
        Canvas.SetTop(shape, (RemotePlayerMarkerHeight - RemotePlayerDotSize) / 2d);

        var nameLabel = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 8d,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(3d, 1d, 3d, 1d),
            MaxWidth = 145d,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(nameLabel, RemotePlayerDotSize + 3d);
        Canvas.SetTop(nameLabel, 1d);

        var visual = new Canvas
        {
            Width = 160d,
            Height = RemotePlayerMarkerHeight,
            IsHitTestVisible = false
        };
        visual.Children.Add(shape);
        visual.Children.Add(nameLabel);
        var dot = new RemotePlayerMapDot(visual, shape, nameLabel);
        ApplyPalette(dot, category);
        ApplyProvisionalStyle(dot, isProvisional);
        return dot;
    }

    private static void ApplyProvisionalStyle(
        RemotePlayerMapDot dot,
        bool isProvisional)
    {
        dot.Shape.Fill = isProvisional ? Brushes.Transparent : dot.Shape.Fill;
        dot.Shape.StrokeDashArray = isProvisional
            ? new DoubleCollection { 1.5d, 1.5d }
            : null;
        dot.Visual.Opacity = isProvisional ? 0.82d : 1d;
        dot.IsProvisional = isProvisional;
    }

    private static void ApplyPalette(
        RemotePlayerMapDot dot,
        RemoteEntityMapCategory category)
    {
        var palette = PaletteFor(category);
        dot.Shape.Fill = BrushFrom(palette.Fill);
        dot.Shape.Stroke = BrushFrom(palette.Stroke);
        dot.Shape.Effect = new DropShadowEffect
        {
            Color = palette.Glow,
            BlurRadius = 5d,
            ShadowDepth = 0d,
            Opacity = 0.92d
        };
        dot.Label.Foreground = BrushFrom(palette.LabelForeground);
        dot.Label.Background = BrushFrom(palette.LabelBackground);
        dot.Category = category;
    }

    private static MarkerPalette PaletteFor(RemoteEntityMapCategory category) =>
        category switch
        {
            RemoteEntityMapCategory.SameSpecies => new(
                "#42D66B", "#E9FFEF", "#CFFFD9", "#B50B1A11",
                System.Windows.Media.Color.FromRgb(66, 214, 107)),
            RemoteEntityMapCategory.OtherHerbivore => new(
                "#3EA6FF", "#EDF7FF", "#D6EDFF", "#B50A1722",
                System.Windows.Media.Color.FromRgb(62, 166, 255)),
            RemoteEntityMapCategory.Ai => new(
                "#F5C542", "#FFF8D7", "#FFF0A6", "#B51D1808",
                System.Windows.Media.Color.FromRgb(245, 197, 66)),
            RemoteEntityMapCategory.UnclassifiedPlayer => new(
                "#C7D0D4", "#F5FAFC", "#E6EDF0", "#B5111719",
                System.Windows.Media.Color.FromRgb(199, 208, 212)),
            RemoteEntityMapCategory.OtherCarnivore => new(
                "#F04444", "#FFF2F2", "#FFD6D6", "#B50B1717",
                System.Windows.Media.Color.FromRgb(240, 68, 68)),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };

    private void PositionRemotePlayerMarkers(
        double left,
        double top,
        double imageWidth,
        double imageHeight)
    {
        RemotePlayerMarkerLayer.Width = MapViewport.ActualWidth;
        RemotePlayerMarkerLayer.Height = MapViewport.ActualHeight;
        Canvas.SetLeft(RemotePlayerMarkerLayer, 0d);
        Canvas.SetTop(RemotePlayerMarkerLayer, 0d);

        foreach (var dot in _remotePlayerMapDots.Values)
        {
            Canvas.SetLeft(
                dot.Visual,
                left + dot.Point.Left * imageWidth - RemotePlayerDotSize / 2d);
            Canvas.SetTop(
                dot.Visual,
                top + dot.Point.Top * imageHeight - RemotePlayerMarkerHeight / 2d);
        }
    }

    private void ClearRemotePlayerMarkers()
    {
        RemotePlayerMarkerLayer.Children.Clear();
        _remotePlayerMapDots.Clear();
        _renderedRemotePlayerMarkers = [];
        RemotePlayerCountLabel.Visibility = Visibility.Collapsed;
        RemoteEntityLegend.Visibility = Visibility.Collapsed;
    }

    private sealed class RemotePlayerMapDot(
        Canvas visual,
        Ellipse shape,
        TextBlock label)
    {
        public Canvas Visual { get; } = visual;
        public Ellipse Shape { get; } = shape;
        public TextBlock Label { get; } = label;
        public MapPoint Point { get; set; }
        public RemoteEntityMapCategory Category { get; set; }
        public bool IsProvisional { get; set; }
    }

    private readonly record struct MarkerPalette(
        string Fill,
        string Stroke,
        string LabelForeground,
        string LabelBackground,
        System.Windows.Media.Color Glow);
}
