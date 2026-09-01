using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TheIsleOverlay.Core;
using TheIsleOverlay.LocalTelemetry;

namespace TheIsleOverlay.App;

public partial class MainWindow : Window
{
    private static readonly Uri GatewayMapResourceUri = new("Assets/GatewayMap.jpg", UriKind.Relative);
    private static readonly TimeSpan LiveHeadingAnimationDuration = TimeSpan.FromMilliseconds(35);
    private static readonly TimeSpan MovementHeadingAnimationDuration = TimeSpan.FromMilliseconds(80);

    private const int EditHotkeyId = 0x714;
    private const int ToggleMissionsNHotkeyId = 0x715;
    private const int ToggleHudHotkeyId = 0x716;
    private const int MapNotesHotkeyId = 0x717;
    private const int WmHotkey = 0x0312;
    private const int WmInput = 0x00FF;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint KeyO = 0x4F;
    private const uint KeyN = 0x4E;
    private const uint KeyP = 0x50;
    private const uint KeyM = 0x4D;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;

    private static readonly SolidColorBrush OnlineBrush = BrushFrom("#37D4C6");
    private static readonly SolidColorBrush WaitingBrush = BrushFrom("#E7B74E");
    private static readonly SolidColorBrush ErrorBrush = BrushFrom("#DC5A56");

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TelemetrySourceDefinition? _requestedSource;
    private readonly string? _providedCookie;
    private readonly ITelemetrySession? _providedSession;
    private readonly IRemotePlayerTelemetrySource? _remotePlayerSource;
    private readonly ILocalMovementSource? _providedLocalSource;
    private ProFeatureAccessGrant _proFeatureAccess;
    private readonly OverlayLayoutSettingsStore _layoutSettingsStore = new();
    private readonly object _renderSnapshotGate = new();
    private ITelemetrySession? _telemetrySession;
    private Task? _telemetryWatchTask;
    private TelemetrySnapshot? _pendingRenderSnapshot;
    private OverlayLayoutSettings _layoutSettings = new();
    private string _configuredSource = "ERA";
    private WorldLocation? _location;
    private MapPoint? _mapLocation;
    private WorldLocation? _previousLocation;
    private GlobalMouseShortcutHook? _mouseShortcuts;
    private HwndSource? _windowSource;
    private double _mapZoom = MapZoomRules.DefaultZoom;
    private double _mapPanStartImageWidth;
    private double _mapPanStartImageHeight;
    private double _mapPanStartDpiScaleX = 1d;
    private double _mapPanStartDpiScaleY = 1d;
    private double _mapPanRawDeltaX;
    private double _mapPanRawDeltaY;
    private double _headingDegrees;
    private double _overlayScale = OverlayLayoutRules.DefaultScale;
    private double _resizeStartingScale;
    private Point _resizeStartingScreenPoint;
    private bool _clickThrough;
    private bool _resizingOverlay;
    private bool _hasMovementHeading;
    private bool _editHotkeyRegistered;
    private bool _toggleMissionsNHotkeyRegistered;
    private bool _toggleHudHotkeyRegistered;
    private bool _mapNotesHotkeyRegistered;
    private bool _hudVisible = true;
    private bool _remotePlayerSourceOwnedBySession;
    private bool _renderSnapshotScheduled;
    private bool _mapPanActive;
    private bool _rawMapPanReceived;
    private DispatcherTimer? _proFeatureExpiryTimer;
    private MapFocusMode _mapFocusMode = MapFocusMode.FollowPlayer;
    private MapPoint? _freeMapFocus;
    private MapPoint _mapPanStartFocus = new(0.5d, 0.5d);
    private GlobalMousePoint _mapPanStartScreenPoint;
    private volatile MapScreenBounds? _mapScreenBounds;
    private FrameworkElement? _draggedWidget;
    private Point _widgetDragStart;
    private Point _widgetOrigin;

    public MainWindow() : this(null, null, null, null, null, null, ProFeatureAccessGrant.Free)
    {
    }

    public MainWindow(TelemetrySourceDefinition? source, string? cookieValue)
        : this(source, cookieValue, null, null, null, null, ProFeatureAccessGrant.Free)
    {
    }

    public MainWindow(
        TelemetrySourceDefinition? source,
        string? cookieValue,
        IRemotePlayerTelemetrySource? remotePlayerSource)
        : this(source, cookieValue, null, null, remotePlayerSource, null, ProFeatureAccessGrant.Free)
    {
    }

    public MainWindow(
        TelemetrySourceDefinition? source,
        string? cookieValue,
        IRemotePlayerTelemetrySource? remotePlayerSource,
        ILocalMovementSource localSource)
        : this(source, cookieValue, null, null, remotePlayerSource, localSource, ProFeatureAccessGrant.Free)
    {
    }

    public MainWindow(ITelemetrySession telemetrySession, string displayName)
        : this(
            null,
            null,
            telemetrySession ?? throw new ArgumentNullException(nameof(telemetrySession)),
            displayName,
            null,
            null,
            ProFeatureAccessGrant.Free)
    {
    }

    public MainWindow(
        ITelemetrySession telemetrySession,
        string displayName,
        IRemotePlayerTelemetrySource? remotePlayerSource)
        : this(
            null,
            null,
            telemetrySession ?? throw new ArgumentNullException(nameof(telemetrySession)),
            displayName,
            remotePlayerSource,
            null,
            ProFeatureAccessGrant.Free)
    {
    }

    internal MainWindow(
        ITelemetrySession telemetrySession,
        string displayName,
        ProFeatureAccessGrant proFeatureAccess)
        : this(
            null,
            null,
            telemetrySession ?? throw new ArgumentNullException(nameof(telemetrySession)),
            displayName,
            null,
            null,
            proFeatureAccess)
    {
    }

    internal MainWindow(
        TelemetrySourceDefinition? source,
        string? cookieValue,
        IRemotePlayerTelemetrySource? remotePlayerSource,
        ILocalMovementSource localSource,
        ProFeatureAccessGrant proFeatureAccess)
        : this(source, cookieValue, null, null, remotePlayerSource, localSource, proFeatureAccess)
    {
    }

