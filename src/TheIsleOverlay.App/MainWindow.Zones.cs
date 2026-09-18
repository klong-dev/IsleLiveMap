using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

/// <summary>Offline map-layer renderer. Telemetry never invalidates static geometry.</summary>
public partial class MainWindow
{
    private const double StaticLabelMinimumZoom = 1.45d;
    private readonly Dictionary<string, StaticMapVisual> _staticMapVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GatewayMapResource> _resourcesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BitmapImage> _mapIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly MapLayerPreferencesStore _mapLayerPreferencesStore = new();
    private MapLayerPreferences _mapLayerPreferences = new();
    private GatewayStaticMapLayers _staticMapLayers = GatewayStaticMapLayers.Empty;
    private double _positionedLayerImageWidth = double.NaN;
    private double _positionedLayerImageHeight = double.NaN;
    private bool _mapLayersInitialized;
    private bool _mapLayerGeometryDirty;

    private void InitializeMapLayers()
    {
        if (_mapLayersInitialized) return;
        _mapLayersInitialized = true;
        try
        {
            _staticMapLayers = GatewayStaticMapLayerCatalog.LoadBundled();
            _mapLayerPreferences = _mapLayerPreferencesStore.Load(_staticMapLayers.Defaults);
            BuildStaticMapVisuals();
            BuildResourceLayerControls();
            _mapLayerGeometryDirty = true;
            UpdateMapLayerControls();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            _staticMapLayers = GatewayStaticMapLayers.Empty;
            MapLayerSummaryLabel.Text = "LAYER DATA ERROR";
            MapLayerSummaryLabel.Foreground = BrushFrom("#E98778");
        }
    }

