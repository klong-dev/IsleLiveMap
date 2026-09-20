using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TheIsleOverlay.Core;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class MapNotesWindowLifecycleTests
{
    [Fact]
    public async Task TacticalMap_CanOpenAndCloseFiftyTimesWithoutOrphanWindows()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "IsleLiveMap.Tests",
                Guid.NewGuid().ToString("N"));
            var notesPath = Path.Combine(temporaryDirectory, "map-notes.json");
            var layerSettingsPath = Path.Combine(temporaryDirectory, "map-layer-settings.json");
            try
            {
                Environment.SetEnvironmentVariable(
                    "ISLELIVEMAP_MAP_LAYER_SETTINGS_PATH",
                    layerSettingsPath);
                var application = Application.Current as App ?? new App();
                application.InitializeComponent();
                application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var store = new MapNoteStore(notesPath);

                for (var index = 0; index < 50; index++)
                {
                    var window = new MapNotesWindow(store, null, 0d);
                    window.Show();
                    window.Close();
                }

                Assert.Empty(application.Windows.OfType<MapNotesWindow>());
                VerifyLayerInspectorAndPersistence();
                VerifyPeerSymmetry();
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

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static void VerifyLayerInspectorAndPersistence()
    {
        var catalog = GatewayStaticMapLayerCatalog.LoadBundled();
        using var session = new SilentTelemetrySession();
        var window = new MainWindow(session, "Layer UI Test");
        Invoke(window, "InitializeMapLayers");
        PositionLayers(window);

        var inspector = Element<Border>(window, "MapLayerInspector");
        var layerButton = Element<Button>(window, "MapLayersButton");
        var inactiveBackground = layerButton.Background.ToString();
        Element<Button>(window, "MapLayersButton").RaiseEvent(
            new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(Visibility.Visible, inspector.Visibility);
        Assert.NotEqual(inactiveBackground, layerButton.Background.ToString());

        var initialZoneVisuals = VisibleChildren(window, "MapZoneLayer");
        Assert.True(initialZoneVisuals > 0);
        Assert.All(
            Element<Canvas>(window, "MapZoneLayer").Children.Cast<UIElement>().ToArray(),
            child => Assert.IsNotType<TextBlock>(child));
        Assert.Equal(0, VisibleChildren(window, "MapFoodLayer"));

        AssertGroupDelta(window, "MigrationLayerToggle", catalog.Zones.Count(zone => zone.Kind == MapZoneKind.Migration));
        AssertGroupDelta(window, "PatrolLayerToggle", catalog.Zones.Count(zone => zone.Kind == MapZoneKind.Patrol));
        AssertGroupDelta(window, "SanctuaryLayerToggle", catalog.Zones.Count(zone => zone.Kind == MapZoneKind.Sanctuary));
        AssertGroupDelta(window, "RoadLayerToggle", catalog.Routes.Count);
        AssertGroupGain(window, "AiSpawnLayerToggle", catalog.AiSpawnZones.Count);
        var waterVisualsBeforeToggle = VisibleChildren(window, "MapZoneLayer");
        SetToggle(window, "WaterLayerToggle", false);
        PositionLayers(window);
        Assert.Equal(waterVisualsBeforeToggle, VisibleChildren(window, "MapZoneLayer"));
        SetToggle(window, "WaterLayerToggle", true);
        PositionLayers(window);
        Assert.Equal(waterVisualsBeforeToggle, VisibleChildren(window, "MapZoneLayer"));
        Assert.All(
            Element<Canvas>(window, "MapZoneLayer").Children.OfType<FrameworkElement>().ToArray(),
            child => Assert.IsNotType<TextBlock>(child));

        AssertResourceGroup(window, "AnimalsLayerToggle", catalog.Resources.Count(resource => resource.Category == "animals"));
        var boarCount = catalog.Resources.Count(resource => resource.Key == "boar");
        SetToggle(window, "AnimalsLayerToggle", true);
        var animalsBeforeChildFilter = VisibleChildren(window, "MapFoodLayer");
        var boarToggle = Element<StackPanel>(window, "ResourceLayerChildrenPanel")
            .Children.OfType<CheckBox>()
            .Single(toggle => string.Equals(toggle.Tag as string, "boar", StringComparison.OrdinalIgnoreCase));
        boarToggle.IsChecked = false;
        boarToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PositionLayers(window);
        Assert.Equal(animalsBeforeChildFilter - boarCount, VisibleChildren(window, "MapFoodLayer"));
        SetToggle(window, "AnimalsLayerToggle", false);

        AssertResourceGroup(window, "PlantsLayerToggle", catalog.Resources.Count(resource => resource.Category == "plants"));
        AssertResourceGroup(window, "EarthLayerToggle", catalog.Resources.Count(resource => resource.Category == "earth"));

        SetToggle(window, "MigrationLayerToggle", false);
        SetToggle(window, "AnimalsLayerToggle", true);
        window.Close();

        using var restoredSession = new SilentTelemetrySession();
        var restored = new MainWindow(restoredSession, "Layer Persistence Test");
        Invoke(restored, "InitializeMapLayers");
        PositionLayers(restored);
        Assert.False(Element<ToggleButton>(restored, "MigrationLayerToggle").IsChecked);
        Assert.True(Element<ToggleButton>(restored, "AnimalsLayerToggle").IsChecked);
        Assert.False(Element<StackPanel>(restored, "ResourceLayerChildrenPanel")
            .Children.OfType<CheckBox>()
            .Single(toggle => string.Equals(toggle.Tag as string, "boar", StringComparison.OrdinalIgnoreCase))
            .IsChecked);
        Assert.Equal(
            catalog.Resources.Count(resource => resource.Category == "animals" && resource.Key != "boar"),
            VisibleChildren(restored, "MapFoodLayer"));
        restored.Close();
    }

    private static void VerifyPeerSymmetry()
    {
        var teamId = Guid.NewGuid();
        var memberIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var members = memberIds.Select((memberId, index) => new TeamMemberSnapshot(
            memberId,
            $"Member {index + 1}",
            true,
            DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15),
            new TeamMemberTelemetry
            {
                Sequence = 3,
                ServerKey = "115.72.226.156:7777",
                ServerEndpoint = "115.72.226.156:7777",
                ServerName = "Gateway Matrix",
                MapId = "gateway",
                Species = "Triceratops",
                HealthPercent = 95,
                HungerPercent = 72,
                ThirstPercent = 81,
                MapLeft = 0.42d + index * 0.08d,
                MapTop = 0.44d + index * 0.06d,
                UpdatedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(15)
            })
        {
            StateRevision = 20,
            ClientTelemetryObservedAt = DateTimeOffset.UtcNow
        }).ToArray();

        for (var viewer = 0; viewer < memberIds.Length; viewer++)
        {
            using var session = new SilentTelemetrySession();
            var window = new MainWindow(session, $"Viewer {viewer + 1}");
            var state = new TeamRelayState
            {
                ConnectionState = TeamRelayConnectionState.Live,
                Session = new TeamSession(
                    teamId,
                    memberIds[viewer],
                    "ABC123",
                    "test-token",
                    10,
                    5),
                Members = members,
                StateRevision = 20
            };
            var waitingForLocalServer = viewer == 1;
            SetField(
                window,
                "_localServerEndpoint",
                waitingForLocalServer ? null : "115.72.226.156:7777");
            SetField(
                window,
                "_localServerName",
                waitingForLocalServer ? null : "Gateway Matrix");
            SetField(window, "_pendingTeamState", state);
            Invoke(window, "RenderTeamState", state);

            var rows = Assert.IsType<MainWindow.TeamMemberRowViewModel[]>(
                Element<ItemsControl>(window, "TeamMembersList").ItemsSource);
            Assert.Equal(2, rows.Length);
            Assert.All(rows, row =>
            {
                Assert.StartsWith(
                    waitingForLocalServer ? "CHỜ SERVER" : "CÙNG SERVER",
                    row.DetailText,
                    StringComparison.Ordinal);
                Assert.NotEqual("—", row.HealthText);
                Assert.Equal(waitingForLocalServer ? 0.68d : 1d, row.Opacity);
            });
            Assert.Equal(2, Element<Canvas>(window, "TeamMarkerLayer").Children.Count);
            window.Close();
        }
    }

    private static void AssertGroupDelta(MainWindow window, string toggleName, int expectedDelta)
    {
        var before = VisibleChildren(window, "MapZoneLayer");
        SetToggle(window, toggleName, false);
        Assert.Equal(before - expectedDelta, VisibleChildren(window, "MapZoneLayer"));
        SetToggle(window, toggleName, true);
        Assert.Equal(before, VisibleChildren(window, "MapZoneLayer"));
    }

    private static void AssertGroupGain(MainWindow window, string toggleName, int expectedGain)
    {
        var before = VisibleChildren(window, "MapZoneLayer");
        SetToggle(window, toggleName, true);
        Assert.Equal(before + expectedGain, VisibleChildren(window, "MapZoneLayer"));
        SetToggle(window, toggleName, false);
        Assert.Equal(before, VisibleChildren(window, "MapZoneLayer"));
    }

    private static void AssertResourceGroup(MainWindow window, string toggleName, int expectedVisible)
    {
        Assert.Equal(0, VisibleChildren(window, "MapFoodLayer"));
        SetToggle(window, toggleName, true);
        Assert.Equal(expectedVisible, VisibleChildren(window, "MapFoodLayer"));
        SetToggle(window, toggleName, false);
        Assert.Equal(0, VisibleChildren(window, "MapFoodLayer"));
    }

    private static void SetToggle(MainWindow window, string name, bool enabled)
    {
        var toggle = Element<ToggleButton>(window, name);
        toggle.IsChecked = enabled;
        toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PositionLayers(window);
    }

    private static int VisibleChildren(MainWindow window, string canvasName) =>
        Element<Canvas>(window, canvasName).Children
            .OfType<FrameworkElement>()
            .Count(element => element.Visibility == Visibility.Visible);

    private static void PositionLayers(MainWindow window) => Invoke(
        window,
        "PositionMapLayers",
        0d,
        0d,
        1000d,
        1003d);

    private static void Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, arguments);

    private static void SetField(object instance, string name, object? value) =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);

    private static T Element<T>(FrameworkElement root, string name) where T : FrameworkElement =>
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