    private MainWindow(
        TelemetrySourceDefinition? source,
        string? cookieValue,
        ITelemetrySession? telemetrySession,
        string? displayName,
        IRemotePlayerTelemetrySource? remotePlayerSource,
        ILocalMovementSource? localSource,
        ProFeatureAccessGrant proFeatureAccess)
    {
        _requestedSource = source;
        _providedCookie = cookieValue;
        _providedSession = telemetrySession;
        _remotePlayerSource = remotePlayerSource;
        _providedLocalSource = localSource;
        _proFeatureAccess = proFeatureAccess;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            _configuredSource = displayName;
        }

        InitializeComponent();
        InitializeMapNotes();
        _layoutSettings = _layoutSettingsStore.Load();
        ApplyOverlayScale(_layoutSettings.Scale, persist: false);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PlayerMarker.Visibility = Visibility.Collapsed;
        DirectionNeedle.Opacity = 0.45d;
        InitializeTeamOverlay();

        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        RawMouseInput.TryRegister(handle);
        _editHotkeyRegistered = RegisterHotKey(handle, EditHotkeyId, ModControl | ModShift, KeyO);
        _toggleMissionsNHotkeyRegistered = RegisterHotKey(handle, ToggleMissionsNHotkeyId, ModAlt, KeyN);
        _toggleHudHotkeyRegistered = RegisterHotKey(handle, ToggleHudHotkeyId, ModAlt, KeyP);
        _mapNotesHotkeyRegistered = HasCurrentProFeatures
            && RegisterHotKey(handle, MapNotesHotkeyId, ModAlt, KeyM);
        StartProFeatureExpiryWatch();
        ConfigureWorkspaceBounds();
        RestoreWidgetLayout();
        InstallMouseShortcuts();
        if (_editHotkeyRegistered)
        {
            SetClickThrough(true);
        }

        if (!TryConfigureTelemetrySession())
        {
            SetConnectionState("CHƯA CẤU HÌNH NGUỒN", ErrorBrush);
            PlayerNameLabel.Text = "Set cookie cho Era hoặc DinoVietnam";
            MapStateLabel.Text = "CHƯA CÓ PHIÊN ĐĂNG NHẬP";
            return;
        }

        if (_telemetrySession is not LocalPositionTelemetrySession)
        {
            _telemetrySession = new LocalPositionTelemetrySession(
                _telemetrySession,
                _providedLocalSource,
                sourceName: _configuredSource,
                remotePlayerSource: _remotePlayerSource);
            _remotePlayerSourceOwnedBySession = _remotePlayerSource is not null;
        }

