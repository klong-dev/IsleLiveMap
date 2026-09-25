using System.Reflection;
using System.Net.Http;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Localization;
using TheIsleOverlay.LocalTelemetry;
using TheIsleOverlay.ProClient;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public partial class HomeWindow : Window
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly LatestTelemetrySnapshotStore _snapshots = LatestTelemetrySnapshotStore.Shared;
    private readonly GitHubReleaseNotesService _releaseService = new();
    private readonly GitHubUpdateService _updateService = new();
    private readonly ProAccessService _proService = new();
    private readonly ProTelemetryWarmup _proTelemetryWarmup;
    private readonly Dictionary<OverlayShortcutAction, TextBox> _shortcutFields = new();
    private string _page = "home";
    private MapLayerPreferences _layers = new();
    private readonly MapLayerPreferencesStore _layerStore = new();
    private readonly OverlayLayoutSettingsStore _layoutStore = new();
    private readonly ShortcutSettingsStore _shortcutStore = new();
    private ProAccessSnapshot _pro = ProAccessSnapshot.SignedOut;
    private HomeProPresentationState _proPresentation;
    private Task? _proLoadTask;
    private Task? _updateTask;
    private int _mapOpenStarted;
    private MapLaunchGateState _mapLaunchGateState = MapLaunchGateState.Checking;
    private Button? _mapActionButton;
    private TextBlock? _updateStatus;
    private bool _updateReadyDialogShown;
    private TextBlock? _status;
    private static Brush B(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    private static TextBlock T(string text, double size = 14, Brush? foreground = null, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = foreground ?? B("#EAF4F0"), FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap };
    public HomeWindow()
    {
        InitializeComponent();
        VersionLabel.Text = $"  v{CurrentVersion()}";
        SizeChanged += (_, _) => UpdateReleaseRailLayout();
        _proTelemetryWarmup = new(() => _proService.CreateRemotePlayerSource());
        _snapshots.Changed += SnapshotChanged;
        App.CurrentTeam.StateChanged += HomeTeamStateChanged;
    }
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowVisible();
        VersionLabel.Text = $"  v{CurrentVersion()}";
        ApplyProPresentation(_pro, rebuildCurrentPage: false);
        BuildHome();
        UpdateNavigationVisuals();
        _ = LoadReleasesAsync();
        _ = LoadProAsync();
        _ = PrepareUpdateAtStartupAsync();
        WarmupLocalTelemetryIfReady();
    }
    private void EnsureWindowVisible()
    {
        var area = SystemParameters.WorkArea;
        if (double.IsNaN(Left) || double.IsInfinity(Left) || Left < area.Left - Width + 80 || Left > area.Right - 80) Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        if (double.IsNaN(Top) || double.IsInfinity(Top) || Top < area.Top - Height + 80 || Top > area.Bottom - 80) Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }
    private void Window_Closed(object? sender, EventArgs e)
    {
        _snapshots.Changed -= SnapshotChanged;
        App.CurrentTeam.StateChanged -= HomeTeamStateChanged;
        // Async click/startup handlers can still be unwinding after Close().
        // Cancel the shared work, but do not dispose the CTS here: a handler
        // that resumes after the window closes must still be able to read its
        // token and observe cancellation. The window owns the CTS for its
        // lifetime and it will be collected with the window.
        _shutdown.Cancel();
        _proTelemetryWarmup.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _proService.Dispose();
    }
    private void HomeTeamStateChanged(object? sender, TeamRelayState state)
    {
        if (_page == "team" && !_teamOperationRunning) Dispatcher.InvokeAsync(() => { if (!_teamOperationRunning && _page == "team") ReplacePage(BuildTeam); });
    }
    private void SnapshotChanged(object? sender, EventArgs e) { if (_page == "info") Dispatcher.Invoke(() => ReplacePage(BuildInfo)); }
    private static string CurrentVersion() => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.2.3";
    private void Nav_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string key }) Select(key); }
    private void Select(string key) { _page = key; var isHome = key == "home"; ReleaseRail.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed; ReleaseRailColumn.Width = isHome ? new GridLength(270) : new GridLength(0); ReplacePage(key switch { "home" => BuildHome, "team" => BuildTeam, "info" => BuildInfo, "locale" => BuildLocale, "shortcuts" => BuildShortcuts, "settings" => BuildSettings, "contact" => BuildContact, "guide" => BuildGuide, "pro" => BuildPro, _ => BuildHome }); UpdateNavigationVisuals(); }
    private void ReplacePage(Action builder) { Workspace.Children.Clear(); _shortcutFields.Clear(); _status = null; builder(); UpdateNavigationVisuals(); }
    private void UpdateNavigationVisuals()
    {
        UpdateReleaseRailLayout();
        foreach (var button in FindVisualButtons(this).Where(button => button.Style == (Style)FindResource("Nav")))
        {
            if (button.Tag is not string key) continue;
            var selected = string.Equals(key, _page, StringComparison.OrdinalIgnoreCase);
            button.Background = selected ? (Brush)FindResource("AccentDark") : Brushes.Transparent;
            button.BorderBrush = selected ? (Brush)FindResource("Accent") : Brushes.Transparent;
            button.Foreground = selected ? (Brush)FindResource("Ink") : (Brush)FindResource("Muted");
            button.ApplyTemplate();
            if (button.Template?.FindName("NavIndicator", button) is Border indicator)
                indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    private static IEnumerable<Button> FindVisualButtons(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindVisualButtons(child)) yield return nested;
        }
    }
    private StackPanel Page(string eyebrow, string title, string detail)
    {
        var p = new StackPanel { MaxWidth = 860 };
        var eyebrowText = T(eyebrow, 11, B(_proPresentation.HasCurrentProAccess ? "#D3A85C" : "#49D5C3"), FontWeights.Bold);
        eyebrowText.Margin = new Thickness(0, 0, 0, 5);
        p.Children.Add(eyebrowText);

        var heading = T(title, 27, null, FontWeights.Black);
        heading.MaxWidth = 620;
        heading.Margin = new Thickness(0, 0, 0, 5);
        p.Children.Add(heading);

        var description = T(detail, 14, B("#91AAA3"));
        description.MaxWidth = 700;
        description.Margin = new Thickness(0, 0, 0, 2);
        p.Children.Add(description);
        p.Children.Add(new Separator { Margin = new Thickness(0, 16, 0, 18), Background = B("#294943") });
        Workspace.Children.Add(p);
        return p;
    }
    private Button Action(string text, RoutedEventHandler click, bool primary = true) { var b = new Button { Content = text, Style = (Style)FindResource(primary ? "Action" : "Ghost"), Margin = new Thickness(0, 0, 10, 10), MinWidth = 132, MaxWidth = 260 }; b.Click += click; return b; }
    private void BuildHome()
    {
        var p = Page(_proPresentation.HeroEyebrow, _proPresentation.HeroTitle, _proPresentation.HeroDescription);
        // A background brush does not participate in measure. Content can grow
        // for wrapped error/status text without inheriting the bitmap size.
        var hero = new Grid
        {
            MaxWidth = 860, MinHeight = 270,
            Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/IsleLiveMap;component/Assets/GatewayMapWater.jpg", UriKind.Absolute)))
            { Stretch = Stretch.UniformToFill, Opacity = .82 }
        };
        hero.Children.Add(new Border { Background = B("#C90A1917") });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 500, Margin = new Thickness(26) };
        copy.Children.Add(T(_proPresentation.HasCurrentProAccess ? "THEO DÕI NGƯỜI CHƠI + AI" : "SỬ DỤNG NGAY", 13, B(_proPresentation.HasCurrentProAccess ? "#E6C477" : "#49D5C3"), FontWeights.Bold));
        // The page header already owns the title and description. Do not render
        // the same "MỞ TRÌNH THEO DÕI" copy again inside the hero; the hero's job
        // is to hold the primary action and the supported-server actions.

        var primary = new StackPanel { Margin = new Thickness(0, _proPresentation.HasCurrentProAccess ? 14 : 18, 0, 0) };
        var mapButton = _proPresentation.HasCurrentProAccess
            ? new Button { Content = _proPresentation.MapAction, Style = (Style)FindResource("PrimaryMapAction"), HorizontalAlignment = HorizontalAlignment.Left, CommandParameter = "basic" }
            : Action(_proPresentation.MapAction, OpenMap_Click);
        if (_proPresentation.HasCurrentProAccess) mapButton.Click += OpenMap_Click;
        AutomationProperties.SetAutomationId(
            mapButton,
            _proPresentation.HasCurrentProAccess ? "OpenMapProButton" : "OpenMapBasicButton");
        AutomationProperties.SetName(
            mapButton,
            _proPresentation.HasCurrentProAccess ? "MỞ MAP PRO" : "MỞ MAP");
        AutomationProperties.SetHelpText(
            mapButton,
            "Mở Live Map sau khi kiểm tra cập nhật và Npcap");
        if (_mapLaunchGateState == MapLaunchGateState.Checking)
            mapButton.Content = _proPresentation.HasCurrentProAccess ? "ĐANG KIỂM TRA CẬP NHẬT" : "ĐANG KIỂM TRA CẬP NHẬT";
        mapButton.IsEnabled = MapLaunchGatePolicy.AllowsMap(_mapLaunchGateState) && _mapOpenStarted == 0;
        _mapActionButton = mapButton;
        primary.Children.Add(mapButton);
        var updateAction = Action("MỞ THÔNG BÁO CẬP NHẬT", (_, _) => ShowUpdateReadyDialog(_updateService.PendingVersion));
        AutomationProperties.SetAutomationId(updateAction, "ReopenUpdateReadyDialog");
        updateAction.Visibility = _mapLaunchGateState == MapLaunchGateState.UpdateRequired ? Visibility.Visible : Visibility.Collapsed;
        _reopenUpdateAction = updateAction;
        primary.Children.Add(updateAction);
        copy.Children.Add(primary);

        var supportedLabel = T("Hoặc các server được hỗ trợ riêng:", 13, B("#A9BAB4"), FontWeights.SemiBold);
        supportedLabel.Margin = new Thickness(0, 10, 0, 0);
        copy.Children.Add(supportedLabel);
        var servers = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        _serverActionButtons.Clear();
        _serverButtonLabels.Clear();
        // Three equal branded actions; wrap only at the smallest viewport.
        servers.Children.Add(ServerButton("Assets/GachaLogo.png", "GACHA", GachaServer_Click, "#415D3E", "#A8D17A"));
        servers.Children.Add(ServerButton("Assets/OriginLogo.png", "ORIGIN 5X", OriginServer_Click, "#344F71", "#9CC8FF"));
        servers.Children.Add(ServerButton("Assets/SDVNIcon.png", "SDVN", SdvnServer_Click, "#303F7D", "#A6B8FF"));
        copy.Children.Add(servers);
        _status = T(_launchStatus, 12, B("#E7D9AB"), FontWeights.SemiBold);
        _status.Margin = new Thickness(0, 4, 0, 0);
        copy.Children.Add(_status);
        hero.Children.Add(copy);
        p.Children.Add(new Border { Child = hero, CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderBrush = B(_proPresentation.HasCurrentProAccess ? "#6B5434" : "#294943"), BorderThickness = new Thickness(1) });
        var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(StatusLine("QUYỀN TRUY CẬP", _proPresentation.StatusLabel));
        row.Children.Add(StatusLine("NPCAP / GPS", NpcapAvailabilityProbe.Check().IsAvailable ? "Sẵn sàng" : "Chưa sẵn sàng"));
        row.Children.Add(StatusLine("PHIÊN", _snapshots.Current is null ? "Chưa có phiên" : "Có dữ liệu gần nhất"));
        p.Children.Add(row);
        _updateStatus = T(_lastUpdateStatus, 12, B(_lastUpdateColor), FontWeights.SemiBold);
        _updateStatus.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(_updateStatus);
        RefreshLaunchButtons();
    }

    private Button ServerButton(string logo, string label, RoutedEventHandler action, string surface, string border)
    {
        var button = new Button { Style = (Style)FindResource("ServerAction"), Background = B(surface), BorderBrush = B(border), ToolTip = $"Mở {label}", Width = 132, Height = 76, Padding = new Thickness(6), Margin = new Thickness(0, 0, 8, 8) };
        AutomationProperties.SetAutomationId(button, "Server" + label.Replace(" ", "") + "Button");
        AutomationProperties.SetName(button, $"Mở server {label}");
        AutomationProperties.SetHelpText(button, $"Mở {label} trong workspace riêng");
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var logoImage = new Image { Source = new BitmapImage(new Uri($"/IsleLiveMap;component/{logo}", UriKind.Relative)), Width = 36, Height = 36, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(logoImage);
        var name = T(label, 13, B("#FFFFFF"), FontWeights.Black);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.VerticalAlignment = VerticalAlignment.Center;
        name.TextAlignment = TextAlignment.Center;
        name.TextWrapping = TextWrapping.NoWrap;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        content.Children.Add(name);
        button.Content = content;
        button.Click += action;
        _serverActionButtons.Add(button);
        _serverButtonLabels[button] = label;
        return button;
    }

    private Border StatusLine(string label, string value) { var s = new StackPanel(); s.Children.Add(T(label, 11, B("#68817A"), FontWeights.Bold)); s.Children.Add(T(value, 15, null, FontWeights.SemiBold)); return new Border { Child = s, BorderBrush = B("#294943"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Margin = new Thickness(0, 0, 18, 0) }; }
    private void BuildTeam()
    {
        var p = Page("KẾT NỐI ĐỒNG ĐỘI", "NHÓM SINH TỒN", "Tạo hoặc nhập phòng để chia sẻ vị trí và trạng thái trong cùng phiên chơi.");
        _status = T(_teamStatus, 13, B("#E7D9AB"), FontWeights.SemiBold);
        _status.Margin = new Thickness(0, 0, 0, 12);
        p.Children.Add(_status);
        var state = App.CurrentTeam.CurrentState;
        var tier = _pro.Entitlement.IsProAt(DateTimeOffset.UtcNow) ? TeamAccessTier.Pro : TeamAccessTier.Free;
        var limit = TeamRoomLimits.For(tier);
        var intro = Section("GIỚI HẠN PHÒNG", tier == TeamAccessTier.Pro ? "Quyền Pro đang mở phòng tối đa 21 người, tính cả chủ phòng." : "Tài khoản miễn phí tạo phòng tối đa 7 người, tính cả chủ phòng.");
        ((StackPanel)intro.Child).Children.Add(T($"CHẾ ĐỘ HIỆN TẠI · {(tier == TeamAccessTier.Pro ? "PRO" : "MIỄN PHÍ")}  ·  TỐI ĐA {limit} NGƯỜI", 13, B(tier == TeamAccessTier.Pro ? "#E6C477" : "#49D5C3"), FontWeights.Bold));
        p.Children.Add(intro);

        if (state.HasActiveSession && state.Session is { } session)
        {
            var active = Section("PHÒNG ĐANG HOẠT ĐỘNG", "Mã mời có thể gửi cho đồng đội.");
            var stack = (StackPanel)active.Child;
            var codeRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 6) };
            codeRow.Children.Add(T(string.Join(" ", session.InviteCode.ToCharArray()), 25, B("#EAF4F0"), FontWeights.Black));
            codeRow.Children.Add(Action("SAO CHÉP MÃ", (_, _) => { Clipboard.SetText(session.InviteCode); SetStatus($"Đã copy mã {session.InviteCode}."); }, false));
            stack.Children.Add(codeRow);
            stack.Children.Add(T($"{state.Members.Count} / {session.MaxMembers} NGƯỜI  ·  {TeamStateText(state.ConnectionState)}", 13, B("#91AAA3"), FontWeights.SemiBold));
            stack.Children.Add(Action("RỜI PHÒNG", async (_, _) => { await App.CurrentTeam.LeaveAsync(_shutdown.Token); ReplacePage(BuildTeam); }, false));
            p.Children.Add(active);
            return;
        }

        var form = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var create = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        create.Children.Add(T("TẠO PHÒNG", 11, B("#49D5C3"), FontWeights.Bold));
        var createButton = Action("TẠO PHÒNG", CreateSizedTeam_Click);
        createButton.IsEnabled = !_teamOperationRunning;
        create.Children.Add(createButton);
        Grid.SetColumn(create, 0); form.Children.Add(create);

        var join = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        join.Children.Add(T("CODE PHÒNG", 11, B("#49D5C3"), FontWeights.Bold));
        var invite = new TextBox { Style = (Style)FindResource("Field"), ToolTip = "Nhập mã mời 6 ký tự", Margin = new Thickness(0, 10, 0, 10), MaxLength = 6 };
        join.Children.Add(invite);
        var joinButton = Action("VÀO PHÒNG", async (_, _) =>
        {
            if (_teamOperationRunning) return;
            var displayName = PromptTeamName("VÀO PHÒNG");
            if (displayName is null) return;
            try { await App.CurrentTeam.JoinAsync(invite.Text.Trim(), displayName, tier, _shutdown.Token); ReplacePage(BuildTeam); }
            catch (Exception ex) { SetStatus(FriendlyTeamErrorText(ex)); }
        });
        joinButton.IsEnabled = !_teamOperationRunning;
        join.Children.Add(joinButton);
        Grid.SetColumn(join, 1); form.Children.Add(join);
        p.Children.Add(form);
        if (state.ConnectionState is TeamRelayConnectionState.Error or TeamRelayConnectionState.Expired)
            p.Children.Add(T(state.Message ?? "Phiên nhóm đã kết thúc. Hãy tạo hoặc nhập mã lại.", 13, B("#E77A68"), FontWeights.SemiBold));
    }

    private string? PromptTeamName(string action)
    {
        var dialog = new Window
        {
            Owner = this,
            Title = action,
            Width = 360,
            Height = 220,
            MinWidth = 360,
            MinHeight = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false
        };
        var shell = new Border { Background = B("#F20D1B1A"), BorderBrush = B("#49665F"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(22) };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(T(action, 17, B("#EAF4F0"), FontWeights.Bold));
        var label = T("Tên của bạn:", 12, B("#91AAA3"), FontWeights.SemiBold);
        label.Margin = new Thickness(0, 14, 0, 5); Grid.SetRow(label, 1); layout.Children.Add(label);
        var input = new TextBox { Style = (Style)FindResource("Field"), MinHeight = 38, MaxLength = 32, Text = "" };
        input.Margin = new Thickness(0, 0, 0, 14); Grid.SetRow(input, 2); layout.Children.Add(input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Action("HỦY", (_, _) => dialog.DialogResult = false, false); cancel.MinWidth = 82;
        var confirm = Action("XÁC NHẬN", (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) dialog.DialogResult = true; }, true); confirm.MinWidth = 100;
        actions.Children.Add(cancel); actions.Children.Add(confirm); Grid.SetRow(actions, 3); layout.Children.Add(actions);
        shell.Child = layout; dialog.Content = shell; dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
    private static string TeamStateText(TeamRelayConnectionState state) => state switch
    {
        TeamRelayConnectionState.Live => "RELAY TRỰC TUYẾN",
        TeamRelayConnectionState.Reconnecting => "ĐANG NỐI LẠI",
        TeamRelayConnectionState.Connecting => "ĐANG KẾT NỐI",
        _ => "CHƯA KẾT NỐI"
    };
    private static string FriendlyTeamErrorText(Exception exception) => exception switch
    {
        TeamRelayApiException { Code: "team_full" } => "Phòng đã đủ thành viên.",
        TeamRelayApiException { Code: "pro_required" } => "Relay chưa xác minh được Pro còn hạn. Hãy đăng nhập/xác minh Pro rồi tạo lại phòng 10 hoặc 21 người.",
        TeamRelayApiException { Code: "invalid_room_size" } => "Chọn một trong các quy mô 3, 7, 10 hoặc 21 người.",
        TeamRelayApiException { Code: "invite_not_found" } => "Không tìm thấy mã mời hoặc phòng đã hết hạn.",
        TimeoutException => "Relay không phản hồi. Hãy thử lại.",
        _ => $"Không thể thao tác nhóm: {exception.Message}"
    };
    private void BuildContact()
    {
        var p = Page("HỖ TRỢ CỘNG ĐỒNG", "LIÊN HỆ & BÁO LỖI", "Gửi góp ý, báo lỗi hoặc nhận hỗ trợ trực tiếp từ Hoàng Kim Long.");
        var columns = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var links = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        links.Children.Add(ContactLine("FACEBOOK", "Hoàng Kim Long", "MỞ FACEBOOK", "https://www.facebook.com/klong.dev/"));
        links.Children.Add(ContactLine("ZALO", "Hoàng Kim Long · 0705 8787 81", "SAO CHÉP SỐ ZALO", null));
        links.Children.Add(T("Khi báo lỗi, hãy gửi phiên bản app, server đang chơi và ảnh chụp màn hình nếu có.", 13, B("#91AAA3")));
        Grid.SetColumn(links, 0); columns.Children.Add(links);
        var qrStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        qrStack.Children.Add(T("NHÓM GÓP Ý · BÁO LỖI", 11, B("#49D5C3"), FontWeights.Bold));
        qrStack.Children.Add(new Image { Source = new BitmapImage(new Uri("/IsleLiveMap;component/Assets/ZaloTeamQr.jpg", UriKind.Relative)), Width = 270, Height = 270, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 6) });
        qrStack.Children.Add(T("Quét mã để tham gia Zalo", 12, B("#91AAA3")));
        Grid.SetColumn(qrStack, 1); columns.Children.Add(qrStack);
        p.Children.Add(columns);
    }
    private Border ContactLine(string label, string detail, string action, string? url)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        row.Children.Add(T(label, 11, B("#49D5C3"), FontWeights.Bold));
        row.Children.Add(T(detail, 16, null, FontWeights.SemiBold));
        var button = Action(action, (_, _) => { if (url is not null) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); else Clipboard.SetText("0705878781"); }, false);
        button.Margin = new Thickness(0, 8, 0, 0); button.MinWidth = 150; button.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(button);
        return new Border { Child = row, BorderBrush = B("#294943"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 14), Margin = new Thickness(0, 0, 0, 14) };
    }
    private Border Section(string label, string? hint = null)
    {
        var content = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
        content.Children.Add(T(label, 11, B(_proPresentation.HasCurrentProAccess ? "#D3A85C" : "#49D5C3"), FontWeights.Bold));
        if (!string.IsNullOrWhiteSpace(hint)) content.Children.Add(T(hint, 12, B("#91AAA3")));
        return new Border { Child = content, Background = Brushes.Transparent, BorderBrush = B("#294943"), BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 0, 0, 10) };
    }
    private Border InfoTile(string label, string value)
    {
        var tile = StatusLine(label, value);
        tile.Width = 150;
        tile.Margin = new Thickness(0, 0, 10, 10);
        tile.Padding = new Thickness(12, 10, 12, 10);
        tile.Background = B("#66101F1C");
        tile.BorderThickness = new Thickness(1);
        tile.CornerRadius = new CornerRadius(6);
        return tile;
    }
    private async void OpenMap_Click(object? sender, RoutedEventArgs e)
    {
        // UI Automation and a fast double click can otherwise start two map
        // flows. The first flow closes Home after creating MainWindow; the
        // second then resumes against a closed/disposed launcher lifetime.
        if (Interlocked.Exchange(ref _mapOpenStarted, 1) != 0)
        {
            return;
        }

        var handedOffToOverlay = false;
        RefreshLaunchButtons();
        try
        {

            if (!await PrepareMapLaunchAsync()) return;

            var store = new IslePilotCredentialStore(AppPaths.IslePilotCredential);
            using var authHttp = new System.Net.Http.HttpClient(
                new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = TimeSpan.FromSeconds(8) };
            var localOnlyRequested = false;
            var credentials = await new IslePilotOverlayLoginFlow(authHttp, store).ResolveAsync(_ =>
            {
                // Pro grants tracking, not an IslePilot login. Never silently
                // open without stats just because the saved login is missing.
                var login = new IslePilotSteamLoginWindow
                {
                    Owner = this,
                    AllowLocalOnly = HomeProPresentationPolicy.Evaluate(_pro, DateTimeOffset.UtcNow).HasCurrentProAccess
                };
                var loggedIn = login.ShowDialog() == true;
                localOnlyRequested = login.LocalOnlyRequested;
                return Task.FromResult(loggedIn ? login.Credentials : null);
            }, SetStatus, _shutdown.Token);
            if (credentials is null)
            {
                if (localOnlyRequested)
                    handedOffToOverlay = await OpenProOnlyOverlayAsync();
                else
                    SetStatus("Đã hủy đăng nhập IslePilot. Cần đăng nhập để nhận dino stats và nhiệm vụ.");
                return;
            }

            var session = new AuthenticationInvalidatingTelemetrySession(
                IslePilotRealtimeSession.Create(new IslePilotOverlayOptions
                {
                    OverlayToken = credentials.OverlayToken,
                    PlayerCookie = credentials.PlayerCookie
                }),
                store.Clear);
            // The Pro grant controls entitlement-gated UI, while the Agent
            // source carries the actual Player/AI telemetry. Both must be
            // supplied to the overlay; passing only the grant leaves Pro
            // users looking premium while tracking remains permanently off.
            handedOffToOverlay = await OpenOverlaySessionAsync(session, "ISLEPILOT");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetStatus($"Không mở được phiên: {ex.Message}");
        }
        finally
        {
            if (!handedOffToOverlay)
            {
                Interlocked.Exchange(ref _mapOpenStarted, 0);
                RefreshLaunchButtons();
            }
        }
    }

    private async Task<bool> OpenProOnlyOverlayAsync()
    {
        return await OpenOverlaySessionAsync(null, "PRO");
    }

    private async Task<IRemotePlayerTelemetrySource?> TakeProPlayerSourceAsync()
    {
        var source = await _proTelemetryWarmup.TakeAsync(_pro, _shutdown.Token);
        return source ?? _proService.CreateRemotePlayerSource();
    }

    private bool EnsureNpcapReady()
    {
        var availability = NpcapAvailabilityProbe.Check(refresh: true);
        if (availability.IsAvailable)
        {
            return true;
        }

        var setup = new NpcapRequiredWindow { Owner = this };
        if (setup.ShowDialog() != true)
        {
            SetStatus("Chưa có Npcap. Hãy cài Npcap để mở Live Map.");
            return false;
        }

        // The setup window re-checks after installation, but refresh once more
        // here because SharpPcap can cache the native resolver state per process.
        var ready = NpcapAvailabilityProbe.Check(refresh: true).IsAvailable;
        if (!ready)
        {
            SetStatus("Npcap chưa sẵn sàng sau khi cài đặt. Hãy thử lại.");
        }

        return ready;
    }

    private void WarmupLocalTelemetryIfReady()
    {
        try
        {
            if (NpcapAvailabilityProbe.Check().IsAvailable)
            {
                // Start capture before the game/session is opened so the first
                // handshake and movement packet are not lost. The merger still
                // requires fresh movement; warmup only makes that packet
                // available sooner and never reuses stale coordinates.
                App.CurrentApp.EnsureLocalTelemetryWarmup();
            }
        }
        catch
        {
            // Npcap is optional until the user opens the map. The existing
            // setup dialog remains the authoritative recovery path.
        }
    }

    private async Task PrepareUpdateAtStartupAsync()
    {
        if (_updateTask is { IsCompleted: false }) return;
        _updateTask = PrepareUpdateCoreAsync();
        try { await _updateTask; }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private async Task PrepareUpdateCoreAsync()
    {
        UpdatePreparationResult result;
        try
        {
            result = await _updateService.PrepareUpdateAsync(
                progress: _ =>
                {
                    var version = _updateService.PendingVersion;
                    if (version is not null)
                        Dispatcher.InvokeAsync(() => SetMapActionText($"ĐANG CẬP NHẬT v{version}"));
                },
                cancellationToken: _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _mapLaunchGateState = MapLaunchGatePolicy.FromUpdate(result.State);
            RefreshLaunchButtons();

            switch (result.State)
            {
                case UpdatePreparationState.Ready:
                    SetMapActionText(_proPresentation.HasCurrentProAccess ? "MỞ MAP PRO  →" : "MỞ MAP  →");
                    SetUpdateStatus($"Đã tải bản cập nhật v{result.Version}. Khởi động lại để hoàn tất trước khi mở map.", "#F0C36A");
                    break;
                case UpdatePreparationState.Current:
                    SetMapActionText(_proPresentation.HasCurrentProAccess ? "MỞ MAP PRO  →" : "MỞ MAP  →");
                    SetUpdateStatus("Bản cập nhật đã kiểm tra · phiên bản hiện tại", "#79D5B0");
                    break;
                case UpdatePreparationState.DevelopmentBuild:
                    SetMapActionText(_proPresentation.HasCurrentProAccess ? "MỞ MAP PRO  →" : "MỞ MAP  →");
                    SetUpdateStatus("Bản phát triển · bỏ qua kiểm tra cập nhật", "#91AAA3");
                    break;
                default:
                    // Network/update service failures are deliberately
                    // non-blocking, as requested. Users can still open map.
                    SetMapActionText(_proPresentation.HasCurrentProAccess ? "MỞ MAP PRO  →" : "MỞ MAP  →");
                    SetUpdateStatus("Không kiểm tra được cập nhật · vẫn cho phép mở map", "#E7B74E");
                    break;
            }

            if (result.State == UpdatePreparationState.Ready)
            {
                ShowUpdateReadyDialog(result.Version);
            }
        });
    }

    private void ShowUpdateReadyDialog(string? version)
    {
        if (_updateReadyDialogShown || !IsVisible)
        {
            return;
        }

        _updateReadyDialogShown = true;
        try
        {
            var dialog = new UpdateReadyWindow(version) { Owner = this };
            dialog.ShowDialog();
            if (dialog.ApplyRequested)
            {
                _updateService.ApplyAndRestart();
            }
        }
        catch (Exception)
        {
            SetUpdateStatus("Chưa thể khởi động lại để cập nhật. Hãy mở lại thông báo cập nhật và thử lại.", "#E7B74E");
        }
        finally
        {
            _updateReadyDialogShown = false;
        }
    }

    private Button? _reopenUpdateAction;

    private void SetUpdateStatus(string text, string color)
    {
        _lastUpdateStatus = text;
        _lastUpdateColor = color;
        if (_updateStatus is null) return;
        _updateStatus.Text = text;
        _updateStatus.Foreground = B(color);
    }

    private void SetMapActionText(string text)
    {
        if (_mapActionButton is not null)
            _mapActionButton.Content = text;
    }
    private void BuildInfo()
    {
        var p = Page("TỔNG QUAN DỮ LIỆU", "THÔNG TIN PHIÊN", "Bản đồ và thông tin dino của phiên gần nhất — không phải bản sao HUD overlay.");
        var snapshot = _snapshots.Current;
        var overview = new Grid { Height = 270, MinHeight = 230, Margin = new Thickness(0, 0, 0, 16) };
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(269) });
        overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        var map = new Grid { Width = 269, Height = 270, MinHeight = 230, ClipToBounds = true };
        map.Children.Add(new Image { Source = new BitmapImage(new Uri("/IsleLiveMap;component/Assets/GatewayMapWater.jpg", UriKind.Relative)), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        map.Children.Add(new Border { Background = B("#33071916") });
        if (snapshot?.Player?.MapLocation is { } point) map.Children.Add(Marker(point, "PLAYER", B("#49D5C3"), 269, 270));
        if (snapshot?.Map?.Markers is { } markers)
            foreach (var marker in markers.Where(m => m.MapLocation is not null).Take(80))
                map.Children.Add(Marker(marker.MapLocation!.Value, marker.Label ?? "TỪ XA", B("#E7B74E"), 269, 270));
        Grid.SetColumn(map, 0);
        overview.Children.Add(new Border { Child = map, CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderBrush = B("#294943"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 16, 0) });
        var stats = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        stats.Children.Add(T("THÔNG TIN DINO", 11, B(_proPresentation.HasCurrentProAccess ? "#D3A85C" : "#49D5C3"), FontWeights.Bold));
        if (snapshot?.Player is { } overviewPlayer)
        {
            foreach (var pair in new[] { ("NGƯỜI CHƠI", overviewPlayer.Name ?? "—"), ("LOÀI", overviewPlayer.Class ?? "—"), ("SERVER", overviewPlayer.Server ?? "—"), ("GROWTH", Percent(overviewPlayer.ExactVitals?.Growth ?? overviewPlayer.GrowthPercent)), ("MÁU", Percent(overviewPlayer.ExactVitals?.Health ?? overviewPlayer.HealthPercent)), ("THỂ LỰC", Percent(overviewPlayer.ExactVitals?.Stamina ?? overviewPlayer.StaminaPercent)), ("ĐÓI", Percent(overviewPlayer.ExactVitals?.Hunger ?? overviewPlayer.HungerPercent)), ("KHÁT", Percent(overviewPlayer.ExactVitals?.Thirst ?? overviewPlayer.ThirstPercent)) })
                stats.Children.Add(CompactStat(pair.Item1, pair.Item2));
        }
        else
        {
            stats.Children.Add(T("Chưa có phiên dữ liệu", 13, B("#E7B74E"), FontWeights.SemiBold));
            stats.Children.Add(T("Mở Live Map để xem thông tin dino tại đây.", 12, B("#91AAA3")));
        }
        Grid.SetColumn(stats, 1);
        overview.Children.Add(stats);
        p.Children.Add(overview);

        var panel = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(StatusLine("PHIÊN", snapshot is null ? "CHƯA CÓ PHIÊN" : snapshot.LiveDataStale ? "DỮ LIỆU CŨ" : "ĐANG TRỰC TIẾP"));
        panel.Children.Add(StatusLine("CẬP NHẬT", snapshot is null ? "—" : (snapshot.UpdatedAt ?? _snapshots.ReceivedAt ?? DateTimeOffset.UtcNow).ToLocalTime().ToString("HH:mm:ss")));
        panel.Children.Add(StatusLine("GPS", snapshot?.Player?.MapLocation is null ? "Chưa có vị trí" : "Đã định vị"));
        p.Children.Add(panel);

    }
    private Border CompactStat(string label, string value)
    {
        var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(T(label, 10, B("#68817A"), FontWeights.Bold));
        var valueText = T(value, 12, null, FontWeights.SemiBold);
        valueText.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return new Border { Child = row, BorderBrush = B("#294943"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 5) };
    }
    private static Border Marker(MapPoint point, string text, Brush color, double width, double height) => new() { Child = T("●  " + text, 11, color, FontWeights.Bold), Background = B("#DD071513"), Padding = new Thickness(5, 3, 5, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(point.Left * width, point.Top * height, 0, 0) };
    private static string Percent(double? value) => value is null ? "—" : $"{Math.Clamp(value.Value, 0, 100):0.#}%";
    private void BuildLocale()
    {
        var p = Page("NGÔN NGỮ GAME", "VIỆT HÓA", "Chọn ngôn ngữ trực tiếp. Trạng thái và lỗi hiển thị ngay trong workspace.");
        var section = Section("NGÔN NGỮ GAME", "Thay đổi được lưu vào launcher và áp dụng lại khi game khởi động.");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        row.Children.Add(Action("TIẾNG ANH", async (_, _) => await ChangeLocale(GameLocale.English), false));
        row.Children.Add(Action("TIẾNG VIỆT", async (_, _) => await ChangeLocale(GameLocale.Vietnamese)));
        ((StackPanel)section.Child).Children.Add(row);
        p.Children.Add(section);
        _status = T("Đọc trạng thái locale hiện tại…", 14, B("#91AAA3"));
        p.Children.Add(_status);
    }
    private async Task ChangeLocale(GameLocale locale) { SetStatus("Đang xác minh và cài locale…"); try { using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) }; var options = new LocalizationOptions(); await new LocalizationCoordinator(new TranslationDownloadClient(http, options), new TranslationPackageInstaller(options)).ApplyLocaleAsync(locale, false, _shutdown.Token); SetStatus($"Đã chọn {(locale == GameLocale.Vietnamese ? "Tiếng Việt" : "English")}. Mở lại game để áp dụng."); } catch (Exception ex) { SetStatus($"Không thể đổi ngôn ngữ: {ex.Message}"); } }
    private void BuildShortcuts()
    {
        var p = Page("ĐIỀU KHIỂN", "PHÍM TẮT", "Bấm vào ô nhập rồi nhấn tổ hợp phím mới. Mỗi thay đổi được kiểm tra ngay trên dòng tương ứng.");
        var settings = _shortcutStore.Load();
        var rows = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        var statusLabels = new Dictionary<OverlayShortcutAction, TextBlock>();
        Button? saveButton = null;
        foreach (var d in ShortcutSettingsManager.Definitions(true))
        {
            var surface = new Border
            {
                Background = B("#3D10231F"), BorderBrush = B("#294943"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7), Padding = new Thickness(14, 11, 12, 11), Margin = new Thickness(0, 0, 0, 14)
            };
            var row = new Grid { MinHeight = 52 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });

            var actionStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
            actionStack.Children.Add(T(d.Label, 13, B("#EAF4F0"), FontWeights.SemiBold));
            actionStack.Children.Add(T("Phím hiện tại", 10, B("#68817A")));
            row.Children.Add(actionStack);

            var field = new TextBox { Text = settings.For(d.Action), Style = (Style)FindResource("Field"), ToolTip = "Bấm vào đây rồi nhấn tổ hợp phím", Margin = new Thickness(0, 0, 8, 0) };
            field.PreviewKeyDown += ShortcutField_PreviewKeyDown;
            field.TextChanged += (_, _) =>
            {
                RefreshShortcutStatus(d.Action, statusLabels);
                if (saveButton is not null)
                {
                    saveButton.IsEnabled = _shortcutFields.Any(pair => !string.Equals(pair.Value.Text.Trim(), settings.For(pair.Key).Trim(), StringComparison.OrdinalIgnoreCase));
                }
            };
            _shortcutFields[d.Action] = field; Grid.SetColumn(field, 1); row.Children.Add(field);

            var status = T("HỢP LỆ", 10, B("#68D7A6"), FontWeights.Bold);
            status.VerticalAlignment = VerticalAlignment.Center;
            status.TextAlignment = TextAlignment.Center;
            statusLabels[d.Action] = status;
            Grid.SetColumn(status, 2); row.Children.Add(status);

            var reset = new Button { Content = "MẶC ĐỊNH", Style = (Style)FindResource("Ghost"), MinWidth = 80, Height = 32, FontSize = 10.5, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Khôi phục phím mặc định" };
            reset.Click += (_, _) => { field.Text = OverlayShortcutSettings.Defaults.For(d.Action); RefreshShortcutStatus(d.Action, statusLabels); };
            Grid.SetColumn(reset, 3); row.Children.Add(reset);
            surface.Child = row;
            rows.Children.Add(surface);
        }
        var footer = new Border { Background = B("#52152D28"), BorderBrush = B("#3B645A"), BorderThickness = new Thickness(1, 1, 1, 0), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 2, 0, 0), CornerRadius = new CornerRadius(7, 7, 0, 0) };
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var footerHint = T("Thay đổi chỉ có hiệu lực sau khi lưu.", 11, B("#91AAA3"));
        footerHint.VerticalAlignment = VerticalAlignment.Center;
        footerGrid.Children.Add(footerHint);
        saveButton = new Button { Content = "LƯU THAY ĐỔI", Style = (Style)FindResource("Action"), Height = 38, MinWidth = 142, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0), IsEnabled = false };
        saveButton.Click += SaveShortcuts;
        Grid.SetColumn(saveButton, 1); footerGrid.Children.Add(saveButton);
        footer.Child = footerGrid;
        p.Children.Add(footer);
        p.Children.Add(rows);
        foreach (var action in statusLabels.Keys.ToArray()) RefreshShortcutStatus(action, statusLabels);
    }
    private void RefreshShortcutStatus(OverlayShortcutAction action, IReadOnlyDictionary<OverlayShortcutAction, TextBlock> labels)
    {
        if (!labels.TryGetValue(action, out var label) || !_shortcutFields.TryGetValue(action, out var field)) return;
        var value = field.Text.Trim();
        if (string.IsNullOrWhiteSpace(value)) { label.Text = "CHƯA GÁN"; label.Foreground = B("#E7B74E"); return; }
        if (!ShortcutBinding.TryParse(value, out _, out _)) { label.Text = "KHÔNG HỢP LỆ"; label.Foreground = B("#F28C8C"); return; }
        var current = new Dictionary<OverlayShortcutAction, string>();
        foreach (var pair in _shortcutFields) current[pair.Key] = pair.Value.Text.Trim();
        var duplicate = current.Any(pair => pair.Key != action && !string.IsNullOrWhiteSpace(pair.Value) && string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase));
        label.Text = duplicate ? "BỊ TRÙNG" : "HỢP LỆ";
        label.Foreground = B(duplicate ? "#F28C8C" : "#68D7A6");
    }
    private void ShortcutField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox field) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if ((key is Key.Back or Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
        {
            field.Clear();
            return;
        }
        var keyName = key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            >= Key.F1 and <= Key.F24 => key.ToString(),
            Key.Space => "Space", Key.Enter => "Enter", Key.Escape => "Esc", Key.Insert => "Insert",
            Key.Home => "Home", Key.End => "End", Key.PageUp => "PageUp", Key.PageDown => "PageDown",
            Key.Left => "Left", Key.Up => "Up", Key.Right => "Right", Key.Down => "Down", _ => null
        };
        if (keyName is null) return;
        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        if (parts.Count == 0) return;
        parts.Add(keyName);
        field.Text = string.Join('+', parts);
        field.CaretIndex = field.Text.Length;
    }
    private void SaveShortcuts(object? sender, RoutedEventArgs e)
    {
        var current = _shortcutStore.Load();
        var next = current with
        {
            EditMode = Value(OverlayShortcutAction.EditMode),
            ToggleMissions = Value(OverlayShortcutAction.ToggleMissions),
            ToggleHud = Value(OverlayShortcutAction.ToggleHud),
            MapNotes = Value(OverlayShortcutAction.MapNotes),
            MutationGuide = Value(OverlayShortcutAction.MutationGuide)
        };
        var errors = ShortcutSettingsManager.Validate(next);
        if (errors.Count == 0 && _shortcutStore.TrySave(next, out _))
        {
            if (sender is Button button) button.IsEnabled = false;
            SetStatus("Đã lưu phím tắt.");
            return;
        }

        SetStatus(string.Join(" ", errors));
    }
    private string Value(OverlayShortcutAction action) => _shortcutFields.TryGetValue(action, out var field) ? field.Text : "";
    private void BuildSettings()
    {
        var p = Page("TÙY CHỈNH", "CÀI ĐẶT", "Thiết lập launcher và map; mọi thay đổi được lưu trực tiếp.");
        var catalog = GatewayStaticMapLayerCatalog.LoadBundled();
        _layers = _layerStore.Load(catalog.Defaults);
        var layersSection = Section("LỚP BẢN ĐỒ", "Bật/tắt trực tiếp các lớp hiển thị trên bản đồ.");
        var sectionStack = (StackPanel)layersSection.Child;
        var layerGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var pair in new[] { ("Nước uống được", MapLayerGroup.Water), ("Di cư", MapLayerGroup.Migration), ("Tuần tra", MapLayerGroup.Patrol), ("Khu an toàn", MapLayerGroup.Sanctuary), ("Đường đi", MapLayerGroup.Roads), ("Động vật", MapLayerGroup.Animals), ("Thực vật", MapLayerGroup.Plants), ("Tài nguyên", MapLayerGroup.Earth) })
        {
            var check = new CheckBox { Content = pair.Item1, IsChecked = _layers.IsEnabled(pair.Item2), Style = (Style)FindResource("Check") };
            check.Checked += (_, _) => SaveLayer(pair.Item2, true); check.Unchecked += (_, _) => SaveLayer(pair.Item2, false); layerGrid.Children.Add(check);
        }
        sectionStack.Children.Add(layerGrid);
        p.Children.Add(layersSection);

        var layoutSection = Section("BỐ CỤC OVERLAY", "Tỷ lệ áp dụng cho bản đồ overlay hiện tại.");
        var layoutStack = (StackPanel)layoutSection.Child; var layout = _layoutStore.Load();
        var scale = new Slider { Minimum = OverlayLayoutRules.MinimumScale, Maximum = OverlayLayoutRules.MaximumScale, Value = layout.Scale, Width = 280, Margin = new Thickness(0, 12, 0, 4), Style = (Style)FindResource("ScaleSlider") };
        AutomationProperties.SetName(scale, "Tỷ lệ kích thước overlay");
        AutomationProperties.SetHelpText(scale, "Điều chỉnh tỷ lệ hiển thị của map overlay");
        scale.ValueChanged += (_, _) => _layoutStore.Save(layout with { Scale = scale.Value }); layoutStack.Children.Add(scale); layoutStack.Children.Add(Action("RESET LAYOUT", (_, _) => _layoutStore.Save(new OverlayLayoutSettings()), false)); p.Children.Add(layoutSection);

        var telemetrySection = Section("DỮ LIỆU PHIÊN", "Nguồn dữ liệu và phiên hiện tại.");
        var telemetryStack = (StackPanel)telemetrySection.Child;
        telemetryStack.Children.Add(T(NpcapAvailabilityProbe.Check().IsAvailable ? "Npcap / GPS: Sẵn sàng" : "Npcap / GPS: Chưa sẵn sàng", 14, B("#91AAA3")));
        var clearCredentials = Action("XÓA THÔNG TIN ĐĂNG NHẬP", (_, _) => { new IslePilotCredentialStore(AppPaths.IslePilotCredential).Clear(); _snapshots.Clear(); SetStatus("Đã xóa thông tin đăng nhập và dữ liệu phiên hiện tại."); }, false);
        clearCredentials.MinWidth = 250;
        clearCredentials.MinHeight = 48;
        clearCredentials.Height = 48;
        clearCredentials.FontSize = 13;
        clearCredentials.Padding = new Thickness(18, 0, 18, 0);
        clearCredentials.Foreground = B("#FFFFFF");
        clearCredentials.Background = B("#B83D42");
        clearCredentials.BorderBrush = B("#F08A8A");
        clearCredentials.Margin = new Thickness(0, 12, 10, 0);
        telemetryStack.Children.Add(clearCredentials);
        telemetryStack.Children.Add(T("Đăng xuất khỏi tài khoản đang chơi để đổi tài khoản khác tránh lấy sai chỉ số dino", 12, B("#C9AAA5")));
        p.Children.Add(telemetrySection);
    }
    private void SaveLayer(MapLayerGroup group, bool value) { _layers.SetEnabled(group, value); _layers.Version = MapLayerPreferences.CurrentVersion; _layerStore.TrySave(_layers, out _); }
    private void BuildGuide()
    {
        var p = Page("QUY TRÌNH", "HƯỚNG DẪN", "Các bước vận hành ngắn gọn.");
        var section = Section("BẮT ĐẦU VỚI LIVE MAP", "Mỗi bước có một mục tiêu rõ ràng.");
        var stack = (StackPanel)section.Child;
        foreach (var text in new[] { "01  Mở map và chọn server.", "02  Chờ telemetry xác nhận phiên.", "03  Theo dõi player / dino trong THÔNG TIN.", "04  Chỉnh lớp map trong CÀI ĐẶT.", "05  Dùng shortcut trong PHÍM TẮT." }) stack.Children.Add(T(text, 15, null, FontWeights.SemiBold));
        p.Children.Add(section);
    }
    private void BuildPro()
    {
        // BuildPro can be called after an inline login/logout. Always replace
        // the current page before adding controls; otherwise WPF keeps the
        // previous visual tree and every refresh paints a second copy over it.
        var p = Page("QUYỀN TRUY CẬP", "PRO", "Không gian làm việc cao cấp cho theo dõi người chơi và AI.");
        var entitlement = _pro.Entitlement;
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        Button CompactAction(string text, RoutedEventHandler handler, bool primary = true)
        {
            var action = Action(text, handler, primary);
            action.MinHeight = 34; action.Height = 34; action.Margin = new Thickness(0, 0, 10, 5);
            return action;
        }
        if (!_pro.IsAuthenticated || !_proPresentation.HasCurrentProAccess)
            actions.Children.Add(CompactAction("ĐĂNG NHẬP / XÁC MINH", async (_, _) =>
            {
                var login = new ProSteamLoginWindow(_proService, CurrentVersion()) { Owner = this };
                if (login.ShowDialog() == true && login.Access is { } access)
                {
                    ApplyProPresentation(access, true);
                    await RefreshProTelemetryWarmupAsync(access);
                }
            }));
        actions.Children.Add(CompactAction("ĐĂNG KÝ PRO", (_, _) => Process.Start(new ProcessStartInfo("https://isle.klong.dev") { UseShellExecute = true }), false));
        if (_proPresentation.HasCurrentProAccess && !_pro.AgentReady) actions.Children.Add(CompactAction("KIỂM TRA LẠI", async (_, _) => await RefreshProAsync()));
        if (_pro.IsAuthenticated) actions.Children.Add(CompactAction("ĐĂNG XUẤT", async (_, _) =>
        {
            await _proTelemetryWarmup.StopAsync();
            await _proService.LogoutAsync(_shutdown.Token);
            ApplyProPresentation(ProAccessSnapshot.SignedOut, true);
        }, false));
        p.Children.Add(actions);
        var expiry = entitlement.ExpiresAt is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "Vĩnh viễn";
        var agentStatus = _pro.AgentReady ? "SẴN SÀNG" : _pro.StatusCode is "agent_unavailable" or "offline_agent_unavailable" ? "KHÔNG KHẢ DỤNG" : _pro.StatusCode == "agent_update_unavailable" ? "CHƯA CÓ BẢN CẬP NHẬT" : "ĐANG CHỜ";
        var identity = Section(_proPresentation.TierLabel, _pro.IsAuthenticated ? $"STEAM ••••{_pro.SteamId64![^4..]}" : "Chưa đăng nhập Steam");
        var identityStack = (StackPanel)identity.Child;
        identityStack.Children.Add(T(_proPresentation.StatusLabel, 17, B(_proPresentation.HasCurrentProAccess ? "#E6C477" : "#E7B74E"), FontWeights.Black));
        p.Children.Add(identity);

        var access = Section("GIẤY PHÉP & TRỢ LÝ", "Quyền Pro quyết định chế độ premium; trợ lý chỉ quyết định khả năng theo dõi.");
        var accessStack = (StackPanel)access.Child;
        var accessTiles = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        foreach (var item in new[] { ("GÓI / HẠN DÙNG", $"{entitlement.Tier.ToUpperInvariant()} · {expiry}"), ("KẾT NỐI", _pro.IsOffline ? "GIẤY PHÉP NGOẠI TUYẾN" : "GIẤY PHÉP TRỰC TUYẾN"), ("TRỢ LÝ", $"{agentStatus}{(_pro.AgentVersion is null ? "" : $" · v{_pro.AgentVersion}")}"), ("THEO DÕI PLAYER", _proPresentation.HasCurrentProAccess && _pro.AgentReady ? "ĐƯỢC PHÉP" : "CHỜ TRỢ LÝ"), ("THEO DÕI AI", _proPresentation.HasCurrentProAccess && _pro.AgentReady ? "ĐƯỢC PHÉP" : "CHỜ TRỢ LÝ") })
            accessTiles.Children.Add(InfoTile(item.Item1, item.Item2));
        accessStack.Children.Add(accessTiles);
        if (!string.IsNullOrWhiteSpace(_pro.StatusCode)) accessStack.Children.Add(T($"MÃ TRẠNG THÁI · {_pro.StatusCode}", 13, B(_pro.StatusCode == "license_service_unavailable" ? "#E77A68" : "#91AAA3"), FontWeights.SemiBold));
        p.Children.Add(access);
        var benefits = Section("LỢI ÍCH PRO", "Các tính năng mở rộng trong cùng một không gian làm việc.");
        var benefitStack = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 0) };
        benefitStack.Children.Add(BenefitLine("01", "Theo dõi người chơi", "Theo dõi vị trí và trạng thái người chơi theo phiên dữ liệu."));
        benefitStack.Children.Add(BenefitLine("02", "Theo dõi AI", "Hiển thị các thực thể AI từ dữ liệu map khi trợ lý sẵn sàng."));
        benefitStack.Children.Add(BenefitLine("03", "Không gian bản đồ", "Lớp bản đồ, dữ liệu phiên và trạng thái dino được gom trong một nơi."));
        p.Children.Add(benefits);
    }
    private Border BenefitLine(string number, string title, string detail)
    {
        var row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(T(number, 12, B("#D3A85C"), FontWeights.Black));
        var copy = new StackPanel();
        copy.Children.Add(T(title, 13, null, FontWeights.Bold));
        copy.Children.Add(T(detail, 12, B("#91AAA3")));
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return new Border { Child = row, BorderBrush = B("#5B4931"), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 0, 10, 0), Margin = new Thickness(0, 0, 10, 0) };
    }
    private Task LoadProAsync()
    {
        if (_proLoadTask is { IsCompleted: false })
        {
            return _proLoadTask;
        }

        _proLoadTask = LoadProCoreAsync();
        return _proLoadTask;
    }

    private async Task LoadProCoreAsync()
    {
        try { _pro = await _proService.InitializeAsync(CurrentVersion(), _shutdown.Token); }
        catch { _pro = ProAccessSnapshot.SignedOut with { StatusCode = "license_service_unavailable" }; }
        await Dispatcher.InvokeAsync(() => ApplyProPresentation(_pro, rebuildCurrentPage: true));
        await RefreshProTelemetryWarmupAsync(_pro);
    }
    private async Task RefreshProAsync()
    {
        await LoadProAsync();
    }

    private Task RefreshProTelemetryWarmupAsync(ProAccessSnapshot? access = null) =>
        _proTelemetryWarmup.RefreshAsync(access ?? _pro, _shutdown.Token);
    private void ApplyProPresentation(ProAccessSnapshot access, bool rebuildCurrentPage)
    {
        _pro = access;
        App.CurrentTeam.ConfigureAccess(
            access.Entitlement.IsProAt(DateTimeOffset.UtcNow) ? TeamAccessTier.Pro : TeamAccessTier.Free, access.EntitlementProof);
        _proPresentation = HomeProPresentationPolicy.Evaluate(access, DateTimeOffset.UtcNow);
        var premium = _proPresentation.IsPremiumMode;
        Resources["Accent"] = B(premium ? "#D3A85C" : "#49D5C3");
        Resources["AccentDark"] = B(premium ? "#49351F" : "#153F39");
        Resources["Line"] = B(premium ? "#5B4931" : "#294943");
        Resources["Dim"] = B(premium ? "#9B8968" : "#68817A");
        Resources["HoverSurface"] = B(premium ? "#3A3025" : "#251D3B37");
        Resources["PressedSurface"] = B(premium ? "#665233" : "#265048");
        ProShellStatusLabel.Text = _proPresentation.StatusLabel;
        ProShellStatusLabel.Foreground = B(premium ? "#E6C477" : _proPresentation.StatusLabel == "STEAM VERIFIED" ? "#E7B74E" : "#68817A");
        ProAccessNavButton.Content = premium ? "PRO ĐANG BẬT" : "QUYỀN PRO";
        ProAccessNavButton.FontSize = 11;
        ProAccessNavButton.Padding = new Thickness(8, 0, 8, 0);
        ProAccessNavButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        ProAccessNavButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        ProAccessNavButton.Background = B(premium ? "#5A6B4B24" : "#153F39");
        ProAccessNavButton.BorderBrush = B(premium ? "#D3A85C" : "#49D5C3");
        ProAccessNavButton.Foreground = B(premium ? "#F0D28C" : "#EAF4F0");
        ProAccessNavButton.SetValue(AutomationProperties.NameProperty, premium ? "QUYỀN PRO ĐANG BẬT" : "QUYỀN PRO");
        if (rebuildCurrentPage && IsLoaded) ReplacePage(_page switch { "home" => BuildHome, "team" => BuildTeam, "info" => BuildInfo, "locale" => BuildLocale, "shortcuts" => BuildShortcuts, "settings" => BuildSettings, "contact" => BuildContact, "guide" => BuildGuide, "pro" => BuildPro, _ => BuildHome });
    }
    private static TControl? FindVisualChild<TControl>(DependencyObject root, Func<TControl, bool> predicate) where TControl : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is TControl typed && predicate(typed)) return typed; if (FindVisualChild(child, predicate) is { } found) return found; }
        return null;
    }
    private void UpdateProShellStatus()
    {
        if (ProShellStatusLabel is null) return;
        var active = _proPresentation.IsPremiumMode;
        ProShellStatusLabel.Text = active ? "  PRO ĐANG BẬT" : _pro.IsAuthenticated ? "  STEAM ĐÃ XÁC MINH" : "  MIỄN PHÍ";
        ProShellStatusLabel.Foreground = active ? B("#E6C477") : _pro.IsAuthenticated ? B("#E7B74E") : B("#68817A");
    }
    private async Task LoadReleasesAsync()
    {
        try
        {
            foreach (var note in await _releaseService.LoadAsync(_shutdown.Token))
            {
                var details = string.Join(Environment.NewLine, note.Details);
                var body = T(details, 12.5, B("#B5C8C1"));
                body.MaxWidth = 218;
                body.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                body.LineHeight = 19;
                body.Margin = new Thickness(6, 7, 6, 2);
                var open = new Button
                {
                    Style = (Style)FindResource("ReleaseLink"),
                    Content = $"v{note.Version}  ·  {note.Title}",
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                open.FontSize = 12.5;
                open.Foreground = B("#F0D7A0");
                open.ToolTip = "Mở rộng hoặc thu gọn ghi chú phiên bản";
                var row = new StackPanel();
                row.Children.Add(open);
                row.Children.Add(body);
                body.Visibility = Visibility.Collapsed;
                var expanded = false;
                open.Click += (_, _) =>
                {
                    expanded = !expanded;
                    body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    open.Foreground = expanded ? B(_proPresentation.HasCurrentProAccess ? "#E6C477" : "#49D5C3") : B("#F0D7A0");
                };
                ReleaseList.Children.Add(new Border
                {
                    Child = row,
                    Background = B("#66101F1C"),
                    BorderBrush = B("#294943"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 9),
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch
        {
            ReleaseList.Children.Add(T("Release notes hiện không khả dụng.", 12, B("#91AAA3")));
        }
    }
    private void SetStatus(string text) { _launchStatus = text; if (_status is not null) _status.Text = text; }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Window_PreviewMouseLeftButtonDown(sender, e);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;

        var point = e.GetPosition(this);
        // Only the 64px title row is draggable. The event is registered on the
        // Window so empty title-bar space and text descendants use the same path.
        if (point.Y < 14 || point.Y > 78) return;
        try
        {
            // The handler lives on Window, so WPF receives the press even when
            // it starts on an empty title-bar area or a text descendant.
            // DragMove keeps the gesture in the same input stack as WPF and is
            // more reliable here than forwarding a synthetic non-client click.
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The window can lose its native handle during shutdown.
        }
        e.Handled = true;
    }
    private static bool IsInsideButton(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is Button) return true;
        return false;
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
