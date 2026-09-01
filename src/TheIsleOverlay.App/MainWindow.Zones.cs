using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private const double ZoneLabelMinimumZoom = 2.25d;
    private const double FoodLabelMinimumZoom = 2.75d;

    private readonly Dictionary<string, MapZoneVisual> _mapZoneVisuals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, FoodRegionVisual> _foodRegionVisuals =
        new(StringComparer.Ordinal);
    private readonly List<PlayerHeatVisual> _playerHeatVisuals = [];
    private GatewayStaticMapLayers _staticMapLayers = GatewayStaticMapLayers.Empty;
    private double _positionedLayerImageWidth = double.NaN;
    private double _positionedLayerImageHeight = double.NaN;
    private bool _mapLayersInitialized;
    private bool _mapLayerGeometryDirty;
    private bool _playerHeatmapAvailable;

    private void InitializeMapLayers()
    {
        if (_mapLayersInitialized)
        {
            return;
        }

        _mapLayersInitialized = true;
        if (!HasCurrentProFeatures)
        {
            DisableProMapLayers();
            return;
        }

        try
        {
            _staticMapLayers = GatewayStaticMapLayerCatalog.LoadBundled();
            foreach (var zone in _staticMapLayers.Zones)
            {
                var visual = CreateMapZoneVisual(zone);
                _mapZoneVisuals.Add(zone.Id, visual);
                MapZoneLayer.Children.Add(visual.Polygon);
                MapZoneLayer.Children.Add(visual.Label);
            }

            foreach (var region in _staticMapLayers.FoodRegions)
            {
                var visual = CreateFoodRegionVisual(region);
                _foodRegionVisuals.Add(region.Id, visual);
                MapFoodLayer.Children.Add(visual.Shape);
                MapFoodLayer.Children.Add(visual.Label);
            }

            _mapLayerGeometryDirty = true;
            UpdateMapLayerControls();
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or IOException
                                          or JsonException)
        {
            _staticMapLayers = GatewayStaticMapLayers.Empty;
            ZoneLayerToggle.IsEnabled = false;
            FoodLayerToggle.IsEnabled = false;
            MapLayerSummaryLabel.Text = "LAYER DATA ERROR";
            MapLayerSummaryLabel.Foreground = BrushFrom("#E98778");
        }
    }

    private void DisableProMapLayers()
    {
        _staticMapLayers = GatewayStaticMapLayers.Empty;
        _mapZoneVisuals.Clear();
        _foodRegionVisuals.Clear();
        MapZoneLayer.Children.Clear();
        MapFoodLayer.Children.Clear();
        ClearPlayerHeatmap();
        ZoneLayerToggle.IsChecked = false;
        FoodLayerToggle.IsChecked = false;
        HeatLayerToggle.IsChecked = false;
        ZoneLayerToggle.IsEnabled = false;
        FoodLayerToggle.IsEnabled = false;
        HeatLayerToggle.IsEnabled = false;
        MapZoneLayer.Visibility = Visibility.Collapsed;
        MapFoodLayer.Visibility = Visibility.Collapsed;
        MapHeatmapLayer.Visibility = Visibility.Collapsed;
        MapLayerSummaryLabel.Text = "PRO MAP LAYERS";
        MapLayerSummaryLabel.Foreground = BrushFrom("#84785A");
    }

    private void SyncPlayerHeatmap(MapTelemetry? map, PlayerTelemetry? localPlayer)
    {
        _ = localPlayer;
        var heatmap = PlayerHeatmapResolver.Resolve(map);
        var points = heatmap.Points;
        _playerHeatmapAvailable = points.Count > 0;
        if (!_playerHeatmapAvailable)
        {
            ClearPlayerHeatmap();
            UpdateMapLayerControls();
            return;
        }

        while (_playerHeatVisuals.Count < points.Count)
        {
            var visual = CreatePlayerHeatVisual();
            _playerHeatVisuals.Add(visual);
            MapHeatmapLayer.Children.Add(visual.Shape);
        }

        while (_playerHeatVisuals.Count > points.Count)
        {
            var last = _playerHeatVisuals[^1];
            MapHeatmapLayer.Children.Remove(last.Shape);
            _playerHeatVisuals.RemoveAt(_playerHeatVisuals.Count - 1);
        }

        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            var visual = _playerHeatVisuals[index];
            visual.Point = point.Point;
            visual.Intensity = point.Intensity;
            visual.Radius = heatmap.Radius;
            visual.Shape.Opacity = 0.42d + 0.42d * point.Intensity;
            visual.Shape.ToolTip = $"Player heatmap IslePilot · {point.Intensity:P0}";
        }

        _mapLayerGeometryDirty = true;
        UpdateMapLayerControls();
    }

    private static MapZoneVisual CreateMapZoneVisual(GatewayStaticMapZone zone)
    {
        var polygon = new Polygon
        {
            StrokeThickness = 1.15d,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        var labelText = new TextBlock
        {
            Text = zone.Kind == MapZoneKind.Migration
                ? $"MMZ · {zone.Name}"
                : $"PZ · {zone.Name}",
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 7.2d,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 118d
        };
        var label = new Border
        {
            Child = labelText,
            Padding = new Thickness(4d, 1.25d, 4d, 1.25d),
            CornerRadius = new CornerRadius(2d),
            BorderThickness = new Thickness(0.75d),
            IsHitTestVisible = false
        };
        var visual = new MapZoneVisual(
            polygon,
            label,
            labelText,
            zone.Points,
            Centroid(zone.Points),
            zone.Kind);
        ApplyZonePalette(visual);
        return visual;
    }

    private static FoodRegionVisual CreateFoodRegionVisual(GatewayFoodRegion region)
    {
        var aquatic = region.Foods.All(food => food is "Rùa" or "Cua");
        var stroke = aquatic ? "#CB72E9FA" : "#D6DCF466";
        var fill = aquatic ? "#2926C8E8" : "#28BFD84C";
        var shape = new Ellipse
        {
            Fill = BrushFrom(fill),
            Stroke = BrushFrom(stroke),
            StrokeThickness = 1.1d,
            IsHitTestVisible = false
        };
        var labelText = new TextBlock
        {
            Text = region.Label.ToUpperInvariant(),
            Foreground = BrushFrom(stroke),
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 7d,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 118d
        };
        var label = new Border
        {
            Child = labelText,
            Background = BrushFrom("#C80A1517"),
            BorderBrush = BrushFrom(stroke),
            BorderThickness = new Thickness(0.6d),
            CornerRadius = new CornerRadius(2d),
            Padding = new Thickness(4d, 1.2d, 4d, 1.2d),
            IsHitTestVisible = false
        };
        return new FoodRegionVisual(
            shape,
            label,
            region.Center,
            region.RadiusX,
            region.RadiusY);
    }

    private static PlayerHeatVisual CreatePlayerHeatVisual()
    {
        var fill = new RadialGradientBrush
        {
            Center = new Point(0.5d, 0.5d),
            GradientOrigin = new Point(0.5d, 0.5d),
            RadiusX = 0.5d,
            RadiusY = 0.5d,
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#E8FF4438"), 0d),
                new GradientStop((Color)ColorConverter.ConvertFromString("#B8FF9E2F"), 0.38d),
                new GradientStop((Color)ColorConverter.ConvertFromString("#42FFD057"), 0.68d),
                new GradientStop(Colors.Transparent, 1d)
            }
        };
        fill.Freeze();
        return new PlayerHeatVisual(new Ellipse
        {
            Fill = fill,
            IsHitTestVisible = false
        });
    }

    private static void ApplyZonePalette(MapZoneVisual visual)
    {
        var migration = visual.Kind == MapZoneKind.Migration;
        var fill = migration ? "#2CF0A423" : "#28A78BFA";
        var stroke = migration ? "#D6FFB84D" : "#CDBDA9FF";
        visual.Polygon.Fill = BrushFrom(fill);
        visual.Polygon.Stroke = BrushFrom(stroke);
        visual.Label.Background = BrushFrom(migration ? "#CD21170A" : "#CC171125");
        visual.Label.BorderBrush = BrushFrom(stroke);
        visual.LabelText.Foreground = BrushFrom(stroke);
    }

    private void PositionMapLayers(
        double left,
        double top,
        double imageWidth,
        double imageHeight)
    {
        foreach (var layer in new[] { MapHeatmapLayer, MapZoneLayer, MapFoodLayer })
        {
            layer.Width = imageWidth;
            layer.Height = imageHeight;
            Canvas.SetLeft(layer, left);
            Canvas.SetTop(layer, top);
        }

        var sizeChanged = !NearlyEqual(imageWidth, _positionedLayerImageWidth)
                          || !NearlyEqual(imageHeight, _positionedLayerImageHeight);
        if (!_mapLayerGeometryDirty && !sizeChanged)
        {
            UpdateMapLayerLabelVisibility(left, top, imageWidth, imageHeight);
            return;
        }

        foreach (var visual in _mapZoneVisuals.Values)
        {
            visual.Polygon.Points = new PointCollection(visual.Points.Select(point =>
                new Point(point.Left * imageWidth, point.Top * imageHeight)));
            PositionLabel(visual.Label, visual.LabelPoint, imageWidth, imageHeight);
        }

        foreach (var visual in _foodRegionVisuals.Values)
        {
            var width = visual.RadiusX * imageWidth * 2d;
            var height = visual.RadiusY * imageHeight * 2d;
            visual.Shape.Width = width;
            visual.Shape.Height = height;
            Canvas.SetLeft(visual.Shape, visual.Center.Left * imageWidth - width / 2d);
            Canvas.SetTop(visual.Shape, visual.Center.Top * imageHeight - height / 2d);
            PositionLabel(visual.Label, visual.Center, imageWidth, imageHeight);
        }

        foreach (var visual in _playerHeatVisuals)
        {
            var radius = visual.Radius;
            var width = radius * imageWidth * 2d;
            var height = radius * imageHeight * 2d;
            visual.Shape.Width = width;
            visual.Shape.Height = height;
            Canvas.SetLeft(visual.Shape, visual.Point.Left * imageWidth - width / 2d);
            Canvas.SetTop(visual.Shape, visual.Point.Top * imageHeight - height / 2d);
        }

        _positionedLayerImageWidth = imageWidth;
        _positionedLayerImageHeight = imageHeight;
        _mapLayerGeometryDirty = false;
        UpdateMapLayerLabelVisibility(left, top, imageWidth, imageHeight);
    }

    private void UpdateMapLayerLabelVisibility(
        double left,
        double top,
        double imageWidth,
        double imageHeight)
    {
        foreach (var visual in _mapZoneVisuals.Values)
        {
            visual.Label.Visibility = Visibility.Collapsed;
        }

        foreach (var visual in _foodRegionVisuals.Values)
        {
            visual.Label.Visibility = Visibility.Collapsed;
        }

        var occupied = new List<Rect>();
        if (_mapZoom >= ZoneLabelMinimumZoom)
        {
            foreach (var visual in _mapZoneVisuals.Values
                         .Where(visual => IsPointInMapViewport(
                             visual.LabelPoint,
                             left,
                             top,
                             imageWidth,
                             imageHeight))
                         .OrderBy(visual => visual.Kind == MapZoneKind.Migration ? 0 : 1)
                         .ThenBy(visual => DistanceFromViewportCenter(
                             visual.LabelPoint,
                             left,
                             top,
                             imageWidth,
                             imageHeight))
                         .Take(12))
            {
                var bounds = LabelScreenBounds(
                    visual.Label,
                    visual.LabelPoint,
                    left,
                    top,
                    imageWidth,
                    imageHeight);
                if (occupied.Any(existing => existing.IntersectsWith(bounds)))
                {
                    continue;
                }

                visual.Label.Visibility = Visibility.Visible;
                occupied.Add(Inflated(bounds, 3d));
                if (occupied.Count >= 6)
                {
                    break;
                }
            }
        }

        if (_mapZoom >= FoodLabelMinimumZoom)
        {
            var visibleFoodLabels = 0;
            foreach (var visual in _foodRegionVisuals.Values
                         .Where(visual => IsPointInMapViewport(
                             visual.Center,
                             left,
                             top,
                             imageWidth,
                             imageHeight))
                         .OrderBy(visual => DistanceFromViewportCenter(
                             visual.Center,
                             left,
                             top,
                             imageWidth,
                             imageHeight)))
            {
                var bounds = LabelScreenBounds(
                    visual.Label,
                    visual.Center,
                    left,
                    top,
                    imageWidth,
                    imageHeight);
                if (occupied.Any(existing => existing.IntersectsWith(bounds)))
                {
                    continue;
                }

                visual.Label.Visibility = Visibility.Visible;
                occupied.Add(Inflated(bounds, 3d));
                visibleFoodLabels++;
                if (visibleFoodLabels >= 3)
                {
                    break;
                }
            }
        }
    }

    private double DistanceFromViewportCenter(
        MapPoint point,
        double left,
        double top,
        double imageWidth,
        double imageHeight)
    {
        var deltaX = left + point.Left * imageWidth - MapViewport.ActualWidth / 2d;
        var deltaY = top + point.Top * imageHeight - MapViewport.ActualHeight / 2d;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private static Rect LabelScreenBounds(
        FrameworkElement label,
        MapPoint point,
        double left,
        double top,
        double imageWidth,
        double imageHeight) => new(
        left + point.Left * imageWidth - label.DesiredSize.Width / 2d,
        top + point.Top * imageHeight - label.DesiredSize.Height / 2d,
        label.DesiredSize.Width,
        label.DesiredSize.Height);

    private static Rect Inflated(Rect source, double margin)
    {
        source.Inflate(margin, margin);
        return source;
    }

    private bool IsPointInMapViewport(
        MapPoint point,
        double left,
        double top,
        double imageWidth,
        double imageHeight)
    {
        const double margin = 24d;
        var x = left + point.Left * imageWidth;
        var y = top + point.Top * imageHeight;
        return x >= -margin
               && x <= MapViewport.ActualWidth + margin
               && y >= -margin
               && y <= MapViewport.ActualHeight + margin;
    }

    private static void PositionLabel(
        FrameworkElement label,
        MapPoint point,
        double imageWidth,
        double imageHeight)
    {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, point.Left * imageWidth - label.DesiredSize.Width / 2d);
        Canvas.SetTop(label, point.Top * imageHeight - label.DesiredSize.Height / 2d);
    }

    private static MapPoint Centroid(IReadOnlyList<MapPoint> points) => new(
        points.Average(point => point.Left),
        points.Average(point => point.Top));

    private void MapLayerToggle_Click(object sender, RoutedEventArgs e) =>
        UpdateMapLayerControls();

    private void UpdateMapLayerControls()
    {
        if (!HasCurrentProFeatures)
        {
            DisableProMapLayers();
            return;
        }

        MapZoneLayer.Visibility = ZoneLayerToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        MapFoodLayer.Visibility = FoodLayerToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        HeatLayerToggle.IsEnabled = _playerHeatmapAvailable;
        MapHeatmapLayer.Visibility = _playerHeatmapAvailable
                                     && HeatLayerToggle.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        HeatLayerToggle.ToolTip = _playerHeatmapAvailable
            ? "Ẩn/hiện Player heatmap do IslePilot cung cấp"
            : "Server hiện tại không cung cấp Player heatmap qua IslePilot";
        MapLayerSummaryLabel.Text = $"MMZ {_staticMapLayers.Zones.Count(zone => zone.Kind == MapZoneKind.Migration)}"
                                    + $" · PZ {_staticMapLayers.Zones.Count(zone => zone.Kind == MapZoneKind.Patrol)}"
                                    + $" · FOOD {_staticMapLayers.FoodRegions.Count}"
                                    + (_playerHeatmapAvailable ? " · HEAT LIVE" : string.Empty);
    }

    private void ClearPlayerHeatmap()
    {
        MapHeatmapLayer.Children.Clear();
        _playerHeatVisuals.Clear();
        _playerHeatmapAvailable = false;
        _mapLayerGeometryDirty = true;
    }

    private static bool NearlyEqual(double left, double right) =>
        double.IsFinite(left)
        && double.IsFinite(right)
        && Math.Abs(left - right) < 0.01d;

    private sealed record MapZoneVisual(
        Polygon Polygon,
        Border Label,
        TextBlock LabelText,
        IReadOnlyList<MapPoint> Points,
        MapPoint LabelPoint,
        MapZoneKind Kind);

    private sealed record FoodRegionVisual(
        Ellipse Shape,
        Border Label,
        MapPoint Center,
        double RadiusX,
        double RadiusY);

    private sealed class PlayerHeatVisual(Ellipse shape)
    {
        public Ellipse Shape { get; } = shape;
        public MapPoint Point { get; set; }
        public double Intensity { get; set; }
        public double Radius { get; set; }
    }
}
