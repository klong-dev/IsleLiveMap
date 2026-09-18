using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TheIsleOverlay.Core;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public partial class MapNotesWindow : Window
{
    private static readonly Uri GatewayMapResourceUri = new(
        "pack://application:,,,/IsleLiveMap;component/Assets/GatewayMap.jpg",
        UriKind.Absolute);
    private static readonly Lazy<BitmapSource> GatewayMapImage = new(
        LoadGatewayMapImage,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly MapNoteStore _store;
    private readonly CancellationTokenSource _shutdown = new();
    private Guid? _selectedNoteId;
    private WorldLocation? _playerLocation;
    private double _playerHeading;
    private TeamRelayState _teamState;
    private bool _paletteBusy;
    private bool _noteCreationBusy;
    private string? _durableSelectionFeedback;
    private bool _durableSelectionFeedbackIsError;
    private readonly Dictionary<Guid, NoteVisual> _noteVisuals = [];
    private readonly Dictionary<string, StaticLayerVisual> _staticLayerVisuals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BitmapImage> _mapIconCache = new(StringComparer.OrdinalIgnoreCase);
    private GatewayStaticMapLayers _staticLayers = GatewayStaticMapLayers.Empty;
    private MapLayerPreferences _layerPreferences = new();
    private FrameworkElement? _playerMarker;
    private RotateTransform? _playerHeadingTransform;

    public MapNotesWindow(
        MapNoteStore store,
        WorldLocation? playerLocation,
        double playerHeading,
        TeamRelayState? teamState = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _playerLocation = playerLocation;
        _playerHeading = playerHeading;
        _teamState = teamState ?? new TeamRelayState();
        InitializeComponent();
        CloseShortcutLabel.Text =
            $"{new ShortcutSettingsStore().Load().MapNotes.ToUpperInvariant()} / ESC ĐỂ ĐÓNG";
        LoadMap();
        LoadStaticLayers();
        BuildPalette();
        _store.Changed += Store_Changed;
    }

    public void UpdatePlayer(WorldLocation? location, double heading)
    {
        _playerLocation = location;
        _playerHeading = heading;
        if (IsLoaded)
        {
            UpdateDynamicVisuals();
        }
    }

    public void UpdateTeamState(TeamRelayState state)
    {
        _teamState = state ?? new TeamRelayState();
        if (IsLoaded)
        {
            RenderMap();
        }
    }

    internal void UpdateLayerPreferences(MapLayerPreferences preferences)
    {
        _layerPreferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        if (IsLoaded)
        {
            BuildStaticLayerVisuals();
            RenderMap();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var mapHeight = Math.Clamp(workArea.Height * 0.65d, 430d, Math.Max(430d, workArea.Height - 150d));
        var mapWidth = mapHeight * 1112d / 1116d;
        MapFrame.Width = mapWidth;
        MapFrame.Height = mapHeight;
        Width = mapWidth + 44d;
        Height = mapHeight + 184d;
        Left = workArea.Left + (workArea.Width - Width) / 2d;
        Top = workArea.Top + (workArea.Height - Height) / 2d;
        RenderMap();
        Activate();
        Focus();
    }

    private void LoadMap() => MapImage.Source = GatewayMapImage.Value;

    private static BitmapSource LoadGatewayMapImage()
    {
        var resource = Application.GetResourceStream(GatewayMapResourceUri)
            ?? throw new InvalidOperationException("Bundled Gateway map resource was not found.");
        using var stream = resource.Stream;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void LoadStaticLayers()
    {
        try
        {
            _staticLayers = GatewayStaticMapLayerCatalog.LoadBundled();
            _layerPreferences = new MapLayerPreferencesStore().Load(_staticLayers.Defaults);
            BuildStaticLayerVisuals();
        }
        catch (Exception)
        {
            _staticLayers = GatewayStaticMapLayers.Empty;
            _staticLayerVisuals.Clear();
            StaticLayer.Children.Clear();
        }
    }

    private void BuildStaticLayerVisuals()
    {
        StaticLayer.Children.Clear();
        _staticLayerVisuals.Clear();
        foreach (var zone in _staticLayers.Zones)
        {
            var group = MapLayerGroupFor(zone.Kind);
            if (!_layerPreferences.IsEnabled(group)) continue;
            var polygon = new Polygon
            {
                Fill = zone.Kind switch
                {
                    MapZoneKind.Migration => BrushFrom("#20F0A423"),
                    MapZoneKind.Patrol => BrushFrom("#1EA78BFA"),
                    _ => BrushFrom("#253BDBFF")
                },
                Stroke = zone.Kind switch
                {
                    MapZoneKind.Migration => BrushFrom("#D6FFB84D"),
                    MapZoneKind.Patrol => BrushFrom("#CDBDA9FF"),
                    _ => BrushFrom("#B86EAAFF")
                },
                StrokeThickness = 1d,
                IsHitTestVisible = false
            };
            var label = CreateStaticLabel(zone.Name, "#D6E7DFFF");
            _staticLayerVisuals[zone.Id] = new StaticLayerVisual(
                zone.Id, group, polygon, label, zone.Points, Centroid(zone.Points));
            StaticLayer.Children.Add(polygon);
            StaticLayer.Children.Add(label);
        }

        if (_layerPreferences.AiSpawnZones)
        {
            foreach (var zone in _staticLayers.AiSpawnZones)
            {
                FrameworkElement shape = zone.Points.Count == 1
                    ? new Ellipse
                    {
                        Width = 14d, Height = 14d, Fill = BrushFrom("#16F5C542"),
                        Stroke = BrushFrom("#B8F5C542"), StrokeThickness = 1d,
                        IsHitTestVisible = false
                    }
                    : new Polygon
                    {
                        Fill = BrushFrom("#16F5C542"), Stroke = BrushFrom("#B8F5C542"),
                        StrokeThickness = 1d, StrokeDashArray = [3d, 2d],
                        IsHitTestVisible = false
                    };
                var label = CreateStaticLabel($"AI · {zone.Name}", "#C8F5C542");
                _staticLayerVisuals[zone.Id] = new StaticLayerVisual(
                    zone.Id, MapLayerGroup.AiSpawnZones, shape, label, zone.Points, Centroid(zone.Points));
                StaticLayer.Children.Add(shape);
                StaticLayer.Children.Add(label);
            }
        }

        if (_layerPreferences.Roads)
        {
            foreach (var route in _staticLayers.Routes)
            {
                var line = new Polyline
                {
                    Stroke = BrushFrom("#9A78A79A"), StrokeThickness = 1d,
                    StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
                };
                _staticLayerVisuals[route.Id] = new StaticLayerVisual(
                    route.Id, MapLayerGroup.Roads, line, null, route.Points,
                    route.Points[route.Points.Count / 2]);
                StaticLayer.Children.Add(line);
            }
        }

        if (_layerPreferences.Water)
        {
            foreach (var water in _staticLayers.WaterLabels)
            {
                var label = CreateStaticLabel($"💧 {water.Name}", "#C878C8FF");
                _staticLayerVisuals[water.Id] = new StaticLayerVisual(
                    water.Id, MapLayerGroup.Water, label, label, [water.Point], water.Point);
                StaticLayer.Children.Add(label);
            }
        }

        foreach (var resource in _staticLayers.Resources)
        {
            var group = ResourceGroup(resource.Category);
            if (!_layerPreferences.IsEnabled(group)
                || !_layerPreferences.ResourceKeys.Contains(resource.Key))
                continue;
            FrameworkElement icon = !string.IsNullOrWhiteSpace(resource.IconKey)
                && TryLoadMapIcon(resource.IconKey!, out var bitmap)
                ? new Image
                {
                    Source = bitmap, Width = 12d, Height = 12d,
                    Stretch = Stretch.Uniform, IsHitTestVisible = false
                }
                : new Ellipse
                {
                    Width = 5d, Height = 5d, Fill = ResourceBrush(resource.Category),
                    IsHitTestVisible = false
                };
            _staticLayerVisuals[resource.Id] = new StaticLayerVisual(
                resource.Id, group, icon, null, [resource.Point], resource.Point, resource.Key);
            StaticLayer.Children.Add(icon);
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

    private static TextBlock CreateStaticLabel(string text, string color) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = BrushFrom(color),
        Background = BrushFrom("#C80A1517"),
        FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
        FontSize = 7d,
        FontWeight = FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxWidth = 150d,
        Padding = new Thickness(3d, 1d, 3d, 1d),
        IsHitTestVisible = false
    };

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
            bitmap = new BitmapImage(new Uri(
                $"pack://application:,,,/IsleLiveMap;component/Assets/MapLayers/Icons/png/{key}.png",
                UriKind.Absolute));
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

    private static MapPoint Centroid(IReadOnlyList<MapPoint> points) =>
        new(points.Average(point => point.Left), points.Average(point => point.Top));

    private void BuildPalette()
    {
        PaletteGrid.Children.Clear();
        foreach (var item in MapNoteIconCatalog.Palette)
        {
            var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(MapNoteIconCatalog.CreatePath(item, 24d));
            content.Children.Add(new TextBlock
            {
                Text = item.Label.ToUpperInvariant(),
                Foreground = BrushFrom("#C9DBD5"),
                FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                FontSize = 8d,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var button = new Button
            {
                Content = content,
                Tag = item,
                ToolTip = item.Label,
                Style = (Style)FindResource("NotePaletteButton")
            };
            button.Click += PaletteButton_Click;
            PaletteGrid.Children.Add(button);
        }
    }

    private async void MapSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is not null)
        {
            return;
        }

        var width = MapSurface.ActualWidth;
        var height = MapSurface.ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        var position = e.GetPosition(MapSurface);
        e.Handled = true;
        var error = await TryCreateNoteAtAsync(new MapPoint(
            position.X / width,
            position.Y / height));
        if (error is not null)
        {
            if (error.Length > 0)
            {
                SelectionDetailLabel.Text = error;
            }
            return;
        }

        RenderMap();
        OpenPalette(position);
    }

    private async void CoordinateSubmitButton_Click(object sender, RoutedEventArgs e) =>
        await PlaceCoordinateNoteAsync();

    private async void CoordinateInputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await PlaceCoordinateNoteAsync();
    }

    private async Task PlaceCoordinateNoteAsync()
    {
        if (!WorldCoordinateInput.TryParse(CoordinateInputTextBox.Text, out var location))
        {
            SetCoordinateFeedback(
                "Sai định dạng · dán đủ X, Y, Z như -238,743.261, 88,587.6, 28,509.171",
                isError: true);
            CoordinateInputTextBox.Focus();
            CoordinateInputTextBox.SelectAll();
            return;
        }

        if (!WorldCoordinateInput.TryProjectToGateway(location, out var point))
        {
            SetCoordinateFeedback("Tọa độ nằm ngoài bản đồ Gateway", isError: true);
            CoordinateInputTextBox.Focus();
            CoordinateInputTextBox.SelectAll();
            return;
        }

        SetCoordinateFeedback("Đang tạo mốc…", isError: false);
        var error = await TryCreateNoteAtAsync(point);
        if (error is not null)
        {
            if (error.Length > 0)
            {
                SetCoordinateFeedback(error, isError: true);
            }
            return;
        }

        RenderMap();
        SetCoordinateFeedback("Đã đặt mốc · chọn biểu tượng cho điểm vừa tạo", isError: false);
        OpenPalette(new Point(
            point.Left * MapSurface.ActualWidth,
            point.Top * MapSurface.ActualHeight));
        CoordinateInputTextBox.Focus();
        CoordinateInputTextBox.SelectAll();
    }

    private async Task<string?> TryCreateNoteAtAsync(MapPoint point)
    {
        if (_noteCreationBusy)
        {
            return "Đang tạo mốc trước đó…";
        }

        _noteCreationBusy = true;
        CoordinateSubmitButton.IsEnabled = false;
        try
        {
            var u = Math.Clamp(point.Left, 0d, 1d);
            var v = Math.Clamp(point.Top, 0d, 1d);
            if (_teamState.HasActiveSession)
            {
                if (_teamState.ConnectionState != TeamRelayConnectionState.Live)
                {
                    return "Relay đang nối lại · chưa thể đặt ping nhóm";
                }

                try
                {
                    var ping = await App.CurrentTeam.UpsertMapPingAsync(
                        MapNotePresentationBuilder.Mutation(
                            Guid.NewGuid(),
                            expectedRevision: 0,
                            MapNoteKind.Pin,
                            u,
                            v),
                        _shutdown.Token);
                    ApplyTeamPing(ping);
                    _selectedNoteId = ping.PingId;
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    return string.Empty;
                }
                catch (TeamMapPingException exception)
                {
                    return FriendlyPingError(exception);
                }
            }
            else
            {
                var result = _store.TryAddDefault(u, v);
                if (!result.Success)
                {
                    return result.Error ?? "Không thể lưu mốc xuống máy.";
                }
                _selectedNoteId = result.Note!.Id;
            }

            ClearDurableSelectionFeedback();
            return null;
        }
        finally
        {
            _noteCreationBusy = false;
            CoordinateSubmitButton.IsEnabled = true;
        }
    }

    private void SetCoordinateFeedback(string message, bool isError)
    {
        CoordinateFeedbackLabel.Text = message;
        CoordinateFeedbackLabel.Foreground = BrushFrom(isError ? "#EF8D7C" : "#8FE3D0");
        CoordinateInputBorder.BorderBrush = BrushFrom(isError ? "#C9695C" : "#8E793D");
    }

    private void NoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } button)
        {
            return;
        }

        _selectedNoteId = id;
        ClearDurableSelectionFeedback();
        var note = VisibleNotes().FirstOrDefault(candidate => candidate.Id == id);
        if (note is null)
        {
            return;
        }
        var center = new Point(
            Canvas.GetLeft(button) + button.Width / 2d,
            Canvas.GetTop(button) + button.Height / 2d);
        UpdateSelectionDetail();
        if (note.CanEdit)
        {
            OpenPalette(center);
        }
        else
        {
            MarkerPalettePopup.IsOpen = false;
            SetDurableSelectionFeedback(
                $"Ping của {note.OwnerDisplayName} · chỉ chủ ping được sửa hoặc xóa",
                isError: false);
        }
        e.Handled = true;
    }

    private async void PaletteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_paletteBusy
            || _selectedNoteId is not { } id
            || sender is not Button { Tag: MapNotePaletteItem item })
        {
            return;
        }

        var selected = VisibleNotes().FirstOrDefault(note => note.Id == id);
        if (selected is null || !selected.CanEdit)
        {
            MarkerPalettePopup.IsOpen = false;
            UpdateSelectionDetail();
            return;
        }

        await DeleteOrChangeSelectedAsync(selected, item);
    }

    private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e) =>
        await DeleteSelectedAsync();

    private async Task DeleteSelectedAsync()
    {
        if (_paletteBusy || _selectedNoteId is not { } id) return;
        var selected = VisibleNotes().FirstOrDefault(note => note.Id == id);
        if (selected is null)
        {
            SetDurableSelectionFeedback("Mốc không còn tồn tại.", true);
            RenderMap();
            return;
        }
        if (!selected.CanEdit)
        {
            SetDurableSelectionFeedback(
                $"Ping của {selected.OwnerDisplayName} · chỉ chủ ping được xóa",
                true);
            return;
        }

        var deleteItem = MapNoteIconCatalog.Palette.First(item => item.IsDelete);
        await DeleteOrChangeSelectedAsync(selected, deleteItem);
    }

    private async Task DeleteOrChangeSelectedAsync(
        MapNotePresentation selected,
        MapNotePaletteItem item)
    {
        var id = selected.Id;
        _paletteBusy = true;
        SetPaletteEnabled(false);
        DeleteSelectedButton.IsEnabled = false;
        var succeeded = false;
        string? failure = null;
        try
        {
            if (selected.IsTeamPing)
            {
                if (item.IsDelete)
                {
                    await App.CurrentTeam.DeleteMapPingAsync(id, selected.Revision, _shutdown.Token);
                    RemoveTeamPing(id);
                    _selectedNoteId = null;
                    succeeded = true;
                }
                else
                {
                    var ping = await App.CurrentTeam.UpsertMapPingAsync(
                        MapNotePresentationBuilder.Mutation(
                            id,
                            selected.Revision,
                            item.Kind!.Value,
                            selected.U,
                            selected.V),
                        _shutdown.Token);
                    ApplyTeamPing(ping);
                    succeeded = true;
                }
            }
            else if (item.IsDelete)
            {
                var result = _store.TryDelete(id);
                if (!result.Success)
                {
                    failure = result.Error ?? "Không thể xóa mốc khỏi máy.";
                }
                else
                {
                    _selectedNoteId = null;
                    succeeded = true;
                }
            }
            else
            {
                var result = _store.TryChangeKind(id, item.Kind!.Value);
                if (!result.Success)
                {
                    failure = result.Error ?? "Không thể lưu thay đổi của mốc.";
                }
                else
                {
                    succeeded = true;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (TeamMapPingException exception)
        {
            if (item.IsDelete && exception.Code == "ping_not_found")
            {
                RemoveTeamPing(selected.Id);
                _selectedNoteId = null;
                succeeded = true;
            }
            else
            {
                failure = FriendlyPingError(exception);
            }
        }
        finally
        {
            _paletteBusy = false;
            SetPaletteEnabled(true);
            DeleteSelectedButton.IsEnabled = true;
            MarkerPalettePopup.IsOpen = failure is not null;
            if (failure is not null)
            {
                SetDurableSelectionFeedback(failure, true);
            }
            else if (succeeded)
            {
                SetDurableSelectionFeedback(
                    item.IsDelete ? "Đã xóa mốc." : "Đã lưu loại mốc.",
                    false);
            }
            RenderMap();
        }
    }

    private void OpenPalette(Point anchor)
    {
        const double paletteWidth = 294d;
        const double paletteHeight = 198d;
        MarkerPalettePopup.HorizontalOffset = Math.Clamp(
            anchor.X - paletteWidth / 2d,
            8d,
            Math.Max(8d, MapSurface.ActualWidth - paletteWidth - 8d));
        MarkerPalettePopup.VerticalOffset = Math.Clamp(
            anchor.Y + 18d,
            8d,
            Math.Max(8d, MapSurface.ActualHeight - paletteHeight - 8d));
        MarkerPalettePopup.IsOpen = true;
    }

    private void RenderMap()
    {
        var width = MapSurface.ActualWidth;
        var height = MapSurface.ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        NoteLineLayer.Width = NoteMarkerLayer.Width = PlayerLayer.Width = width;
        NoteLineLayer.Height = NoteMarkerLayer.Height = PlayerLayer.Height = height;
        StaticLayer.Width = width;
        StaticLayer.Height = height;
        foreach (var visual in _staticLayerVisuals.Values)
        {
            if (visual.Shape is Polygon polygon)
            {
                polygon.Points = new PointCollection(
                    visual.Points.Select(point => new Point(point.Left * width, point.Top * height)));
            }
            else if (visual.Shape is Polyline line)
            {
                line.Points = new PointCollection(
                    visual.Points.Select(point => new Point(point.Left * width, point.Top * height)));
            }
            else
            {
                var elementWidth = visual.Shape.Width > 0d ? visual.Shape.Width : 12d;
                var elementHeight = visual.Shape.Height > 0d ? visual.Shape.Height : 12d;
                Canvas.SetLeft(visual.Shape, visual.Anchor.Left * width - elementWidth / 2d);
                Canvas.SetTop(visual.Shape, visual.Anchor.Top * height - elementHeight / 2d);
            }

            if (visual.Label is not null)
            {
                visual.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(
                    visual.Label,
                    visual.Anchor.Left * width - visual.Label.DesiredSize.Width / 2d);
                Canvas.SetTop(
                    visual.Label,
                    visual.Anchor.Top * height - visual.Label.DesiredSize.Height / 2d);
            }
        }

        var playerPoint = _playerLocation is null
            ? (MapPoint?)null
            : GatewayMapProjection.Project(_playerLocation);
        var notes = VisibleNotes();
        var visibleIds = notes.Select(note => note.Id).ToHashSet();
        foreach (var removedId in _noteVisuals.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
        {
            var removed = _noteVisuals[removedId];
            NoteLineLayer.Children.Remove(removed.Line);
            NoteMarkerLayer.Children.Remove(removed.Button);
            _noteVisuals.Remove(removedId);
        }

        foreach (var note in notes)
        {
            var item = MapNoteIconCatalog.For(note.Kind);
            var target = new Point(note.U * width, note.V * height);
            if (!_noteVisuals.TryGetValue(note.Id, out var visual)
                || visual.Kind != note.Kind
                || visual.IsTeamPing != note.IsTeamPing
                || visual.CanEdit != note.CanEdit
                || visual.Revision != note.Revision)
            {
                if (visual is not null)
                {
                    NoteLineLayer.Children.Remove(visual.Line);
                    NoteMarkerLayer.Children.Remove(visual.Button);
                }

                var line = new Line
                {
                    Stroke = BrushFrom(item.Color),
                    StrokeDashArray = [5d, 4d],
                    IsHitTestVisible = false
                };
                var button = CreateNoteButton(note, item);
                visual = new NoteVisual(
                    line,
                    button,
                    note.Kind,
                    note.IsTeamPing,
                    note.CanEdit,
                    note.Revision);
                _noteVisuals[note.Id] = visual;
                NoteLineLayer.Children.Add(line);
                NoteMarkerLayer.Children.Add(button);
            }

            var selected = _selectedNoteId == note.Id;
            visual.Line.Visibility = playerPoint is null ? Visibility.Collapsed : Visibility.Visible;
            visual.Line.StrokeThickness = selected ? 2.2d : 1.25d;
            visual.Line.Opacity = selected ? 0.92d : (notes.Count >= 4 ? 0.34d : 0.52d);
            if (playerPoint is { } player)
            {
                visual.Line.X1 = player.Left * width;
                visual.Line.Y1 = player.Top * height;
                visual.Line.X2 = target.X;
                visual.Line.Y2 = target.Y;
            }
            SetSelectedChrome(visual.Button, selected);
            Canvas.SetLeft(visual.Button, target.X - visual.Button.Width / 2d);
            Canvas.SetTop(visual.Button, target.Y - visual.Button.Height / 2d);
        }

        if (playerPoint is { } location)
        {
            EnsurePlayerMarker();
            _playerMarker!.Visibility = Visibility.Visible;
            _playerHeadingTransform!.Angle = _playerHeading;
            Canvas.SetLeft(_playerMarker, location.Left * width - _playerMarker.Width / 2d);
            Canvas.SetTop(_playerMarker, location.Top * height - _playerMarker.Height / 2d);
        }
        else if (_playerMarker is not null)
        {
            _playerMarker.Visibility = Visibility.Collapsed;
        }

        var teamPingCount = notes.Count(note => note.IsTeamPing);
        NoteCountLabel.Text = teamPingCount == 0
            ? $"{notes.Count} MỐC"
            : $"{notes.Count} MỐC · {teamPingCount} NHÓM";
        UpdateSelectionDetail();
    }

    private void UpdateDynamicVisuals()
    {
        var width = MapSurface.ActualWidth;
        var height = MapSurface.ActualHeight;
        if (width <= 0d || height <= 0d) return;

        var playerPoint = _playerLocation is null
            ? (MapPoint?)null
            : GatewayMapProjection.Project(_playerLocation);
        foreach (var note in VisibleNotes())
        {
            if (!_noteVisuals.TryGetValue(note.Id, out var visual))
            {
                RenderMap();
                return;
            }

            visual.Line.Visibility = playerPoint is null ? Visibility.Collapsed : Visibility.Visible;
            if (playerPoint is not { } player) continue;
            visual.Line.X1 = player.Left * width;
            visual.Line.Y1 = player.Top * height;
            visual.Line.X2 = note.U * width;
            visual.Line.Y2 = note.V * height;
        }

        if (playerPoint is { } current)
        {
            EnsurePlayerMarker();
            _playerMarker!.Visibility = Visibility.Visible;
            _playerHeadingTransform!.Angle = _playerHeading;
            Canvas.SetLeft(_playerMarker, current.Left * width - _playerMarker.Width / 2d);
            Canvas.SetTop(_playerMarker, current.Top * height - _playerMarker.Height / 2d);
        }
        else if (_playerMarker is not null)
        {
            _playerMarker.Visibility = Visibility.Collapsed;
        }

        UpdateSelectionDetail();
    }

    private static void SetSelectedChrome(Button button, bool selected)
    {
        if (button.Content is Border chrome)
        {
            chrome.BorderThickness = new Thickness(selected ? 2d : 1d);
        }
    }

    private Button CreateNoteButton(MapNotePresentation note, MapNotePaletteItem item)
    {
        var borderBrush = BrushFrom(item.Color);
        var icon = MapNoteIconCatalog.CreatePath(item, 18d);
        var chrome = new Border
        {
            Width = 30d,
            Height = 30d,
            CornerRadius = new CornerRadius(15d),
            Background = BrushFrom(note.IsTeamPing ? "#ED0B2421" : "#DF071719"),
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(_selectedNoteId == note.Id ? 2d : 1d),
            Child = icon
        };
        var button = new Button
        {
            Width = 34d,
            Height = 34d,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0d),
            Padding = new Thickness(2d),
            Cursor = note.CanEdit ? Cursors.Hand : Cursors.Arrow,
            Content = chrome,
            Tag = note.Id,
            ToolTip = note.IsTeamPing
                ? $"{item.Label} · Ping của {note.OwnerDisplayName} · X {note.WorldX / 1000d:0.0} / Y {note.WorldY / 1000d:0.0}"
                : $"{item.Label} · Cá nhân · X {note.WorldX / 1000d:0.0} / Y {note.WorldY / 1000d:0.0}"
        };
        button.Click += NoteButton_Click;
        button.MouseRightButtonUp += NoteButton_MouseRightButtonUp;
        return button;
    }

    private async void NoteButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: Guid id }) return;
        _selectedNoteId = id;
        ClearDurableSelectionFeedback();
        UpdateSelectionDetail();
        await DeleteSelectedAsync();
        e.Handled = true;
    }

    private FrameworkElement CreatePlayerMarker()
    {
        var path = new Path
        {
            Data = Geometry.Parse("M16,1 L23,24 L16,19 L9,24 Z"),
            Fill = BrushFrom("#F4FFFB"),
            Stroke = BrushFrom("#37D4C6"),
            StrokeThickness = 1d,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(5d)
        };
        _playerHeadingTransform = new RotateTransform(_playerHeading);
        var marker = new Grid
        {
            Width = 32d,
            Height = 32d,
            RenderTransformOrigin = new Point(0.5d, 0.5d),
            RenderTransform = _playerHeadingTransform
        };
        marker.Children.Add(new Ellipse
        {
            Stroke = BrushFrom("#A337D4C6"),
            StrokeThickness = 1d
        });
        marker.Children.Add(path);
        return marker;
    }

    private void EnsurePlayerMarker()
    {
        if (_playerMarker is not null) return;
        _playerMarker = CreatePlayerMarker();
        PlayerLayer.Children.Add(_playerMarker);
    }

    private void UpdateSelectionDetail()
    {
        if (_durableSelectionFeedback is not null)
        {
            SelectionDetailLabel.Text = _durableSelectionFeedback;
            SelectionDetailLabel.Foreground = BrushFrom(
                _durableSelectionFeedbackIsError ? "#EF8D7C" : "#8FE3D0");
            RefreshDeleteButton();
            return;
        }

        SelectionDetailLabel.Foreground = BrushFrom("#8CA59D");
        var notes = VisibleNotes();
        var note = _selectedNoteId is { } id
            ? notes.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        if (note is null)
        {
            SelectionDetailLabel.Text = notes.Count == 0
                ? "Chọn một vị trí trên bản đồ"
                : "Click mốc để chọn · Delete hoặc chuột phải để xóa";
            RefreshDeleteButton();
            return;
        }

        var item = MapNoteIconCatalog.For(note.Kind);
        var distance = _playerLocation is null
            ? "CHỜ GPS"
            : FormatDistance(_playerLocation, note);
        var ownership = note.IsTeamPing
            ? note.CanEdit
                ? $"PING NHÓM CỦA BẠN · {note.OwnerDisplayName}"
                : $"PING CỦA {note.OwnerDisplayName} · CHỈ CHỦ PING ĐƯỢC SỬA"
            : "MỐC CÁ NHÂN";
        SelectionDetailLabel.Text = $"{item.Label.ToUpperInvariant()} · {ownership} · X {note.WorldX / 1000d:0.0}  Y {note.WorldY / 1000d:0.0} · {distance}";
        RefreshDeleteButton();
    }

    private void RefreshDeleteButton()
    {
        var selected = _selectedNoteId is { } id
            ? VisibleNotes().FirstOrDefault(note => note.Id == id)
            : null;
        DeleteSelectedButton.Visibility = selected is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        DeleteSelectedButton.IsEnabled = selected?.CanEdit == true && !_paletteBusy;
        DeleteSelectedButton.Content = selected?.CanEdit == true
            ? "XÓA MỐC · DELETE"
            : "CHỈ CHỦ PING ĐƯỢC XÓA";
    }

    private void SetDurableSelectionFeedback(string message, bool isError)
    {
        _durableSelectionFeedback = message;
        _durableSelectionFeedbackIsError = isError;
        UpdateSelectionDetail();
    }

    private void ClearDurableSelectionFeedback()
    {
        _durableSelectionFeedback = null;
        _durableSelectionFeedbackIsError = false;
    }

    private static string FormatDistance(WorldLocation player, MapNotePresentation note)
    {
        var dx = player.X - note.WorldX;
        var dy = player.Y - note.WorldY;
        var meters = Math.Sqrt(dx * dx + dy * dy) / 100d;
        return meters >= 1_000d ? $"{meters / 1_000d:0.0} KM" : $"{meters:0} M";
    }

    private void Store_Changed(object? sender, EventArgs e) => RenderMap();

    private void MapSurface_SizeChanged(object sender, SizeChangedEventArgs e) => RenderMap();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete
                 && Keyboard.FocusedElement is not TextBoxBase)
        {
            await DeleteSelectedAsync();
            e.Handled = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
        MarkerPalettePopup.IsOpen = false;
        _store.Changed -= Store_Changed;
    }

    private IReadOnlyList<MapNotePresentation> VisibleNotes() =>
        MapNotePresentationBuilder.Merge(_store.Notes, _teamState);

    private void ApplyTeamPing(TeamMapPingSnapshot ping)
    {
        var pings = _teamState.MapPings
            .Where(candidate => candidate.PingId != ping.PingId)
            .Append(ping)
            .OrderBy(candidate => candidate.CreatedAt)
            .ToArray();
        _teamState = _teamState with { MapPings = pings };
    }

    private void RemoveTeamPing(Guid pingId) =>
        _teamState = _teamState with
        {
            MapPings = _teamState.MapPings.Where(ping => ping.PingId != pingId).ToArray()
        };

    private void SetPaletteEnabled(bool enabled)
    {
        foreach (var button in PaletteGrid.Children.OfType<Button>())
        {
            button.IsEnabled = enabled;
        }
    }

    private static string FriendlyPingError(TeamMapPingException exception) => exception.Code switch
    {
        "ping_not_owned" => "Chỉ chủ ping mới được sửa hoặc xóa",
        "ping_limit_reached" => "Đã đạt giới hạn 12 ping/người trong nhóm",
        "stale_ping_revision" => "Ping vừa thay đổi · click lại để tải trạng thái mới",
        "relay_not_connected" => "Relay nhóm đang mất kết nối",
        _ => exception.Message
    };

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private sealed record NoteVisual(
        Line Line,
        Button Button,
        MapNoteKind Kind,
        bool IsTeamPing,
        bool CanEdit,
        long Revision);

    private sealed record StaticLayerVisual(
        string Id,
        MapLayerGroup Group,
        FrameworkElement Shape,
        FrameworkElement? Label,
        IReadOnlyList<MapPoint> Points,
        MapPoint Anchor,
        string? ResourceKey = null);
}