        LoadMap();
        _telemetryWatchTask = WatchTelemetryAsync();
    }

    private bool HasCurrentProFeatures => _proFeatureAccess.IsActiveAt(DateTimeOffset.UtcNow);

    private void StartProFeatureExpiryWatch()
    {
        _proFeatureExpiryTimer?.Stop();
        _proFeatureExpiryTimer = null;
        if (!HasCurrentProFeatures || _proFeatureAccess.ExpiresAt is not { } expiresAt)
        {
            return;
        }

        var remaining = expiresAt - DateTimeOffset.UtcNow;
        _proFeatureExpiryTimer = new DispatcherTimer(
            remaining <= TimeSpan.FromSeconds(30)
                ? TimeSpan.FromMilliseconds(Math.Max(250d, remaining.TotalMilliseconds + 100d))
                : TimeSpan.FromSeconds(30),
            DispatcherPriority.Normal,
            ProFeatureExpiryTimer_Tick,
            Dispatcher);
        _proFeatureExpiryTimer.Start();
    }

    private void ProFeatureExpiryTimer_Tick(object? sender, EventArgs e)
    {
        if (HasCurrentProFeatures)
        {
            return;
        }

        _proFeatureExpiryTimer?.Stop();
        _proFeatureExpiryTimer = null;
        _proFeatureAccess = ProFeatureAccessGrant.Free;
        if (_mapNotesHotkeyRegistered)
        {
            UnregisterHotKey(new WindowInteropHelper(this).Handle, MapNotesHotkeyId);
            _mapNotesHotkeyRegistered = false;
        }

        DisableProMapFeatures();
    }

    private bool TryConfigureTelemetrySession()
    {
        if (_providedSession is not null)
        {
            _telemetrySession = _providedSession;
            return true;
        }

        if (_requestedSource is not null && !string.IsNullOrWhiteSpace(_providedCookie))
        {
            ConfigureTelemetrySession(_requestedSource, _providedCookie);
            return true;
        }

        var requestedSourceId = Environment.GetEnvironmentVariable("TELEMETRY_SOURCE")?.Trim().ToLowerInvariant();
        if (requestedSourceId == "islepilot")
        {
            requestedSourceId = "dinovietnam";
        }

        var source = TelemetrySourceDefinition.FromId(requestedSourceId);
        var cookie = source?.Id switch
        {
            "era" => Environment.GetEnvironmentVariable("ERA_SESSION"),
            "dinovietnampremium" => Environment.GetEnvironmentVariable("ISLEPILOT_PREMIUM_PLAYER") ??
                                      Environment.GetEnvironmentVariable("ISLEPILOT_PLAYER"),
            "hoho" => Environment.GetEnvironmentVariable("ISLEPILOT_HOHO_PLAYER") ??
                      Environment.GetEnvironmentVariable("ISLEPILOT_PLAYER"),
            "dinovietnam" => Environment.GetEnvironmentVariable("ISLEPILOT_PLAYER"),
            "pandora" => Environment.GetEnvironmentVariable("PANDORA_SESSION"),
            _ => null
        };

        if (source is not null && !string.IsNullOrWhiteSpace(cookie))
        {
            ConfigureTelemetrySession(source, cookie);
            return true;
        }

        return false;
    }

    private void ConfigureTelemetrySession(TelemetrySourceDefinition source, string cookieValue)
    {
        var provider = source.CreateProvider(_httpClient, cookieValue);
        var interval = source.Kind == TelemetrySourceKind.Pandora
            ? TimeSpan.FromSeconds(5)
            : (TimeSpan?)null;
        _telemetrySession = new PollingTelemetrySession(
            provider,
            interval,
            source: source.ShortName);
        _configuredSource = source.ShortName;
    }

    private void InstallMouseShortcuts()
    {
        _mouseShortcuts = new GlobalMouseShortcutHook(Dispatcher);
        _mouseShortcuts.CanStartMapPan = CanStartMapPan;
        _mouseShortcuts.ZoomInRequested += ZoomInMap;
        _mouseShortcuts.ZoomOutRequested += ZoomOutMap;
        _mouseShortcuts.ToggleMapRequested += ToggleMap;
        _mouseShortcuts.MapPanStarted += StartMapPan;
        _mouseShortcuts.MapPanMoved += MoveMapPan;
        _mouseShortcuts.MapPanEnded += EndMapPan;
        _mouseShortcuts.FollowMapRequested += FollowPlayerMap;
        _mouseShortcuts.Install();
    }

    private void LoadMap()
    {
        try
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
            InitializeMapLayers();
            MapStateLabel.Visibility = Visibility.Collapsed;
            PositionMap();
        }
        catch
        {
            MapStateLabel.Text = "KHÔNG ĐỌC ĐƯỢC BẢN ĐỒ";
        }
    }

    private async Task WatchTelemetryAsync()
    {
        if (_telemetrySession is null)
        {
            return;
        }

        try
        {
            await foreach (var snapshot in _telemetrySession
                               .WatchAsync(_shutdown.Token)
                               .ConfigureAwait(false))
            {
                QueueRenderSnapshot(snapshot);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            await Dispatcher.InvokeAsync(() =>
                ShowTelemetryUnavailable("MẤT KẾT NỐI", "Telemetry session đã dừng"));
        }
    }

    private void QueueRenderSnapshot(TelemetrySnapshot snapshot)
    {
        lock (_renderSnapshotGate)
        {
            _pendingRenderSnapshot = snapshot;
            if (_renderSnapshotScheduled)
            {
                return;
            }

            _renderSnapshotScheduled = true;
        }

        Dispatcher.BeginInvoke(
            RenderPendingSnapshot,
            DispatcherPriority.Render);
    }

    private void RenderPendingSnapshot()
    {
        TelemetrySnapshot? snapshot;
        lock (_renderSnapshotGate)
        {
            snapshot = _pendingRenderSnapshot;
            _pendingRenderSnapshot = null;
        }

        if (snapshot is not null)
        {
            try
            {
                RenderSnapshot(snapshot);
            }
            catch
            {
                ShowTelemetryUnavailable("MẤT KẾT NỐI", "Telemetry session đã dừng");
            }
        }

        lock (_renderSnapshotGate)
        {
            if (_pendingRenderSnapshot is null)
            {
                _renderSnapshotScheduled = false;
                return;
            }
        }

        Dispatcher.BeginInvoke(
            RenderPendingSnapshot,
            DispatcherPriority.Render);
    }

    private void RenderSnapshot(TelemetrySnapshot snapshot)
    {
        try
        {
            if (snapshot.SessionState == TelemetrySessionState.AuthenticationRequired)
            {
                ShowTelemetryUnavailable("PHIÊN ĐÃ HẾT HẠN", "Đăng nhập lại đúng website nguồn để tiếp tục");
                return;
            }

            if (snapshot.SessionState == TelemetrySessionState.UnsupportedServer)
            {
                ShowNoActiveDinosaur(
                    snapshot.StatusMessage ?? "ISLEPILOT · CHƯA VÀO SERVER HỖ TRỢ",
                    "Server hiện tại chưa cài IslePilot");
                return;
            }

            if (!snapshot.Success || !snapshot.ServerOnline)
            {
                ShowTelemetryUnavailable("SERVER OFFLINE", "Nguồn telemetry đang ngoại tuyến");
                return;
            }

            if (!snapshot.PlayerOnline || snapshot.Player is null)
            {
                var state = ConnectionText(snapshot.SessionState);
                var detail = snapshot.SessionState switch
                {
                    TelemetrySessionState.Connecting => "Đang khởi tạo phiên telemetry",
                    TelemetrySessionState.Reconnecting => "Mất kết nối, đang thử lại",
                    TelemetrySessionState.Stale => "Dữ liệu realtime đã quá hạn",
                    _ => $"Join server {_configuredSource} để nhận telemetry"
                };
                ShowNoActiveDinosaur(state, detail);
                return;
            }

            var player = snapshot.Player;
            var exact = player.ExactVitals;

            var degraded = snapshot.SessionState is TelemetrySessionState.Reconnecting or TelemetrySessionState.Stale;
            SetTelemetryOpacity(degraded ? 0.58d : 1d);
            SetConnectionState(
                ConnectionText(snapshot.SessionState, player.ExactVitalsSource),
                degraded ? WaitingBrush : OnlineBrush);
            SpeciesLabel.Text = FriendlySpecies(player.Class);
            PlayerNameLabel.Text = string.IsNullOrWhiteSpace(player.Name) ? "ACTIVE PLAYER" : player.Name;

            var growth = exact?.Growth ?? player.GrowthPercent;
            GrowthLabel.Text = growth is null
                ? "—"
                : $"{NormalizePercent(growth):0.#}%";

            RenderVital(HealthBar, HealthValue, exact?.Health, exact?.MaxHealth, player.HealthPercent);
            RenderVital(StaminaBar, StaminaValue, exact?.Stamina, exact?.MaxStamina, player.StaminaPercent);

            RenderVital(HungerBar, HungerValue, exact?.Hunger, exact?.MaxHunger, player.HungerPercent);
            RenderVital(WaterBar, WaterValue, exact?.Thirst, exact?.MaxThirst, player.ThirstPercent);
            RenderPrimeMissions(player.Prime);

            UpdatedLabel.Text = $"SYNC {(snapshot.UpdatedAt ?? DateTimeOffset.Now).ToLocalTime():HH:mm:ss}";

            UpdateHeading(player);
            _location = player.Location;
            _mapLocation = player.MapLocation;
            UpdateMapNotesPlayer();
            SyncPlayerHeatmap(snapshot.Map, snapshot.Player);
            SyncRemotePlayerMarkers(snapshot);
            if (_location is not null)
            {
                var altitude = _location.Z is null ? "—" : $"{_location.Z.Value / 1000d:0.0}";
                CoordinateLabel.Text = $"X {_location.X / 1000d:0.0}  Y {_location.Y / 1000d:0.0}  Z {altitude}";
            }
            else
            {
                CoordinateLabel.Text = "X —  Y —  Z —";
            }

            PlayerMarker.Visibility = ResolvePlayerMapPoint() is null
                ? Visibility.Collapsed
                : Visibility.Visible;
            PositionMap();
        }
        finally
        {
            PublishTeamTelemetry(snapshot);
        }
    }

    private void UpdateHeading(PlayerTelemetry player)
    {
        if (player.ExactMapHeadingDegrees is not null)
        {
            _headingDegrees = MapHeading.Normalize(player.ExactMapHeadingDegrees.Value);
            AnimateHeadingTo(_headingDegrees, LiveHeadingAnimationDuration);
            _hasMovementHeading = true;
            DirectionNeedle.Opacity = 1d;
            HeadingModeLabel.Text = $"HEADING · {_headingDegrees:000}° SERVER";
            _previousLocation = player.Location;
            return;
        }

        UpdateMovementHeading(player.Location);
    }

    private void UpdateMovementHeading(WorldLocation? current)
    {
        if (current is null)
        {
            _previousLocation = null;
            return;
        }

        if (_previousLocation is not null && MovementHeading.TryCalculate(_previousLocation, current, out var measuredHeading))
        {
            _headingDegrees = _hasMovementHeading
                ? MovementHeading.Smooth(_headingDegrees, measuredHeading, 0.72d)
                : measuredHeading;
            AnimateHeadingTo(_headingDegrees, MovementHeadingAnimationDuration);
            _hasMovementHeading = true;
            DirectionNeedle.Opacity = 1d;
            HeadingModeLabel.Text = $"COURSE · {_headingDegrees:000}° / 2S";
        }
        else if (!_hasMovementHeading)
        {
            HeadingModeLabel.Text = "COURSE · WAITING";
        }

        _previousLocation = current;
    }

    private void AnimateHeadingTo(double targetDegrees, TimeSpan duration)
    {
        var target = MapHeading.Normalize(targetDegrees);
        if (!_hasMovementHeading)
        {
            PlayerHeadingTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            PlayerHeadingTransform.Angle = target;
            return;
        }

        var current = PlayerHeadingTransform.Angle;
        var currentNormalized = MapHeading.Normalize(current);
        var shortestDelta = (target - currentNormalized + 540d) % 360d - 180d;
        var animation = new DoubleAnimation
        {
            From = current,
            To = current + shortestDelta,
            Duration = duration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        PlayerHeadingTransform.BeginAnimation(
            RotateTransform.AngleProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void RenderVital(System.Windows.Controls.ProgressBar bar, System.Windows.Controls.TextBlock label, double? current, double? maximum, double? fallback)
    {
        if (current is null && maximum is null && fallback is null)
        {
            bar.Value = 0d;
            label.Text = "—";
            return;
        }

        var percent = VitalMath.Percent(current, maximum, fallback);
        bar.Value = percent;
        label.Text = current is not null && maximum is > 0
            ? $"{FormatNumber(current.Value)} / {FormatNumber(maximum.Value)}"
            : current is not null
                ? FormatNumber(current.Value)
            : $"{percent:0.#}%";
    }

    private void ClearVitals()
    {
        HealthBar.Value = StaminaBar.Value = HungerBar.Value = WaterBar.Value = 0;
        HealthValue.Text = StaminaValue.Text = HungerValue.Text = WaterValue.Text = "— / —";
        GrowthLabel.Text = "—";
        CoordinateLabel.Text = "X —  Y —  Z —";
        UpdatedLabel.Text = "—";
    }

    private void ShowTelemetryUnavailable(string connectionState, string detail)
    {
        SetTelemetryOpacity(1d);
        SetConnectionState(connectionState, ErrorBrush);
        SpeciesLabel.Text = "TELEMETRY UNAVAILABLE";
        PlayerNameLabel.Text = detail;
        _location = null;
        _mapLocation = null;
        _previousLocation = null;
        _hasMovementHeading = false;
        ClearRemotePlayerMarkers();
        ClearPlayerHeatmap();
        PlayerMarker.Visibility = Visibility.Collapsed;
        HeadingModeLabel.Text = "COURSE · WAITING";
        ClearVitals();
        ClearPrimeMissions();
        PositionMap();
    }

    private void ShowNoActiveDinosaur(string connectionState, string detail)
    {
        SetTelemetryOpacity(1d);
        SetConnectionState(connectionState, WaitingBrush);
        SpeciesLabel.Text = "NO ACTIVE DINOSAUR";
        PlayerNameLabel.Text = detail;
        _location = null;
        _mapLocation = null;
        _previousLocation = null;
        _hasMovementHeading = false;
        ClearRemotePlayerMarkers();
        ClearPlayerHeatmap();
        PlayerMarker.Visibility = Visibility.Collapsed;
        HeadingModeLabel.Text = "COURSE · WAITING";
        ClearVitals();
        ClearPrimeMissions();
        PositionMap();
    }

    private string ConnectionText(
        TelemetrySessionState state,
        string? directVitalsSource = null)
    {
        var source = string.IsNullOrWhiteSpace(directVitalsSource)
            ? _configuredSource
            : directVitalsSource.Trim();
        return state switch
        {
            TelemetrySessionState.Live => $"{source} · LIVE",
            TelemetrySessionState.Reconnecting => $"{source} · RECONNECTING",
            TelemetrySessionState.Stale => $"{source} · DATA STALE",
            TelemetrySessionState.Polling => $"{source} · POLL 2S",
            _ => $"{source} · {state.ToString().ToUpperInvariant()}"
        };
    }

    private void SetTelemetryOpacity(double opacity)
    {
        PlayerMarker.Opacity = opacity;
        MapHeatmapLayer.Opacity = opacity;
        RemotePlayerMarkerLayer.Opacity = opacity;
        HealthBar.Opacity = StaminaBar.Opacity = HungerBar.Opacity = WaterBar.Opacity = opacity;
        HealthValue.Opacity = StaminaValue.Opacity = HungerValue.Opacity = WaterValue.Opacity = opacity;
        GrowthLabel.Opacity = CoordinateLabel.Opacity = opacity;
    }

    private void PositionMap()
    {
        var viewportWidth = MapViewport.ActualWidth;
        var viewportHeight = MapViewport.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            _mapScreenBounds = null;
            return;
        }

        MapShade.Width = viewportWidth;
        MapShade.Height = viewportHeight;

        if (MapImage.Source is not BitmapSource source || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            _mapScreenBounds = null;
            return;
        }

        var coverScale = Math.Max(viewportWidth / source.PixelWidth, viewportHeight / source.PixelHeight);
        var imageWidth = source.PixelWidth * coverScale * _mapZoom;
        var imageHeight = source.PixelHeight * coverScale * _mapZoom;
        MapImage.Width = imageWidth;
        MapImage.Height = imageHeight;

        var playerPoint = ResolvePlayerMapPoint();
        var anchorPoint = playerPoint ?? new MapPoint(0.5d, 0.5d);
        var focusPoint = _mapFocusMode == MapFocusMode.FollowPlayer
            ? MapPanRules.ClampFocus(anchorPoint, viewportWidth, viewportHeight, imageWidth, imageHeight)
            : MapPanRules.ClampFocus(
                _freeMapFocus ?? anchorPoint,
                viewportWidth,
                viewportHeight,
                imageWidth,
                imageHeight);
        if (_mapFocusMode == MapFocusMode.FreeLook)
        {
            _freeMapFocus = focusPoint;
        }
        var desiredLeft = viewportWidth / 2d - focusPoint.Left * imageWidth;
        var desiredTop = viewportHeight / 2d - focusPoint.Top * imageHeight;
        var left = ClampImageOffset(desiredLeft, viewportWidth, imageWidth);
        var top = ClampImageOffset(desiredTop, viewportHeight, imageHeight);
        Canvas.SetLeft(MapImage, left);
        Canvas.SetTop(MapImage, top);

        Canvas.SetLeft(PlayerMarker, left + anchorPoint.Left * imageWidth - PlayerMarker.Width / 2d);
        Canvas.SetTop(PlayerMarker, top + anchorPoint.Top * imageHeight - PlayerMarker.Height / 2d);
        PositionMapLayers(left, top, imageWidth, imageHeight);
        PositionRemotePlayerMarkers(left, top, imageWidth, imageHeight);
        PositionTeamMarkers(left, top, imageWidth, imageHeight);
        PositionMapNotes(playerPoint, left, top, imageWidth, imageHeight);
        UpdateMapFocusIndicator();
        UpdateMapScreenBounds();
    }

    private MapPoint? ResolvePlayerMapPoint()
        => GatewayMapProjection.ResolveForBundledTexture(_location, _mapLocation);

    private static double ClampImageOffset(double desired, double viewportSize, double imageSize) =>
        imageSize <= viewportSize ? (viewportSize - imageSize) / 2d : Math.Clamp(desired, viewportSize - imageSize, 0d);

    private bool CanStartMapPan(GlobalMousePoint point)
    {
        var bounds = _mapScreenBounds;
        return bounds?.Contains(point) == true;
    }

    private void StartMapPan(GlobalMousePoint point)
    {
        if (!CanStartMapPan(point)
            || !double.IsFinite(MapImage.Width)
            || !double.IsFinite(MapImage.Height)
            || MapImage.Width <= 0d
            || MapImage.Height <= 0d)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(MapViewport);
        _mapPanStartDpiScaleX = Math.Max(0.01d, dpi.DpiScaleX);
        _mapPanStartDpiScaleY = Math.Max(0.01d, dpi.DpiScaleY);
        _mapPanStartImageWidth = MapImage.Width;
        _mapPanStartImageHeight = MapImage.Height;
        var anchorPoint = ResolvePlayerMapPoint() ?? new MapPoint(0.5d, 0.5d);
        _mapPanStartFocus = _mapFocusMode == MapFocusMode.FreeLook && _freeMapFocus is { } freeFocus
            ? MapPanRules.ClampFocus(
                freeFocus,
                MapViewport.ActualWidth,
                MapViewport.ActualHeight,
                MapImage.Width,
                MapImage.Height)
            : MapPanRules.ClampFocus(
                anchorPoint,
                MapViewport.ActualWidth,
                MapViewport.ActualHeight,
                MapImage.Width,
                MapImage.Height);
        _mapPanRawDeltaX = 0d;
        _mapPanRawDeltaY = 0d;
        _rawMapPanReceived = false;
        _freeMapFocus = _mapPanStartFocus;
        _mapFocusMode = MapFocusMode.FreeLook;
        _mapPanStartScreenPoint = point;
        _mapPanActive = true;
        MapPanel.Cursor = Cursors.Hand;
        UpdateMapFocusIndicator();
    }

    private void MoveMapPan(GlobalMousePoint point)
    {
        if (!_mapPanActive || _rawMapPanReceived)
        {
            return;
        }

        var horizontalDelta = (point.X - _mapPanStartScreenPoint.X) / _mapPanStartDpiScaleX;
        var verticalDelta = (point.Y - _mapPanStartScreenPoint.Y) / _mapPanStartDpiScaleY;
        ApplyMapPanDelta(horizontalDelta, verticalDelta);
    }

    private void MoveMapPan(RawMouseDelta delta)
    {
        if (!_mapPanActive)
        {
            return;
        }

        _rawMapPanReceived = true;
        _mapPanRawDeltaX += delta.X / _mapPanStartDpiScaleX;
        _mapPanRawDeltaY += delta.Y / _mapPanStartDpiScaleY;
        ApplyMapPanDelta(_mapPanRawDeltaX, _mapPanRawDeltaY);
    }

    private void ApplyMapPanDelta(double horizontalDelta, double verticalDelta)
    {
        var requestedFocus = MapPanRules.ApplyDragToFocus(
            _mapPanStartFocus,
            horizontalDelta,
            verticalDelta,
            _mapPanStartImageWidth,
            _mapPanStartImageHeight);
        _freeMapFocus = MapPanRules.ClampFocus(
            requestedFocus,
            MapViewport.ActualWidth,
            MapViewport.ActualHeight,
            MapImage.Width,
            MapImage.Height);
        PositionMap();
    }

    private void EndMapPan(GlobalMousePoint point)
    {
        if (!_rawMapPanReceived)
        {
            MoveMapPan(point);
        }
        _mapPanActive = false;
        MapPanel.Cursor = _clickThrough ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void FollowPlayerMap()
    {
        _mapPanActive = false;
        _mapFocusMode = MapFocusMode.FollowPlayer;
        _freeMapFocus = null;
        UpdateMapFocusIndicator();
        PositionMap();
    }

    private void UpdateMapFocusIndicator()
    {
        if (MapFocusModeButton is null)
        {
            return;
        }

        var freeLook = _mapFocusMode == MapFocusMode.FreeLook;
        MapFocusModeButton.Content = freeLook ? "FREE · ALT+RMB" : "FOLLOW · GPS";
        MapFocusModeButton.Foreground = freeLook ? BrushFrom("#F4CB69") : BrushFrom("#72E4D8");
        MapFocusModeButton.BorderBrush = freeLook ? BrushFrom("#A8E7B74E") : BrushFrom("#8A37D4C6");
        MapFocusModeButton.ToolTip = freeLook
            ? "GPS vẫn cập nhật nhưng map đang đứng yên. ALT + chuột phải để bám GPS lại."
            : "Map tự bám theo GPS. ALT + kéo chuột trái để quan sát tự do.";
    }

    private void UpdateMapScreenBounds()
    {
        if (!IsLoaded
            || !_hudVisible
            || WindowState == WindowState.Minimized
            || MapPanel.Visibility != Visibility.Visible
            || MapImage.Source is null)
        {
            _mapScreenBounds = null;
            return;
        }

        try
        {
            var topLeft = MapViewport.PointToScreen(new Point(0d, 0d));
            var bottomRight = MapViewport.PointToScreen(
                new Point(MapViewport.ActualWidth, MapViewport.ActualHeight));
            var bounds = new MapScreenBounds(
                Math.Min(topLeft.X, bottomRight.X),
                Math.Min(topLeft.Y, bottomRight.Y),
                Math.Max(topLeft.X, bottomRight.X),
                Math.Max(topLeft.Y, bottomRight.Y));
            _mapScreenBounds = bounds.Width > 1d && bounds.Height > 1d ? bounds : null;
        }
        catch (InvalidOperationException)
        {
            _mapScreenBounds = null;
        }
    }

    private void SetConnectionState(string text, Brush brush)
    {
        ConnectionLabel.Text = text;
        ConnectionDot.Fill = brush;
    }

    private static double NormalizePercent(double? value)
    {
        if (value is null) return 0d;
        return Math.Clamp(value.Value is >= 0d and <= 1d ? value.Value * 100d : value.Value, 0d, 100d);
    }

    private static string FriendlySpecies(string? className)
    {
        if (string.IsNullOrWhiteSpace(className)) return "ACTIVE DINOSAUR";
        var value = className.Replace("BP_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_C", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Character", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('_', ' ');
        return value.Replace('_', ' ').ToUpperInvariant();
    }

    private static string FormatNumber(double value) => Math.Abs(value) >= 100d ? value.ToString("0") : value.ToString("0.#");

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e) => PositionMap();

    private void ZoomInMap()
    {
        _mapZoom = MapZoomRules.ZoomIn(_mapZoom);
        PositionMap();
    }

    private void ZoomOutMap()
    {
        _mapZoom = MapZoomRules.ZoomOut(_mapZoom);
        PositionMap();
    }

    private void ToggleMap()
    {
        MapPanel.Visibility = MapPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (MapPanel.Visibility == Visibility.Visible)
        {
            Dispatcher.BeginInvoke(PositionMap, DispatcherPriority.Loaded);
        }
        else
        {
            _mapScreenBounds = null;
        }
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomInMap();

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOutMap();

    private void MapFocusModeButton_Click(object sender, RoutedEventArgs e) => FollowPlayerMap();

    private FrameworkElement[] WidgetPanels =>
        [MapPanel, StatsPanel, TeamPanel, MissionPanel, LayoutControls];

    private void WidgetPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough
            || e.ButtonState != MouseButtonState.Pressed
            || sender is not FrameworkElement widget
            || (ReferenceEquals(widget, MapPanel) && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            || FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null
            || FindAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        _draggedWidget = widget;
        _widgetDragStart = e.GetPosition(WidgetCanvas);
        _widgetOrigin = new Point(
            FiniteCanvasCoordinate(Canvas.GetLeft(widget)),
            FiniteCanvasCoordinate(Canvas.GetTop(widget)));
        Panel.SetZIndex(widget, 80);
        widget.CaptureMouse();
        e.Handled = true;
    }

    private void WidgetPanel_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedWidget is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(WidgetCanvas);
        MoveWidget(
            _draggedWidget,
            _widgetOrigin.X + current.X - _widgetDragStart.X,
            _widgetOrigin.Y + current.Y - _widgetDragStart.Y,
            snap: true);
        e.Handled = true;
    }

    private void WidgetPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedWidget is null)
        {
            return;
        }

        var movedMap = ReferenceEquals(_draggedWidget, MapPanel);
        _draggedWidget.ReleaseMouseCapture();
        Panel.SetZIndex(_draggedWidget, 0);
        _draggedWidget = null;
        SaveOverlayLayout();
        if (movedMap)
        {
            PositionMap();
        }
        e.Handled = true;
    }

    private void ScaleDownButton_Click(object sender, RoutedEventArgs e) =>
        ApplyOverlayScale(_overlayScale - OverlayLayoutRules.ButtonStep, persist: true);

    private void ScaleResetButton_Click(object sender, RoutedEventArgs e) =>
        ApplyOverlayScale(OverlayLayoutRules.DefaultScale, persist: true);

    private void ScaleUpButton_Click(object sender, RoutedEventArgs e) =>
        ApplyOverlayScale(_overlayScale + OverlayLayoutRules.ButtonStep, persist: true);

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        _resizeStartingScale = _overlayScale;
        _resizeStartingScreenPoint = PointToScreen(Mouse.GetPosition(this));
        _resizingOverlay = true;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_resizingOverlay)
        {
            return;
        }

        var current = PointToScreen(Mouse.GetPosition(this));
        var dpiScale = Math.Max(0.01d, VisualTreeHelper.GetDpi(this).DpiScaleX);
        var deltaDip = (current.X - _resizeStartingScreenPoint.X) / dpiScale;
        ApplyOverlayScale(
            OverlayLayoutRules.ScaleFromHorizontalDrag(_resizeStartingScale, deltaDip),
            persist: false);
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizingOverlay = false;
        SaveOverlayLayout();
    }

    private void FinishOverlayResize()
    {
        if (!_resizingOverlay)
        {
            return;
        }

        _resizingOverlay = false;
        if (ResizeGrip.IsDragging)
        {
            ResizeGrip.CancelDrag();
        }
        SaveOverlayLayout();
    }

    private void ApplyOverlayScale(double scale, bool persist)
    {
        _overlayScale = OverlayLayoutRules.NormalizeScale(scale);
        foreach (var widget in WidgetPanels)
        {
            widget.LayoutTransform = new ScaleTransform(_overlayScale, _overlayScale);
        }
        MissionToast.LayoutTransform = new ScaleTransform(_overlayScale, _overlayScale);
        OverlayScaleLabel.Text = OverlayLayoutRules.FormatScale(_overlayScale);
        ScaleDownButton.IsEnabled = _overlayScale > OverlayLayoutRules.MinimumScale;
        ScaleResetButton.IsEnabled = Math.Abs(_overlayScale - OverlayLayoutRules.DefaultScale) > 0.001d;
        ScaleUpButton.IsEnabled = _overlayScale < OverlayLayoutRules.MaximumScale;
        OverlayScaleRoot.InvalidateMeasure();
        InvalidateMeasure();

        if (IsLoaded)
        {
            UpdateLayout();
            KeepWidgetsVisible();
            PositionMap();
        }

        if (persist)
        {
            SaveOverlayLayout();
        }
    }

    private void ConfigureWorkspaceBounds()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
        WidgetCanvas.Width = workArea.Width;
        WidgetCanvas.Height = workArea.Height;
    }

    private void RestoreWidgetLayout()
    {
        var workArea = SystemParameters.WorkArea;
        var baseLeft = _layoutSettings.Left is { } legacyLeft
            ? legacyLeft - workArea.Left
            : Math.Max(12d, WidgetCanvas.Width - 304d * _overlayScale - 24d);
        var baseTop = _layoutSettings.Top is { } legacyTop
            ? legacyTop - workArea.Top
            : 70d;
        var controlsLeft = Math.Max(12d, baseLeft - 316d * _overlayScale);
        var defaults = new Dictionary<string, OverlayWidgetPosition>(StringComparer.OrdinalIgnoreCase)
        {
            [OverlayLayoutRules.MapWidget] = new() { Left = baseLeft, Top = baseTop },
            [OverlayLayoutRules.StatsWidget] = new() { Left = baseLeft, Top = baseTop + 312d * _overlayScale },
            [OverlayLayoutRules.TeamWidget] = new() { Left = baseLeft, Top = baseTop + 490d * _overlayScale },
            [OverlayLayoutRules.PrimeWidget] = new() { Left = baseLeft, Top = baseTop + 610d * _overlayScale },
            [OverlayLayoutRules.ControlsWidget] = new() { Left = controlsLeft, Top = baseTop }
        };

        RestoreWidget(MapPanel, OverlayLayoutRules.MapWidget, defaults);
        RestoreWidget(StatsPanel, OverlayLayoutRules.StatsWidget, defaults);
        RestoreWidget(TeamPanel, OverlayLayoutRules.TeamWidget, defaults);
        RestoreWidget(MissionPanel, OverlayLayoutRules.PrimeWidget, defaults);
        RestoreWidget(LayoutControls, OverlayLayoutRules.ControlsWidget, defaults);
        KeepWidgetsVisible();
    }

    private void RestoreWidget(
        FrameworkElement widget,
        string id,
        IReadOnlyDictionary<string, OverlayWidgetPosition> defaults)
    {
        var position = _layoutSettings.Widgets.TryGetValue(id, out var saved)
            ? saved
            : defaults[id];
        MoveWidget(widget, position.Left, position.Top, snap: false);
    }

    private void MoveWidget(FrameworkElement widget, double left, double top, bool snap)
    {
        const double edgeMargin = 10d;
        const double snapDistance = 10d;
        var width = ScaledWidth(widget);
        var height = ScaledHeight(widget);
        var maxLeft = Math.Max(edgeMargin, WidgetCanvas.Width - width - edgeMargin);
        var maxTop = Math.Max(edgeMargin, WidgetCanvas.Height - height - edgeMargin);
        left = Math.Clamp(double.IsFinite(left) ? left : edgeMargin, edgeMargin, maxLeft);
        top = Math.Clamp(double.IsFinite(top) ? top : edgeMargin, edgeMargin, maxTop);

        if (snap)
        {
            left = Snap(left, [edgeMargin, maxLeft], snapDistance);
            top = Snap(top, [edgeMargin, maxTop], snapDistance);
            foreach (var other in WidgetPanels.Where(candidate => candidate != widget && candidate.Visibility == Visibility.Visible))
            {
                var otherLeft = FiniteCanvasCoordinate(Canvas.GetLeft(other));
                var otherTop = FiniteCanvasCoordinate(Canvas.GetTop(other));
                var otherWidth = ScaledWidth(other);
                var otherHeight = ScaledHeight(other);
                left = Snap(left, [otherLeft, otherLeft + otherWidth, otherLeft - width, otherLeft + otherWidth - width], snapDistance);
                top = Snap(top, [otherTop, otherTop + otherHeight, otherTop - height, otherTop + otherHeight - height], snapDistance);
            }
        }

        Canvas.SetLeft(widget, Math.Clamp(left, edgeMargin, maxLeft));
        Canvas.SetTop(widget, Math.Clamp(top, edgeMargin, maxTop));
    }

    private static double Snap(double value, IEnumerable<double> candidates, double distance)
    {
        var closest = candidates.OrderBy(candidate => Math.Abs(candidate - value)).FirstOrDefault(value);
        return Math.Abs(closest - value) <= distance ? closest : value;
    }

    private double ScaledWidth(FrameworkElement widget) =>
        Math.Max(1d, (double.IsNaN(widget.Width) ? widget.ActualWidth : widget.Width) * _overlayScale);

    private double ScaledHeight(FrameworkElement widget) =>
        Math.Max(1d, (double.IsNaN(widget.Height) ? widget.ActualHeight : widget.Height) * _overlayScale);

    private static double FiniteCanvasCoordinate(double value) => double.IsFinite(value) ? value : 0d;

    private void KeepWidgetsVisible()
    {
        foreach (var widget in WidgetPanels)
        {
            MoveWidget(
                widget,
                FiniteCanvasCoordinate(Canvas.GetLeft(widget)),
                FiniteCanvasCoordinate(Canvas.GetTop(widget)),
                snap: false);
        }
    }

    private void KeepOverlayVisible() => KeepWidgetsVisible();

    private void SaveOverlayLayout()
    {
        KeepWidgetsVisible();
        _layoutSettings = OverlayLayoutRules.Normalize(_layoutSettings with
        {
            Scale = _overlayScale,
            Left = null,
            Top = null,
            Widgets = new Dictionary<string, OverlayWidgetPosition>(StringComparer.OrdinalIgnoreCase)
            {
                [OverlayLayoutRules.MapWidget] = PositionOf(MapPanel),
                [OverlayLayoutRules.StatsWidget] = PositionOf(StatsPanel),
                [OverlayLayoutRules.TeamWidget] = PositionOf(TeamPanel),
                [OverlayLayoutRules.PrimeWidget] = PositionOf(MissionPanel),
                [OverlayLayoutRules.ControlsWidget] = PositionOf(LayoutControls)
            }
        });
        _layoutSettingsStore.Save(_layoutSettings);
    }

    private static OverlayWidgetPosition PositionOf(FrameworkElement widget) => new()
    {
        Left = FiniteCanvasCoordinate(Canvas.GetLeft(widget)),
        Top = FiniteCanvasCoordinate(Canvas.GetTop(widget))
    };

    private void ResetWidgetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _layoutSettings = _layoutSettings with
        {
            Left = null,
            Top = null,
            Widgets = new Dictionary<string, OverlayWidgetPosition>(StringComparer.OrdinalIgnoreCase)
        };
        RestoreWidgetLayout();
        SaveOverlayLayout();
    }

    private void LockButton_Click(object sender, RoutedEventArgs e) => SetClickThrough(true);

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        var home = new HomeWindow();
        Application.Current.MainWindow = home;
        home.Show();
        Close();
    }

    private void SetClickThrough(bool enabled)
    {
        if (enabled)
        {
            FinishOverlayResize();
            if (_draggedWidget is not null)
            {
                _draggedWidget.ReleaseMouseCapture();
                _draggedWidget = null;
                SaveOverlayLayout();
            }
        }

        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        style = enabled ? style | WsExTransparent | WsExNoActivate : style & ~(WsExTransparent | WsExNoActivate);
        SetWindowLong(handle, GwlExStyle, style);
        _clickThrough = enabled;
        EditToolbar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        StatsMoveBadge.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        TeamMoveBadge.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        MissionMoveBadge.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        MapZoomControls.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        MapLayerControls.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        LayoutControls.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        foreach (var widget in WidgetPanels.Where(widget => widget != LayoutControls))
        {
            widget.Cursor = enabled ? Cursors.Arrow : Cursors.SizeAll;
        }
        LockButton.Content = enabled ? "LOCKED" : "LOCK";
        RefreshOptionalWidgetVisibility();
        if (!enabled)
        {
            Activate();
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                RefreshWindowSizeToContent();
                KeepOverlayVisible();
                PositionMap();
            },
            DispatcherPriority.Loaded);
    }

    private void RefreshWindowSizeToContent()
    {
        OverlayScaleRoot.InvalidateMeasure();
        InvalidateMeasure();
        UpdateLayout();
        KeepWidgetsVisible();
    }

    private void RefreshOptionalWidgetVisibility()
    {
        TeamPanel.Visibility = !_clickThrough || _pendingTeamState.HasActiveSession
            ? Visibility.Visible
            : Visibility.Collapsed;
        MissionPanel.Visibility = !_clickThrough || (_hasMissions && _missionsVisible)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmInput && _mapPanActive && RawMouseInput.TryReadDelta(lParam, out var delta))
        {
            MoveMapPan(delta);
        }
        else if (message == WmHotkey && wParam.ToInt32() == EditHotkeyId)
        {
            SetClickThrough(!_clickThrough);
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == ToggleMissionsNHotkeyId)
        {
            ToggleMissions();
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == ToggleHudHotkeyId)
        {
            ToggleHud();
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == MapNotesHotkeyId)
        {
            if (HasCurrentProFeatures)
            {
                ToggleMapNotesWindow();
            }
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Window_StateChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(PositionMap, DispatcherPriority.Loaded);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closed(object? sender, EventArgs e)
    {
        SaveOverlayLayout();
        DetachMapNotes();
        _proFeatureExpiryTimer?.Stop();
        _proFeatureExpiryTimer = null;
        DetachTeamOverlay();
        App.CurrentTeam.ClearTelemetry();
        _shutdown.Cancel();
        if (_telemetryWatchTask is not null)
        {
            await _telemetryWatchTask;
        }

        if (_telemetrySession is not null)
        {
            await _telemetrySession.DisposeAsync();
        }
        if (!_remotePlayerSourceOwnedBySession && _remotePlayerSource is not null)
        {
            await _remotePlayerSource.DisposeAsync();
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (_editHotkeyRegistered) UnregisterHotKey(handle, EditHotkeyId);
        if (_toggleMissionsNHotkeyRegistered) UnregisterHotKey(handle, ToggleMissionsNHotkeyId);
        if (_toggleHudHotkeyRegistered) UnregisterHotKey(handle, ToggleHudHotkeyId);
        if (_mapNotesHotkeyRegistered) UnregisterHotKey(handle, MapNotesHotkeyId);
        if (_mouseShortcuts is not null)
        {
            _mouseShortcuts.CanStartMapPan = null;
            _mouseShortcuts.ZoomInRequested -= ZoomInMap;
            _mouseShortcuts.ZoomOutRequested -= ZoomOutMap;
            _mouseShortcuts.ToggleMapRequested -= ToggleMap;
            _mouseShortcuts.MapPanStarted -= StartMapPan;
            _mouseShortcuts.MapPanMoved -= MoveMapPan;
            _mouseShortcuts.MapPanEnded -= EndMapPan;
            _mouseShortcuts.FollowMapRequested -= FollowPlayerMap;
            _mouseShortcuts.Dispose();
        }
        _windowSource?.RemoveHook(WindowMessageHook);
        _httpClient.Dispose();
        _shutdown.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);

    private sealed record MapScreenBounds(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;

        public bool Contains(GlobalMousePoint point) =>
            point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    }
}
