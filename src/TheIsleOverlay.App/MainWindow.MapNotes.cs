using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TheIsleOverlay.Core;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private readonly MapNoteStore _mapNoteStore = new();
    private MapNotesWindow? _mapNotesWindow;
    private TeamRelayState _mapNoteTeamState = new();
    private readonly Dictionary<Guid, MiniMapNoteVisual> _miniMapNoteVisuals = [];

    private void InitializeMapNotes() => _mapNoteStore.Changed += MapNoteStore_Changed;

    private void DetachMapNotes()
    {
        _mapNoteStore.Changed -= MapNoteStore_Changed;
        if (_mapNotesWindow is not null)
        {
            _mapNotesWindow.Close();
            _mapNotesWindow = null;
        }
        MapNoteLineLayer.Children.Clear();
        MapNoteMarkerLayer.Children.Clear();
        _miniMapNoteVisuals.Clear();
        _mapNoteTeamState = new TeamRelayState();
    }

    private void ToggleMapNotesWindow()
    {
        if (!HasCurrentProFeatures)
        {
            _mapNotesWindow?.Close();
            return;
        }

        if (_mapNotesWindow is not null)
        {
            _mapNotesWindow.Close();
            return;
        }

        if (!_clickThrough)
        {
            SetClickThrough(true);
        }

        var window = new MapNotesWindow(
            _mapNoteStore,
            _location,
            _hasMovementHeading ? _headingDegrees : 0d,
            _mapNoteTeamState)
        {
            Owner = this
        };
        window.Closed += MapNotesWindow_Closed;
        _mapNotesWindow = window;
        window.Show();
    }

    private void DisableProMapFeatures()
    {
        _mapNotesWindow?.Close();
        _mapNotesWindow = null;
        _mapNoteTeamState = new TeamRelayState();
        MapNoteLineLayer.Children.Clear();
        MapNoteMarkerLayer.Children.Clear();
        _miniMapNoteVisuals.Clear();
        ClearRemotePlayerMarkers();
    }

    private void MapNotesWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is MapNotesWindow window)
        {
            window.Closed -= MapNotesWindow_Closed;
        }
        _mapNotesWindow = null;
    }

    private void UpdateMapNotesPlayer() => _mapNotesWindow?.UpdatePlayer(
        _location,
        _hasMovementHeading ? _headingDegrees : 0d);

    private void UpdateTeamMapPings(TeamRelayState state)
    {
        _mapNoteTeamState = state;
        _mapNotesWindow?.UpdateTeamState(state);
        PositionMap();
    }

    private void MapNoteStore_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            PositionMap();
        }
        else
        {
            Dispatcher.BeginInvoke(PositionMap);
        }
    }

    private void PositionMapNotes(
        MapPoint? playerPoint,
        double imageLeft,
        double imageTop,
        double imageWidth,
        double imageHeight)
    {
        var width = MapViewport.ActualWidth;
        var height = MapViewport.ActualHeight;
        MapNoteLineLayer.Width = MapNoteMarkerLayer.Width = width;
        MapNoteLineLayer.Height = MapNoteMarkerLayer.Height = height;
        if (width <= 0d || height <= 0d)
        {
            ClearMiniMapNoteVisuals();
            return;
        }

        Point? player = playerPoint is { } current
            ? new Point(
                imageLeft + current.Left * imageWidth,
                imageTop + current.Top * imageHeight)
            : null;
        if (!HasCurrentProFeatures)
        {
            ClearMiniMapNoteVisuals();
            return;
        }

        var notes = MapNotePresentationBuilder.Merge(_mapNoteStore.Notes, _mapNoteTeamState);
        var visibleIds = notes.Select(note => note.Id).ToHashSet();
        foreach (var removedId in _miniMapNoteVisuals.Keys
                     .Where(id => !visibleIds.Contains(id))
                     .ToArray())
        {
            var removed = _miniMapNoteVisuals[removedId];
            MapNoteLineLayer.Children.Remove(removed.Line);
            MapNoteMarkerLayer.Children.Remove(removed.Marker);
            _miniMapNoteVisuals.Remove(removedId);
        }

        foreach (var note in notes)
        {
            var item = MapNoteIconCatalog.For(note.Kind);
            var target = new Point(
                imageLeft + note.U * imageWidth,
                imageTop + note.V * imageHeight);
            var visibleTarget = player is { } start
                ? ClipEndpoint(start, target, width, height, 12d)
                : new Point(
                    Math.Clamp(target.X, 12d, Math.Max(12d, width - 12d)),
                    Math.Clamp(target.Y, 12d, Math.Max(12d, height - 12d)));
            if (!_miniMapNoteVisuals.TryGetValue(note.Id, out var visual)
                || visual.Kind != note.Kind
                || visual.IsTeamPing != note.IsTeamPing
                || visual.Revision != note.Revision)
            {
                if (visual is not null)
                {
                    MapNoteLineLayer.Children.Remove(visual.Line);
                    MapNoteMarkerLayer.Children.Remove(visual.Marker);
                }

                var line = new Line
                {
                    Stroke = BrushFrom(item.Color),
                    StrokeThickness = 1.25d,
                    StrokeDashArray = [4d, 3d],
                    IsHitTestVisible = false
                };
                var marker = CreateMiniMapNote(item);
                visual = new MiniMapNoteVisual(line, marker, note.Kind, note.IsTeamPing, note.Revision);
                _miniMapNoteVisuals[note.Id] = visual;
                MapNoteLineLayer.Children.Add(line);
                MapNoteMarkerLayer.Children.Add(marker);
            }

            visual.Line.Visibility = player is null ? Visibility.Collapsed : Visibility.Visible;
            if (player is { } origin)
            {
                visual.Line.X1 = origin.X;
                visual.Line.Y1 = origin.Y;
                visual.Line.X2 = visibleTarget.X;
                visual.Line.Y2 = visibleTarget.Y;
                visual.Line.Opacity = notes.Count >= 4 ? 0.34d : 0.5d;
            }
            Canvas.SetLeft(visual.Marker, visibleTarget.X - visual.Marker.Width / 2d);
            Canvas.SetTop(visual.Marker, visibleTarget.Y - visual.Marker.Height / 2d);
        }
    }

    private void ClearMiniMapNoteVisuals()
    {
        MapNoteLineLayer.Children.Clear();
        MapNoteMarkerLayer.Children.Clear();
        _miniMapNoteVisuals.Clear();
    }

    private static FrameworkElement CreateMiniMapNote(MapNotePaletteItem item)
    {
        var border = new Border
        {
            Width = 20d,
            Height = 20d,
            CornerRadius = new CornerRadius(10d),
            Background = BrushFrom("#D9071719"),
            BorderBrush = BrushFrom(item.Color),
            BorderThickness = new Thickness(1d),
            Child = MapNoteIconCatalog.CreatePath(item, 11d),
            IsHitTestVisible = false
        };
        return border;
    }

    internal static Point ClipEndpoint(
        Point start,
        Point target,
        double width,
        double height,
        double margin)
    {
        var minX = margin;
        var minY = margin;
        var maxX = Math.Max(minX, width - margin);
        var maxY = Math.Max(minY, height - margin);
        if (target.X >= minX && target.X <= maxX && target.Y >= minY && target.Y <= maxY)
        {
            return target;
        }

        var dx = target.X - start.X;
        var dy = target.Y - start.Y;
        var t = 1d;
        if (dx > 0d) t = Math.Min(t, (maxX - start.X) / dx);
        else if (dx < 0d) t = Math.Min(t, (minX - start.X) / dx);
        if (dy > 0d) t = Math.Min(t, (maxY - start.Y) / dy);
        else if (dy < 0d) t = Math.Min(t, (minY - start.Y) / dy);
        t = Math.Clamp(double.IsFinite(t) ? t : 0d, 0d, 1d);
        return new Point(
            Math.Clamp(start.X + dx * t, minX, maxX),
            Math.Clamp(start.Y + dy * t, minY, maxY));
    }

    private sealed record MiniMapNoteVisual(
        Line Line,
        FrameworkElement Marker,
        MapNoteKind Kind,
        bool IsTeamPing,
        long Revision);
}
