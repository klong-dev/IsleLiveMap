using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using TheIsleOverlay.Core;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private static readonly string[] TeamAccentColors =
    [
        "#E7B74E", "#D77CB4", "#83C66B", "#78A7E8", "#D7854D",
        "#B09CEC", "#7DD2A2", "#E86F64", "#63C1D5"
    ];

    private readonly Dictionary<Guid, TeamMapMarker> _teamMapMarkers = [];
    private TeamRelayState _pendingTeamState = new();
    private DispatcherTimer? _teamRenderTimer;
    private string? _localServerKey;
    private volatile bool _teamStateDirty;

    private void InitializeTeamOverlay()
    {
        App.CurrentTeam.StateChanged += TeamCoordinator_StateChanged;
        _pendingTeamState = App.CurrentTeam.CurrentState;
        _teamStateDirty = true;
        _teamRenderTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Render,
            TeamRenderTimer_Tick,
            Dispatcher);
        _teamRenderTimer.Start();
    }

    private void DetachTeamOverlay()
    {
        App.CurrentTeam.StateChanged -= TeamCoordinator_StateChanged;
        _teamRenderTimer?.Stop();
        _teamRenderTimer = null;
        ClearTeamMarkers();
    }

    private void TeamCoordinator_StateChanged(object? sender, TeamRelayState state)
    {
        _pendingTeamState = state;
        _teamStateDirty = true;
    }

    private void TeamRenderTimer_Tick(object? sender, EventArgs e)
    {
        if (!_teamStateDirty)
        {
            return;
        }

        _teamStateDirty = false;
        RenderTeamState(_pendingTeamState);
    }

    private void PublishTeamTelemetry(TelemetrySnapshot snapshot)
    {
        _localServerKey = snapshot is { PlayerOnline: true, Player: { } player }
            ? player.Server
            : null;
        App.CurrentTeam.UpdateTelemetry(
            snapshot,
            _hasMovementHeading ? _headingDegrees : null);
        _pendingTeamState = App.CurrentTeam.CurrentState;
        _teamStateDirty = true;
    }

    private void RenderTeamState(TeamRelayState state)
    {
        UpdateTeamMapPings(HasCurrentProFeatures ? state : new TeamRelayState());
        if (!state.HasActiveSession || state.Session is not { } session)
        {
            RefreshOptionalWidgetVisibility();
            TeamMembersList.ItemsSource = null;
            ClearTeamMarkers();
            return;
        }

        TeamPanel.Visibility = Visibility.Visible;
        TeamCodeLabel.Text = $"TEAM // {session.InviteCode}";
        TeamCountLabel.Text = $"{state.Members.Count} NGƯỜI";
        TeamConnectionLabel.Text = state.ConnectionState switch
        {
            TeamRelayConnectionState.Live => "RELAY · LIVE",
            TeamRelayConnectionState.Reconnecting => "RELAY · NỐI LẠI",
            TeamRelayConnectionState.Connecting => "RELAY · KẾT NỐI",
            _ => "RELAY"
        };
        TeamConnectionDot.Fill = state.ConnectionState == TeamRelayConnectionState.Live
            ? OnlineBrush
            : WaitingBrush;

        var peers = state.Members
            .Where(member => member.MemberId != session.MemberId)
            .ToArray();
        TeamEmptyLabel.Visibility = peers.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        TeamMembersList.Visibility = peers.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TeamMembersList.ItemsSource = peers
            .Select(CreateTeamMemberRow)
            .ToArray();

        SyncTeamMarkers(peers);
        KeepWidgetsVisible();
        PositionMap();
    }

    private TeamMemberRowViewModel CreateTeamMemberRow(TeamMemberSnapshot member)
    {
        var telemetry = member.Telemetry;
        var sameServer = IsSameServer(_localServerKey, telemetry?.ServerKey);
        var status = !member.IsOnline
            ? "MẤT TÍN HIỆU"
            : telemetry is null || string.IsNullOrWhiteSpace(telemetry.ServerKey)
                ? "CHỜ DINO"
                : string.IsNullOrWhiteSpace(_localServerKey)
                    ? "CHỜ SERVER"
                    : sameServer
                        ? "CÙNG SERVER"
                        : "KHÁC SERVER";
        var species = string.IsNullOrWhiteSpace(telemetry?.Species)
            ? "NO DINO"
            : FriendlySpecies(telemetry.Species);

        return new TeamMemberRowViewModel
        {
            DisplayName = member.DisplayName,
            DetailText = $"{status} · {species}",
            DetailBrush = status == "CÙNG SERVER"
                ? OnlineBrush
                : status == "MẤT TÍN HIỆU"
                    ? ErrorBrush
                    : WaitingBrush,
            HealthText = FormatTeamPercent(telemetry?.HealthPercent),
            HungerText = FormatTeamPercent(telemetry?.HungerPercent),
            ThirstText = FormatTeamPercent(telemetry?.ThirstPercent),
            AccentBrush = AccentBrush(member.MemberId),
            Opacity = !member.IsOnline ? 0.48d : sameServer ? 1d : 0.68d
        };
    }

    private void SyncTeamMarkers(IReadOnlyList<TeamMemberSnapshot> peers)
    {
        var visibleIds = new HashSet<Guid>();
        foreach (var member in peers)
        {
            if (!TryGetMapPoint(member, out var point))
            {
                continue;
            }

            visibleIds.Add(member.MemberId);
            if (!_teamMapMarkers.TryGetValue(member.MemberId, out var marker))
            {
                marker = CreateTeamMarker(member);
                _teamMapMarkers.Add(member.MemberId, marker);
                TeamMarkerLayer.Children.Add(marker.Root);
            }

            marker.Point = point;
            marker.Root.Opacity = member.IsOnline ? 1d : 0.45d;
            marker.NameLabel.Text = member.DisplayName;
            UpdateTeamHeading(marker, member.Telemetry?.HeadingDegrees);
        }

        foreach (var memberId in _teamMapMarkers.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
        {
            TeamMarkerLayer.Children.Remove(_teamMapMarkers[memberId].Root);
            _teamMapMarkers.Remove(memberId);
        }
    }

    private bool TryGetMapPoint(TeamMemberSnapshot member, out MapPoint point)
    {
        var telemetry = member.Telemetry;
        if (!member.IsOnline
            || telemetry is null
            || !IsSameServer(_localServerKey, telemetry.ServerKey)
            || telemetry.MapId is { Length: > 0 } mapId
                && !string.Equals(mapId, "gateway", StringComparison.OrdinalIgnoreCase))
        {
            point = default;
            return false;
        }

        var worldLocation = telemetry.WorldX is { } worldX
            && telemetry.WorldY is { } worldY
            && double.IsFinite(worldX)
            && double.IsFinite(worldY)
                ? new WorldLocation { X = worldX, Y = worldY }
                : null;
        MapPoint? providerMapLocation = telemetry.MapLeft is { } left
            && telemetry.MapTop is { } top
            && double.IsFinite(left)
            && double.IsFinite(top)
                ? new MapPoint(left, top)
                : null;

        var resolved = GatewayMapProjection.ResolveForBundledTexture(
            worldLocation,
            providerMapLocation);
        if (resolved is { } mapPoint)
        {
            point = mapPoint;
            return true;
        }

        point = default;
        return false;
    }

    private TeamMapMarker CreateTeamMarker(TeamMemberSnapshot member)
    {
        var accent = AccentBrush(member.MemberId);
        var heading = new RotateTransform();
        var root = new Grid
        {
            Width = 116,
            Height = 34,
            IsHitTestVisible = false
        };

        var target = new Grid
        {
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        target.Children.Add(new Ellipse
        {
            Width = 22,
            Height = 22,
            Fill = BrushFrom("#C20A1718"),
            Stroke = accent,
            StrokeThickness = 1.2
        });
        var needle = new Path
        {
            Data = Geometry.Parse("M 15,1 L 20,17 L 15,14 L 10,17 Z"),
            Fill = accent,
            Stroke = BrushFrom("#E9F4EE"),
            StrokeThickness = 0.45,
            RenderTransform = heading,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        target.Children.Add(needle);
        root.Children.Add(target);

        var nameLabel = new TextBlock
        {
            Text = member.DisplayName,
            Foreground = BrushFrom("#E9F4EE"),
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 78
        };
        root.Children.Add(new Border
        {
            Background = BrushFrom("#C8071717"),
            BorderBrush = accent,
            BorderThickness = new Thickness(1, 0, 0, 0),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(27, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = nameLabel
        });

        return new TeamMapMarker(root, nameLabel, heading);
    }

    private static void UpdateTeamHeading(TeamMapMarker marker, double? headingDegrees)
    {
        if (headingDegrees is not { } value || !double.IsFinite(value))
        {
            marker.Heading.Opacity = 0.45d;
            return;
        }

        marker.Heading.Opacity = 1d;
        var target = MapHeading.Normalize(value);
        var current = marker.HeadingTransform.Angle;
        var currentNormalized = MapHeading.Normalize(current);
        var shortestDelta = (target - currentNormalized + 540d) % 360d - 180d;
        marker.HeadingTransform.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation
            {
                From = current,
                To = current + shortestDelta,
                Duration = TimeSpan.FromMilliseconds(90),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void PositionTeamMarkers(double left, double top, double imageWidth, double imageHeight)
    {
        TeamMarkerLayer.Width = MapViewport.ActualWidth;
        TeamMarkerLayer.Height = MapViewport.ActualHeight;
        Canvas.SetLeft(TeamMarkerLayer, 0d);
        Canvas.SetTop(TeamMarkerLayer, 0d);

        foreach (var marker in _teamMapMarkers.Values)
        {
            Canvas.SetLeft(marker.Root, left + marker.Point.Left * imageWidth - 15d);
            Canvas.SetTop(marker.Root, top + marker.Point.Top * imageHeight - 15d);
        }
    }

    private void ClearTeamMarkers()
    {
        TeamMarkerLayer.Children.Clear();
        _teamMapMarkers.Clear();
    }

    private static bool IsSameServer(string? localServer, string? remoteServer) =>
        !string.IsNullOrWhiteSpace(localServer)
        && !string.IsNullOrWhiteSpace(remoteServer)
        && string.Equals(localServer.Trim(), remoteServer.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FormatTeamPercent(double? value) => value is { } percent
        ? $"{Math.Clamp(percent, 0d, 100d):0}%"
        : "—";

    private static SolidColorBrush AccentBrush(Guid memberId)
    {
        var index = (int)((uint)memberId.GetHashCode() % TeamAccentColors.Length);
        return BrushFrom(TeamAccentColors[index]);
    }

    public sealed record TeamMemberRowViewModel
    {
        public required string DisplayName { get; init; }
        public required string DetailText { get; init; }
        public required Brush DetailBrush { get; init; }
        public required string HealthText { get; init; }
        public required string HungerText { get; init; }
        public required string ThirstText { get; init; }
        public required Brush AccentBrush { get; init; }
        public double Opacity { get; init; }
    }

    private sealed class TeamMapMarker(
        Grid root,
        TextBlock nameLabel,
        RotateTransform headingTransform)
    {
        public Grid Root { get; } = root;
        public TextBlock NameLabel { get; } = nameLabel;
        public RotateTransform HeadingTransform { get; } = headingTransform;
        public UIElement Heading { get; } = AssertHeading(root);
        public MapPoint Point { get; set; }

        private static UIElement AssertHeading(Grid markerRoot) =>
            ((Grid)markerRoot.Children[0]).Children[1];
    }
}
