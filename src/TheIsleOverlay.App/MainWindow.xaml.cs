using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class MainWindow : Window
{
    private static readonly Uri GatewayMapResourceUri = new("Assets/GatewayMap.webp", UriKind.Relative);
    private static readonly TimeSpan LiveHeadingAnimationDuration = TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan MovementHeadingAnimationDuration = TimeSpan.FromMilliseconds(220);

    private const int EditHotkeyId = 0x714;
    private const int HideGuideHotkeyId = 0x715;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint KeyO = 0x4F;
    private const uint KeyCloseBracket = 0xDD;
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
    private ITelemetrySession? _telemetrySession;
    private Task? _telemetryWatchTask;
    private string _configuredSource = "ERA";
    private WorldLocation? _location;
    private MapPoint? _mapLocation;
    private WorldLocation? _previousLocation;
    private GlobalMouseShortcutHook? _mouseShortcuts;
    private HwndSource? _windowSource;
    private double _mapZoom = 2.25d;
    private double _headingDegrees;
    private bool _clickThrough;
    private bool _hasMovementHeading;
    private bool _editHotkeyRegistered;
    private bool _hideGuideHotkeyRegistered;

    public MainWindow() : this(null, null, null, null)
    {
    }

    public MainWindow(TelemetrySourceDefinition? source, string? cookieValue)
        : this(source, cookieValue, null, null)
    {
    }

    public MainWindow(ITelemetrySession telemetrySession, string displayName)
        : this(
            null,
            null,
            telemetrySession ?? throw new ArgumentNullException(nameof(telemetrySession)),
            displayName)
    {
    }

    private MainWindow(
        TelemetrySourceDefinition? source,
        string? cookieValue,
        ITelemetrySession? telemetrySession,
        string? displayName)
    {
        _requestedSource = source;
        _providedCookie = cookieValue;
        _providedSession = telemetrySession;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            _configuredSource = displayName;
        }

        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left, area.Right - ActualWidth - 24);
        Top = area.Top + 70;
        PlayerMarker.Visibility = Visibility.Collapsed;
        DirectionNeedle.Opacity = 0.45d;
        InitializeTeamOverlay();

        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
        _editHotkeyRegistered = RegisterHotKey(handle, EditHotkeyId, ModControl | ModShift, KeyO);
        _hideGuideHotkeyRegistered = RegisterHotKey(handle, HideGuideHotkeyId, ModAlt, KeyCloseBracket);
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

        LoadMap();
        _telemetryWatchTask = WatchTelemetryAsync();
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
        _telemetrySession = new PollingTelemetrySession(provider, source: source.ShortName);
        _configuredSource = source.ShortName;
    }

    private void InstallMouseShortcuts()
    {
        _mouseShortcuts = new GlobalMouseShortcutHook(Dispatcher);
        _mouseShortcuts.ZoomInRequested += ZoomInMap;
        _mouseShortcuts.ZoomOutRequested += ZoomOutMap;
        _mouseShortcuts.ToggleMapRequested += ToggleMap;
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
                await Dispatcher.InvokeAsync(() => RenderSnapshot(snapshot));
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

    private void RenderSnapshot(TelemetrySnapshot snapshot)
    {
        try
        {
            if (snapshot.SessionState == TelemetrySessionState.AuthenticationRequired)
            {
                ShowTelemetryUnavailable("PHIÊN ĐÃ HẾT HẠN", "Đăng nhập Steam lại để tiếp tục");
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
            SetConnectionState(ConnectionText(snapshot.SessionState), degraded ? WaitingBrush : OnlineBrush);
            SpeciesLabel.Text = FriendlySpecies(player.Class);
            PlayerNameLabel.Text = string.IsNullOrWhiteSpace(player.Name) ? "ACTIVE PLAYER" : player.Name;

            var growth = exact?.Growth ?? player.GrowthPercent;
            GrowthLabel.Text = $"{NormalizePercent(growth):0.#}%";

            RenderVital(HealthBar, HealthValue, exact?.Health, exact?.MaxHealth, player.HealthPercent);
            RenderVital(StaminaBar, StaminaValue, exact?.Stamina, exact?.MaxStamina, player.StaminaPercent);

            RenderVital(HungerBar, HungerValue, exact?.Hunger, exact?.MaxHunger, player.HungerPercent);
            RenderVital(WaterBar, WaterValue, exact?.Thirst, exact?.MaxThirst, player.ThirstPercent);

            UpdatedLabel.Text = $"SYNC {(snapshot.UpdatedAt ?? DateTimeOffset.Now).ToLocalTime():HH:mm:ss}";

            UpdateHeading(player);
            _location = player.Location;
            _mapLocation = player.MapLocation;
            if (_location is not null)
            {
                var altitude = _location.Z is null ? "—" : $"{_location.Z.Value / 1000d:0.0}";
                CoordinateLabel.Text = $"X {_location.X / 1000d:0.0}  Y {_location.Y / 1000d:0.0}  Z {altitude}";
                PlayerMarker.Visibility = Visibility.Visible;
                PositionMap();
            }
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
        PlayerMarker.Visibility = Visibility.Collapsed;
        HeadingModeLabel.Text = "COURSE · WAITING";
        ClearVitals();
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
        PlayerMarker.Visibility = Visibility.Collapsed;
        HeadingModeLabel.Text = "COURSE · WAITING";
        ClearVitals();
    }

    private string ConnectionText(TelemetrySessionState state) => state switch
    {
        TelemetrySessionState.Live => $"{_configuredSource} · LIVE",
        TelemetrySessionState.Reconnecting => $"{_configuredSource} · RECONNECTING",
        TelemetrySessionState.Stale => $"{_configuredSource} · DATA STALE",
        TelemetrySessionState.Polling => $"{_configuredSource} · POLL 2S",
        _ => $"{_configuredSource} · {state.ToString().ToUpperInvariant()}"
    };

    private void SetTelemetryOpacity(double opacity)
    {
        PlayerMarker.Opacity = opacity;
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
            return;
        }

        MapShade.Width = viewportWidth;
        MapShade.Height = viewportHeight;

        if (MapImage.Source is not BitmapSource source || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return;
        }

        var coverScale = Math.Max(viewportWidth / source.PixelWidth, viewportHeight / source.PixelHeight);
        var imageWidth = source.PixelWidth * coverScale * _mapZoom;
        var imageHeight = source.PixelHeight * coverScale * _mapZoom;
        MapImage.Width = imageWidth;
        MapImage.Height = imageHeight;

        var point = _mapLocation ??
            (_location is null ? new MapPoint(0.5d, 0.5d) : GatewayMapProjection.Project(_location));
        var desiredLeft = viewportWidth / 2d - point.Left * imageWidth;
        var desiredTop = viewportHeight / 2d - point.Top * imageHeight;
        var left = ClampImageOffset(desiredLeft, viewportWidth, imageWidth);
        var top = ClampImageOffset(desiredTop, viewportHeight, imageHeight);
        Canvas.SetLeft(MapImage, left);
        Canvas.SetTop(MapImage, top);

        Canvas.SetLeft(PlayerMarker, left + point.Left * imageWidth - PlayerMarker.Width / 2d);
        Canvas.SetTop(PlayerMarker, top + point.Top * imageHeight - PlayerMarker.Height / 2d);
        PositionTeamMarkers(left, top, imageWidth, imageHeight);
    }

    private static double ClampImageOffset(double desired, double viewportSize, double imageSize) =>
        imageSize <= viewportSize ? (viewportSize - imageSize) / 2d : Math.Clamp(desired, viewportSize - imageSize, 0d);

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

    private void MapViewport_SizeChanged(object sender, SizeChangedEventArgs e) => PositionMap();

    private void ZoomInMap()
    {
        _mapZoom = Math.Min(6d, _mapZoom + 0.35d);
        PositionMap();
    }

    private void ZoomOutMap()
    {
        _mapZoom = Math.Max(1d, _mapZoom - 0.35d);
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
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomInMap();

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomOutMap();

    private void DragRegion_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.Button)
        {
            DragMove();
        }
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
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        style = enabled ? style | WsExTransparent | WsExNoActivate : style & ~(WsExTransparent | WsExNoActivate);
        SetWindowLong(handle, GwlExStyle, style);
        _clickThrough = enabled;
        EditToolbar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        MapZoomControls.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        LockButton.Content = enabled ? "LOCKED" : "LOCK";
        if (!enabled)
        {
            Activate();
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == EditHotkeyId)
        {
            SetClickThrough(!_clickThrough);
            handled = true;
        }
        else if (message == WmHotkey && wParam.ToInt32() == HideGuideHotkeyId)
        {
            HotkeyGuide.Visibility = Visibility.Collapsed;
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void Window_Closed(object? sender, EventArgs e)
    {
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

        var handle = new WindowInteropHelper(this).Handle;
        if (_editHotkeyRegistered) UnregisterHotKey(handle, EditHotkeyId);
        if (_hideGuideHotkeyRegistered) UnregisterHotKey(handle, HideGuideHotkeyId);
        if (_mouseShortcuts is not null)
        {
            _mouseShortcuts.ZoomInRequested -= ZoomInMap;
            _mouseShortcuts.ZoomOutRequested -= ZoomOutMap;
            _mouseShortcuts.ToggleMapRequested -= ToggleMap;
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
}
