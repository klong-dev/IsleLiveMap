using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

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

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static void VerifyLayerInspectorAndPersistence()
    {
        var catalog = GatewayStaticMapLayerCatalog.LoadBundled();
        using var session = new SilentTelemetrySession();
        var window = new MainWindow(session, "Layer UI Test");
        Invoke(window, "InitializeMapLayers");
        PositionLayers(window);

        var inspector = Element<Border>(window, "MapLayerInspector");
        Element<Button>(window, "MapLayersButton").RaiseEvent(
            new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Equal(Visibility.Visible, inspector.Visibility);

        var initialZoneVisuals = VisibleChildren(window, "MapZoneLayer");
        Assert.True(initialZoneVisuals > 0);
        Assert.Equal(0, VisibleChildren(window, "MapFoodLayer"));

        AssertGroupDelta(window, "MigrationLayerToggle", catalog.Zones.Count(zone => zone.Kind == MapZoneKind.Migration) * 2);
        AssertGroupDelta(window, "PatrolLayerToggle", catalog.Zones.Count(zone => zone.Kind == MapZoneKind.Patrol) * 2);
        AssertGroupDelta(window, "SanctuaryLayerToggle", catalog.Zones.Count(zone => zone.Kind == MapZoneKind.Sanctuary) * 2);
        AssertGroupDelta(window, "RoadLayerToggle", catalog.Routes.Count);
        AssertGroupGain(window, "AiSpawnLayerToggle", catalog.AiSpawnZones.Count * 2);
        AssertGroupGain(window, "WaterLayerToggle", catalog.WaterLabels.Count);

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
