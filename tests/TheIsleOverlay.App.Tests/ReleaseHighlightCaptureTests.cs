using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using TheIsleOverlay.Core;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class ReleaseHighlightCaptureTests
{
    [Fact]
    public async Task CaptureFeatureFrames_WhenExplicitlyEnabled()
    {
        var outputDirectory = Environment.GetEnvironmentVariable(
            "ISLELIVEMAP_RELEASE_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "IsleLiveMap.ReleaseCapture",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(outputDirectory);
                Directory.CreateDirectory(temporaryDirectory);
                Environment.SetEnvironmentVariable(
                    "ISLELIVEMAP_MAP_LAYER_SETTINGS_PATH",
                    Path.Combine(temporaryDirectory, "map-layer-settings.json"));
                var application = Application.Current as App ?? new App();
                application.InitializeComponent();
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                CaptureOverlayFrames(outputDirectory);
                CaptureMapNotesFrame(outputDirectory, temporaryDirectory);
                CaptureReleasePages(outputDirectory, temporaryDirectory);
                completed.TrySetResult();
            }
            catch (Exception error)
            {
                completed.TrySetException(error);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    "ISLELIVEMAP_MAP_LAYER_SETTINGS_PATH",
                    null);
                if (Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    private static void CaptureOverlayFrames(string outputDirectory)
    {
        using var session = new SilentTelemetrySession();
        var window = new MainWindow(session, "Release Capture");
        Invoke(window, "InitializeMapLayers");
        Invoke(window, "LoadMap");
        LayoutWindow(window, 1040d, 900d);
        Invoke(window, "PositionMapLayers", 0d, 0d, 304d, 304d);

        var inspector = Element<Border>(window, "MapLayerInspector");
        inspector.Visibility = Visibility.Visible;
        LayoutWindow(window, 1040d, 900d);
        SaveRegion(
            (FrameworkElement)window.Content,
            inspector,
            Path.Combine(outputDirectory, "MapLayerInspector.png"),
            scale: 2d);
        Element<Border>(window, "EditToolbar").Visibility = Visibility.Visible;
        LayoutWindow(window, 1040d, 900d);
        SaveRegion(
            (FrameworkElement)window.Content,
            Element<Border>(window, "EditToolbar"),
            Path.Combine(outputDirectory, "EditModeCommandBar.png"),
            scale: 2d);

        PopulateTeamPreview(window);
        LayoutWindow(window, 1040d, 900d);
        Invoke(window, "PositionMap");
        SaveRegion(
            (FrameworkElement)window.Content,
            Element<Border>(window, "TeamPanel"),
            Path.Combine(outputDirectory, "SurvivalTeamPanel.png"),
            scale: 2d);

        var count = Element<TextBlock>(window, "RemotePlayerCountLabel");
        count.Text = "P 7 · CHỜ 1 · AI 12";
        count.Visibility = Visibility.Visible;
        Element<StackPanel>(window, "RemoteEntityLegend").Visibility = Visibility.Visible;
        var status = Element<TextBlock>(window, "RemoteTrackingStatusLabel");
        status.Text = "PRO · ĐÃ ĐỒNG BỘ · 7 PLAYER · 12 AI";
        status.Visibility = Visibility.Visible;
        Element<TextBlock>(window, "MapStateLabel").Visibility = Visibility.Collapsed;
        AddTrackingDots(Element<Canvas>(window, "RemotePlayerMarkerLayer"));
        LayoutWindow(window, 1040d, 900d);
        Save(Element<Border>(window, "MapPanel"),
            Path.Combine(outputDirectory, "CompactTrackingMap.png"), scale: 2d);
        window.Close();
    }

    private static void PopulateTeamPreview(MainWindow window)
    {
        var now = DateTimeOffset.UtcNow;
        var localMember = Guid.NewGuid();
        var alpha = Guid.NewGuid();
        var bravo = Guid.NewGuid();
        var state = new TeamRelayState
        {
            ConnectionState = TeamRelayConnectionState.Live,
            Session = new TeamSession(
                Guid.NewGuid(), localMember, "7K9Q2M", "capture-token", 10, 5),
            StateRevision = 42,
            Members =
            [
                Member(localMember, "Bạn", 0.50, 0.50, "Triceratops", 100, 82, 74, now),
                Member(alpha, "Alpha", 0.42, 0.47, "Deinosuchus", 91, 68, 55, now),
                Member(bravo, "Bravo", 0.57, 0.54, "Pteranodon", 76, 42, 88, now)
            ]
        };
        SetField(window, "_localServerEndpoint", "115.72.226.156:7777");
        SetField(window, "_localServerName", "Gateway Community");
        SetField(window, "_pendingTeamState", state);
        Invoke(window, "RenderTeamState", state);
    }

    private static TeamMemberSnapshot Member(
        Guid id,
        string name,
        double left,
        double top,
        string species,
        double health,
        double hunger,
        double thirst,
        DateTimeOffset now) => new(
            id,
            name,
            true,
            now,
            new TeamMemberTelemetry
            {
                Sequence = 5,
                Source = "capture",
                ServerKey = "115.72.226.156:7777",
                ServerEndpoint = "115.72.226.156:7777",
                ServerName = "Gateway Community",
                MapId = "gateway",
                Species = species,
                HealthPercent = health,
                HungerPercent = hunger,
                ThirstPercent = thirst,
                MapLeft = left,
                MapTop = top,
                HeadingDegrees = left * 360d,
                UpdatedAt = now
            })
        {
            StateRevision = 42,
            ClientTelemetryObservedAt = DateTimeOffset.UtcNow
        };

    private static void AddTrackingDots(Canvas layer)
    {
        (double X, double Y, string Color, string Label)[] markers =
        [
            (78, 96, "#F04444", "T-Rex"),
            (198, 82, "#42D66B", "Trice"),
            (232, 188, "#3EA6FF", "Galli"),
            (116, 222, "#F5C542", "AI"),
            (172, 146, "#C7D0D4", "?")
        ];
        foreach (var marker in markers)
        {
            var dot = new Ellipse
            {
                Width = 9d,
                Height = 9d,
                Fill = Brush(marker.Color),
                Stroke = Brushes.White,
                StrokeThickness = 0.7d,
                ToolTip = marker.Label
            };
            Canvas.SetLeft(dot, marker.X);
            Canvas.SetTop(dot, marker.Y);
            layer.Children.Add(dot);
        }
    }

    private static void CaptureMapNotesFrame(string outputDirectory, string temporaryDirectory)
    {
        var store = new MapNoteStore(Path.Combine(temporaryDirectory, "map-notes.json"));
        var rally = store.TryAddDefault(0.44d, 0.42d).Note!;
        store.TryChangeKind(rally.Id, MapNoteKind.Rally);
        var danger = store.TryAddDefault(0.61d, 0.56d).Note!;
        store.TryChangeKind(danger.Id, MapNoteKind.Danger);
        var water = store.TryAddDefault(0.52d, 0.68d).Note!;
        store.TryChangeKind(water.Id, MapNoteKind.Water);
        var window = new MapNotesWindow(
            store,
            GatewayMapProjection.Unproject(new MapPoint(0.5d, 0.5d)),
            125d);
        window.Width = 980d;
        window.Height = 700d;
        Element<Border>(window, "MapFrame").Width = 930d;
        Element<Border>(window, "MapFrame").Height = 510d;
        LayoutWindow(window, 980d, 700d);
        Invoke(window, "RenderMap");
        LayoutWindow(window, 980d, 700d);
        Save(Element<Border>(window, "WindowChrome"),
            Path.Combine(outputDirectory, "MapNotesAltM.png"), scale: 1.35d);
        window.Close();
    }

    private static void CaptureReleasePages(string outputDirectory, string temporaryDirectory)
    {
        var preferences = new ReleaseHighlightsPreferenceStore(
            Path.Combine(temporaryDirectory, "release-highlights.json"));
        var window = new ReleaseHighlightsWindow("candidate", false, preferences);
        Element<Grid>(window, "ReleaseContent").Opacity = 1d;
        Element<System.Windows.Media.TranslateTransform>(window, "ReleaseEntranceTransform").Y = 0d;
        LayoutWindow(window, 1080d, 720d);
        var next = Element<Button>(window, "NextButton");
        for (var page = 1; page <= ReleaseHighlightsWindow.PageCount; page++)
        {
            LayoutWindow(window, 1080d, 720d);
            Save(
                (FrameworkElement)window.Content,
                Path.Combine(outputDirectory, $"ModalPage{page}.png"),
                scale: 1d);
            if (page < ReleaseHighlightsWindow.PageCount)
                next.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        window.Close();
    }

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0d, 0d, width, height));
        root.UpdateLayout();
    }

    private static void LayoutWindow(Window window, double width, double height)
    {
        window.Width = width;
        window.Height = height;
        Layout((FrameworkElement)window.Content, width, height);
    }

    private static void Save(FrameworkElement element, string path, double scale)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (element.ActualWidth <= 0d || element.ActualHeight <= 0d)
            throw new InvalidOperationException($"Capture target '{element.Name}' has no layout size.");
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale)),
            96d * scale,
            96d * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void SaveRegion(
        FrameworkElement root,
        FrameworkElement element,
        string path,
        double scale)
    {
        if (root.ActualWidth <= 0d || root.ActualHeight <= 0d
            || element.ActualWidth <= 0d || element.ActualHeight <= 0d)
            throw new InvalidOperationException($"Capture target '{element.Name}' has no layout size.");
        var full = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(root.ActualWidth * scale)),
            Math.Max(1, (int)Math.Ceiling(root.ActualHeight * scale)),
            96d * scale,
            96d * scale,
            PixelFormats.Pbgra32);
        full.Render(root);
        var origin = element.TransformToAncestor(root).Transform(new Point(0d, 0d));
        var x = Math.Clamp((int)Math.Floor(origin.X * scale), 0, full.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Floor(origin.Y * scale), 0, full.PixelHeight - 1);
        var width = Math.Clamp(
            (int)Math.Ceiling(element.ActualWidth * scale),
            1,
            full.PixelWidth - x);
        var height = Math.Clamp(
            (int)Math.Ceiling(element.ActualHeight * scale),
            1,
            full.PixelHeight - y);
        var cropped = new CroppedBitmap(full, new Int32Rect(x, y, width, height));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        brush.Freeze();
        return brush;
    }

    private static void Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, arguments);

    private static void SetField(object instance, string name, object? value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static T Element<T>(FrameworkElement root, string name) where T : DependencyObject =>
        Assert.IsType<T>(root.FindName(name));

    private sealed class SilentTelemetrySession : ITelemetrySession, IDisposable
    {
        public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }
}