    private void BuildStaticMapVisuals()
    {
        MapZoneLayer.Children.Clear();
        MapFoodLayer.Children.Clear();
        _staticMapVisuals.Clear();
        _resourcesById.Clear();

        foreach (var zone in _staticMapLayers.Zones)
        {
            var polygon = new Polygon
            {
                Points = new PointCollection(),
                StrokeThickness = zone.Kind == MapZoneKind.Sanctuary ? 1d : 1.15d,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            ApplyZonePalette(polygon, zone.Kind);
            var label = CreateLabel(zone.Name, zone.Kind == MapZoneKind.Migration ? "#D6FFB84D" : "#CDBDA9FF");
            var visual = new StaticMapVisual(zone.Id, MapLayerGroupFor(zone.Kind), polygon, label,
                zone.Points, Centroid(zone.Points), IsPolygon: true);
            _staticMapVisuals.Add(zone.Id, visual);
            MapZoneLayer.Children.Add(polygon);
            MapZoneLayer.Children.Add(label);
        }

        foreach (var zone in _staticMapLayers.AiSpawnZones)
        {
            FrameworkElement polygon = zone.Points.Count == 1
                ? new Ellipse { Width = 16d, Height = 16d, IsHitTestVisible = false }
                : new Polygon { IsHitTestVisible = false };
            if (polygon is Polygon polygonShape)
            {
                polygonShape.Points = new PointCollection();
                polygonShape.StrokeThickness = 0.8d;
                polygonShape.StrokeDashArray = new DoubleCollection { 3d, 2d };
                polygonShape.StrokeLineJoin = PenLineJoin.Round;
            }
            if (polygon is Shape shape)
            {
                shape.Fill = BrushFrom("#16F5C542");
                shape.Stroke = BrushFrom("#B8F5C542");
            }
            var label = CreateLabel($"AI · {zone.Name}", "#C8F5C542");
            var visual = new StaticMapVisual(zone.Id, MapLayerGroup.AiSpawnZones, polygon, label,
                zone.Points, Centroid(zone.Points), true);
            _staticMapVisuals.Add(zone.Id, visual);
            MapZoneLayer.Children.Add(polygon);
            MapZoneLayer.Children.Add(label);
        }

        foreach (var route in _staticMapLayers.Routes)
        {
            var line = new Polyline
            {
                Stroke = BrushFrom("#B878A79A"), StrokeThickness = 1.1d,
                StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
            };
            var visual = new StaticMapVisual(route.Id, MapLayerGroup.Roads, line, null,
                route.Points, route.Points[route.Points.Count / 2], true);
            _staticMapVisuals.Add(route.Id, visual);
            MapZoneLayer.Children.Add(line);
        }

        foreach (var water in _staticMapLayers.WaterLabels)
        {
            var label = CreateLabel($"💧 {water.Name}", "#C878C8FF");
            var visual = new StaticMapVisual(water.Id, MapLayerGroup.Water, label, label,
                [water.Point], water.Point, false);
            _staticMapVisuals.Add(water.Id, visual);
            MapZoneLayer.Children.Add(label);
        }

        foreach (var resource in _staticMapLayers.Resources)
        {
            _resourcesById[resource.Id] = resource;
            FrameworkElement icon;
            if (!string.IsNullOrWhiteSpace(resource.IconKey)
                && TryLoadMapIcon(resource.IconKey!, out var bitmap))
            {
                icon = new Image { Source = bitmap, Width = 13d, Height = 13d, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            }
            else
            {
                icon = new Ellipse { Width = 5d, Height = 5d, Fill = ResourceBrush(resource.Category), IsHitTestVisible = false };
            }
            var visual = new StaticMapVisual(resource.Id, ResourceGroup(resource.Category), icon, null,
                [resource.Point], resource.Point, false);
            _staticMapVisuals.Add(resource.Id, visual);
            MapFoodLayer.Children.Add(icon);
        }
    }

    private void BuildResourceLayerControls()
    {
        if (ResourceLayerChildrenPanel is null) return;
        ResourceLayerChildrenPanel.Children.Clear();
        var keys = _staticMapLayers.Resources
            .Where(resource => resource.Category is "animals" or "plants" or "earth")
            .Select(resource => (resource.Category, resource.Key, resource.Name))
            .Distinct()
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        string? currentCategory = null;
        foreach (var (category, key, name) in keys)
        {
            if (!string.Equals(currentCategory, category, StringComparison.OrdinalIgnoreCase))
            {
                currentCategory = category;
                ResourceLayerChildrenPanel.Children.Add(new TextBlock
                {
                    Text = category switch
                    {
                        "animals" => "ĐỘNG VẬT",
                        "plants" => "THỰC VẬT & NẤM",
                        _ => "ĐẤT & KHOÁNG"
                    },
                    Foreground = BrushFrom("#789A92"),
                    FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                    FontSize = 7.5d,
                    Margin = new Thickness(0d, 7d, 0d, 2d)
                });
            }
            var toggle = new CheckBox
            {
                Content = name,
                Tag = key,
                IsChecked = _mapLayerPreferences.ResourceKeys.Contains(key),
                Style = (Style)FindResource("LayerInspectorCheckBox"),
                ToolTip = "Bật/tắt riêng loại tài nguyên này"
            };
            toggle.Click += ResourceLayerToggle_Click;
            ResourceLayerChildrenPanel.Children.Add(toggle);
        }
    }

    private static MapLayerGroup MapLayerGroupFor(MapZoneKind kind) => kind switch
    {
        MapZoneKind.Migration => MapLayerGroup.Migration,
        MapZoneKind.Patrol => MapLayerGroup.Patrol,
        MapZoneKind.Sanctuary => MapLayerGroup.Sanctuary,
        _ => MapLayerGroup.Migration
    };

    private static MapLayerGroup ResourceGroup(string category) => category.ToLowerInvariant() switch
    {
        "animals" => MapLayerGroup.Animals,
        "plants" => MapLayerGroup.Plants,
        _ => MapLayerGroup.Earth
    };

    private static TextBlock CreateLabel(string text, string color) => new()
    {
        Text = text.ToUpperInvariant(), Foreground = BrushFrom(color),
        Background = BrushFrom("#C80A1517"), FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
        FontSize = 7d, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 132d, Padding = new Thickness(3d, 1d, 3d, 1d), IsHitTestVisible = false
    };

    private static void ApplyZonePalette(Polygon polygon, MapZoneKind kind)
    {
        var (fill, stroke) = kind switch
        {
            MapZoneKind.Migration => ("#2CF0A423", "#D6FFB84D"),
            MapZoneKind.Patrol => ("#28A78BFA", "#CDBDA9FF"),
            _ => ("#243BDBFF", "#B86EAAFF")
        };
        polygon.Fill = BrushFrom(fill);
        polygon.Stroke = BrushFrom(stroke);
    }

    private static Brush ResourceBrush(string category) => BrushFrom(category.ToLowerInvariant() switch
    {
        "animals" => "#F5C542",
        "plants" => "#57D77D",
        _ => "#D69E58"
    });

    private bool TryLoadMapIcon(string key, out BitmapImage bitmap)
    {
        if (_mapIconCache.TryGetValue(key, out bitmap!)) return true;
        try
        {
            var uri = new Uri(
                $"pack://application:,,,/IsleLiveMap;component/Assets/MapLayers/Icons/png/{key}.png",
                UriKind.Absolute);
            bitmap = new BitmapImage(uri);
            bitmap.Freeze();
            _mapIconCache[key] = bitmap;
            return true;
        }
        catch
        {
            bitmap = null!;
            return false;
        }
    }

    private void PositionMapLayers(double left, double top, double imageWidth, double imageHeight)
    {
        foreach (var layer in new[] { MapHeatmapLayer, MapZoneLayer, MapFoodLayer })
        {
            layer.Width = imageWidth; layer.Height = imageHeight;
            Canvas.SetLeft(layer, left); Canvas.SetTop(layer, top);
        }
        var sizeChanged = !NearlyEqual(imageWidth, _positionedLayerImageWidth)
                          || !NearlyEqual(imageHeight, _positionedLayerImageHeight);
        if (!_mapLayerGeometryDirty && !sizeChanged)
        {
            return;
        }
        foreach (var visual in _staticMapVisuals.Values)
        {
            if (visual.Shape is Polygon polygon)
                polygon.Points = new PointCollection(visual.Points.Select(point => new Point(point.Left * imageWidth, point.Top * imageHeight)));
            else if (visual.Shape is Polyline line)
                line.Points = new PointCollection(visual.Points.Select(point => new Point(point.Left * imageWidth, point.Top * imageHeight)));
            else
            {
                var width = visual.Shape.Width > 0 ? visual.Shape.Width : 13d;
                var height = visual.Shape.Height > 0 ? visual.Shape.Height : 13d;
                Canvas.SetLeft(visual.Shape, visual.Anchor.Left * imageWidth - width / 2d);
                Canvas.SetTop(visual.Shape, visual.Anchor.Top * imageHeight - height / 2d);
            }
            if (visual.Label is not null && !ReferenceEquals(visual.Shape, visual.Label))
                PositionLabel(visual.Label, visual.Anchor, imageWidth, imageHeight);
            else if (visual.Label is not null)
                PositionLabel(visual.Label, visual.Anchor, imageWidth, imageHeight);
        }
        _positionedLayerImageWidth = imageWidth;
        _positionedLayerImageHeight = imageHeight;
        _mapLayerGeometryDirty = false;
        UpdateStaticLayerVisibility();
    }

    private void UpdateStaticLayerVisibility()
    {
        foreach (var visual in _staticMapVisuals.Values)
        {
            var visible = _mapLayerPreferences.IsEnabled(visual.Group);
            if (visual.Group is MapLayerGroup.Animals or MapLayerGroup.Plants or MapLayerGroup.Earth)
            {
                visible = visible
                          && _resourcesById.TryGetValue(visual.Id, out var resource)
                          && _mapLayerPreferences.ResourceKeys.Contains(resource.Key);
            }
            if (visual.Label is not null && _mapZoom < StaticLabelMinimumZoom)
                visual.Label.Visibility = Visibility.Collapsed;
            else if (visual.Label is not null)
                visual.Label.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            visual.Shape.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        MapZoneLayer.Visibility = _staticMapVisuals.Values.Any(visual => visual.Shape.Visibility == Visibility.Visible)
            ? Visibility.Visible : Visibility.Collapsed;
        MapFoodLayer.Visibility = _staticMapVisuals.Values.Any(visual =>
            (visual.Group is MapLayerGroup.Animals or MapLayerGroup.Plants or MapLayerGroup.Earth)
            && visual.Shape.Visibility == Visibility.Visible)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MapHeatmapLayer.Visibility = Visibility.Collapsed;
    }

    private void MapLayerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && toggle.Tag is string value && Enum.TryParse<MapLayerGroup>(value, out var group))
        {
            _mapLayerPreferences.SetEnabled(group, toggle.IsChecked == true);
            if (group is MapLayerGroup.Animals or MapLayerGroup.Plants or MapLayerGroup.Earth)
            {
                var category = group switch
                {
                    MapLayerGroup.Animals => "animals",
                    MapLayerGroup.Plants => "plants",
                    _ => "earth"
                };
                var keys = _staticMapLayers.Resources
                    .Where(resource => string.Equals(resource.Category, category, StringComparison.OrdinalIgnoreCase))
                    .Select(resource => resource.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (toggle.IsChecked == true
                    && !keys.Any(key => _mapLayerPreferences.ResourceKeys.Contains(key)))
                    _mapLayerPreferences.ResourceKeys.UnionWith(keys);
            }
            _mapLayerPreferencesStore.TrySave(_mapLayerPreferences, out _);
            _mapNotesWindow?.UpdateLayerPreferences(_mapLayerPreferences);
            _mapLayerGeometryDirty = true;
            UpdateMapLayerControls();
            PositionMap();
        }
    }

    private void ResourceLayerToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: string key } toggle)
        {
            if (toggle.IsChecked == true) _mapLayerPreferences.ResourceKeys.Add(key);
            else _mapLayerPreferences.ResourceKeys.Remove(key);
            _mapLayerPreferencesStore.TrySave(_mapLayerPreferences, out _);
            _mapNotesWindow?.UpdateLayerPreferences(_mapLayerPreferences);
            _mapLayerGeometryDirty = true;
            UpdateMapLayerControls();
            PositionMap();
        }
    }

    private void UpdateMapLayerControls()
    {
        if (!_mapLayersInitialized) return;
        SetLayerToggle(MigrationLayerToggle, MapLayerGroup.Migration, "Migration Zone");
        SetLayerToggle(PatrolLayerToggle, MapLayerGroup.Patrol, "Patrol Zone");
        SetLayerToggle(SanctuaryLayerToggle, MapLayerGroup.Sanctuary, "Sanctuary");
        SetLayerToggle(AiSpawnLayerToggle, MapLayerGroup.AiSpawnZones, "AI Spawn Zones");
        SetLayerToggle(RoadLayerToggle, MapLayerGroup.Roads, "Roads & Trails");
        SetLayerToggle(WaterLayerToggle, MapLayerGroup.Water, "Drinkable Water");
        SetLayerToggle(AnimalsLayerToggle, MapLayerGroup.Animals, "Animals");
        SetLayerToggle(PlantsLayerToggle, MapLayerGroup.Plants, "Plants & Fungi");
        SetLayerToggle(EarthLayerToggle, MapLayerGroup.Earth, "Earth: Gastrolith · Salt · Mud");
        foreach (var toggle in ResourceLayerChildrenPanel.Children.OfType<CheckBox>())
        {
            if (toggle.Tag is string key)
                toggle.IsChecked = _mapLayerPreferences.ResourceKeys.Contains(key);
        }
        MapLayerSummaryLabel.Text = $"{_staticMapLayers.Zones.Count} ZONE · {_staticMapLayers.Resources.Count} RESOURCE";
    }

    private void SetLayerToggle(ToggleButton toggle, MapLayerGroup group, string label)
    {
        var isResourceGroup = group is MapLayerGroup.Animals or MapLayerGroup.Plants or MapLayerGroup.Earth;
        var selected = isResourceGroup
            ? ResourceSelectionState(group)
            : (_mapLayerPreferences.IsEnabled(group), false);
        toggle.IsChecked = selected.Item1;
        toggle.IsThreeState = false;
        toggle.Content = selected.Item2 ? $"{label} · MỘT PHẦN" : label;
        toggle.ToolTip = selected.Item2
            ? "Đang bật một phần loại dữ liệu · click để bật/tắt toàn bộ nhóm"
            : "Bật/tắt nhóm dữ liệu bản đồ";
    }

    private (bool Enabled, bool Partial) ResourceSelectionState(MapLayerGroup group)
    {
        var category = group switch
        {
            MapLayerGroup.Animals => "animals",
            MapLayerGroup.Plants => "plants",
            _ => "earth"
        };
        var keys = _staticMapLayers.Resources
            .Where(resource => string.Equals(resource.Category, category, StringComparison.OrdinalIgnoreCase))
            .Select(resource => resource.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0)
            return (_mapLayerPreferences.IsEnabled(group), false);
        var selected = keys.Count(key => _mapLayerPreferences.ResourceKeys.Contains(key));
        return (_mapLayerPreferences.IsEnabled(group), selected > 0 && selected < keys.Count);
    }

    private void ClearPlayerHeatmap()
    {
        // Activity heatmap is intentionally no longer consumed. Keep the old
        // canvas empty for binary/XAML compatibility with the 2.1.x layout.
        MapHeatmapLayer.Children.Clear();
        MapHeatmapLayer.Visibility = Visibility.Collapsed;
    }

    private static MapPoint Centroid(IReadOnlyList<MapPoint> points) => new(points.Average(point => point.Left), points.Average(point => point.Top));
    private static void PositionLabel(FrameworkElement label, MapPoint point, double imageWidth, double imageHeight)
    {
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, point.Left * imageWidth - label.DesiredSize.Width / 2d);
        Canvas.SetTop(label, point.Top * imageHeight - label.DesiredSize.Height / 2d);
    }
    private static bool NearlyEqual(double left, double right) => double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) < 0.01d;

    private sealed record StaticMapVisual(
        string Id,
        MapLayerGroup Group,
        FrameworkElement Shape,
        FrameworkElement? Label,
        IReadOnlyList<MapPoint> Points,
        MapPoint Anchor,
        bool IsPolygon);
}
