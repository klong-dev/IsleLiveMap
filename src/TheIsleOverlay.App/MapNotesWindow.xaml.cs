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
    private static readonly Uri GatewayMapResourceUri = new("Assets/GatewayMap.jpg", UriKind.Relative);
    private readonly MapNoteStore _store;
    private readonly CancellationTokenSource _shutdown = new();
    private Guid? _selectedNoteId;
    private WorldLocation? _playerLocation;
    private double _playerHeading;
    private TeamRelayState _teamState;
    private bool _paletteBusy;

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
        LoadMap();
        BuildPalette();
        _store.Changed += Store_Changed;
    }

    public void UpdatePlayer(WorldLocation? location, double heading)
    {
        _playerLocation = location;
        _playerHeading = heading;
        if (IsLoaded)
        {
            RenderMap();
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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var mapHeight = Math.Clamp(workArea.Height * 0.65d, 430d, Math.Max(430d, workArea.Height - 150d));
        var mapWidth = mapHeight * 1112d / 1116d;
        MapFrame.Width = mapWidth;
        MapFrame.Height = mapHeight;
        Width = mapWidth + 44d;
        Height = mapHeight + 118d;
        Left = workArea.Left + (workArea.Width - Width) / 2d;
        Top = workArea.Top + (workArea.Height - Height) / 2d;
        RenderMap();
        Activate();
        Focus();
    }

    private void LoadMap()
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
        MapImage.Source = image;
    }

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
        var u = position.X / width;
        var v = position.Y / height;
        if (_teamState.HasActiveSession)
        {
            if (_teamState.ConnectionState != TeamRelayConnectionState.Live)
            {
                SelectionDetailLabel.Text = "Relay đang nối lại · chưa thể đặt ping nhóm";
                return;
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
                return;
            }
            catch (TeamMapPingException exception)
            {
                SelectionDetailLabel.Text = FriendlyPingError(exception);
                return;
            }
        }
        else
        {
            var note = _store.AddDefault(u, v);
            _selectedNoteId = note.Id;
        }

        RenderMap();
        OpenPalette(position);
    }

    private void NoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid id } button)
        {
            return;
        }

        _selectedNoteId = id;
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

        _paletteBusy = true;
        SetPaletteEnabled(false);
        try
        {
            if (selected.IsTeamPing)
            {
                if (item.IsDelete)
                {
                    await App.CurrentTeam.DeleteMapPingAsync(id, selected.Revision, _shutdown.Token);
                    RemoveTeamPing(id);
                    _selectedNoteId = null;
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
                }
            }
            else if (item.IsDelete)
            {
                _store.Delete(id);
                _selectedNoteId = null;
            }
            else
            {
                _store.ChangeKind(id, item.Kind!.Value);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }
        catch (TeamMapPingException exception)
        {
            SelectionDetailLabel.Text = FriendlyPingError(exception);
        }
        finally
        {
            _paletteBusy = false;
            SetPaletteEnabled(true);
            MarkerPalettePopup.IsOpen = false;
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

        NoteLineLayer.Children.Clear();
        NoteMarkerLayer.Children.Clear();
        PlayerLayer.Children.Clear();
        NoteLineLayer.Width = NoteMarkerLayer.Width = PlayerLayer.Width = width;
        NoteLineLayer.Height = NoteMarkerLayer.Height = PlayerLayer.Height = height;

        var playerPoint = _playerLocation is null
            ? (MapPoint?)null
            : GatewayMapProjection.Project(_playerLocation);
        var notes = VisibleNotes();
        foreach (var note in notes)
        {
            var item = MapNoteIconCatalog.For(note.Kind);
            var target = new Point(note.U * width, note.V * height);
            if (playerPoint is { } player)
            {
                var brush = BrushFrom(item.Color);
                NoteLineLayer.Children.Add(new Line
                {
                    X1 = player.Left * width,
                    Y1 = player.Top * height,
                    X2 = target.X,
                    Y2 = target.Y,
                    Stroke = brush,
                    StrokeThickness = _selectedNoteId == note.Id ? 2.2d : 1.25d,
                    StrokeDashArray = [5d, 4d],
                    Opacity = _selectedNoteId == note.Id ? 0.92d : (notes.Count >= 4 ? 0.34d : 0.52d)
                });
            }

            var marker = CreateNoteButton(note, item);
            Canvas.SetLeft(marker, target.X - marker.Width / 2d);
            Canvas.SetTop(marker, target.Y - marker.Height / 2d);
            NoteMarkerLayer.Children.Add(marker);
        }

        if (playerPoint is { } location)
        {
            var marker = CreatePlayerMarker();
            Canvas.SetLeft(marker, location.Left * width - marker.Width / 2d);
            Canvas.SetTop(marker, location.Top * height - marker.Height / 2d);
            PlayerLayer.Children.Add(marker);
        }

        var teamPingCount = notes.Count(note => note.IsTeamPing);
        NoteCountLabel.Text = teamPingCount == 0
            ? $"{notes.Count} MỐC"
            : $"{notes.Count} MỐC · {teamPingCount} NHÓM";
        UpdateSelectionDetail();
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
            Cursor = Cursors.Hand,
            Content = chrome,
            Tag = note.Id,
            ToolTip = note.IsTeamPing
                ? $"{item.Label} · Ping của {note.OwnerDisplayName} · X {note.WorldX / 1000d:0.0} / Y {note.WorldY / 1000d:0.0}"
                : $"{item.Label} · Cá nhân · X {note.WorldX / 1000d:0.0} / Y {note.WorldY / 1000d:0.0}"
        };
        button.Click += NoteButton_Click;
        return button;
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
        var marker = new Grid
        {
            Width = 32d,
            Height = 32d,
            RenderTransformOrigin = new Point(0.5d, 0.5d),
            RenderTransform = new RotateTransform(_playerHeading)
        };
        marker.Children.Add(new Ellipse
        {
            Stroke = BrushFrom("#A337D4C6"),
            StrokeThickness = 1d
        });
        marker.Children.Add(path);
        return marker;
    }

    private void UpdateSelectionDetail()
    {
        var notes = VisibleNotes();
        var note = _selectedNoteId is { } id
            ? notes.FirstOrDefault(candidate => candidate.Id == id)
            : null;
        if (note is null)
        {
            SelectionDetailLabel.Text = notes.Count == 0
                ? "Chọn một vị trí trên bản đồ"
                : "Click một mốc để đổi biểu tượng hoặc xóa";
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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
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
}
