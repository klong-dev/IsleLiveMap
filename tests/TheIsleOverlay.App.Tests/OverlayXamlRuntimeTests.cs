using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayXamlRuntimeTests
{
    [Fact]
    public async Task Overlay_LayoutAndRenderSmokeWithoutGameOrDesktopInput()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var root = LoadPassiveOverlay();
                T Find<T>(string name) where T : FrameworkElement => (T)root.FindName(name);
                var map = Find<Border>("MapPanel");
                Assert.Null(map.Effect);
                Assert.Null(Find<Border>("StatsPanel").Effect);
                Assert.Null(Find<Image>("MapImage").CacheMode);
                Find<FrameworkElement>("EditToolbar").Visibility = Visibility.Collapsed;
                Find<FrameworkElement>("MapZoomControls").Visibility = Visibility.Collapsed;
                Find<FrameworkElement>("MapLayerControls").Visibility = Visibility.Collapsed;
                Find<FrameworkElement>("MapStateLabel").Visibility = Visibility.Collapsed;
                var count = Find<TextBlock>("RemotePlayerCountLabel");
                count.Text = "P 99 · CHỜ 10 · AI 99";
                count.Visibility = Visibility.Visible;
                Find<FrameworkElement>("RemoteEntityLegend").Visibility = Visibility.Visible;
                Find<TextBlock>("CoordinateLabel").Text = "X -254.8  Y 80.5  Z 28.1";
                Find<TextBlock>("MapLayerSummaryLabel").Text = "MMZ 10 · PZ 18 · FOOD 25";
                var status = Find<TextBlock>("RemoteTrackingStatusLabel");
                status.Text = "PRO · ĐANG CHỜ PACKET · 3 ADAPTER";
                status.Visibility = Visibility.Visible;
                Layout(root);
                var info = Find<Border>("MapInfoPanel");
                Assert.InRange(info.ActualHeight, 20d, 70d);
                Assert.InRange(info.ActualWidth, 150d, 290d);
                Assert.True(count.ActualWidth >= count.DesiredSize.Width
                    - count.Margin.Left - count.Margin.Right - 0.5d);
                var target = new RenderTargetBitmap(304, 304, 96, 96, PixelFormats.Pbgra32);
                target.Render(map);
                var artifactDirectory = Path.Combine(AppContext.BaseDirectory, "Artifacts");
                Directory.CreateDirectory(artifactDirectory);
                using var file = File.Create(Path.Combine(artifactDirectory, "overlay-hud-smoke.png"));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(target));
                encoder.Save(file);

                // The same XAML still lays out with edit controls visible.
                Find<FrameworkElement>("EditToolbar").Visibility = Visibility.Visible;
                Layout(root);
                target.Render(map);
                completed.TrySetResult();
            }
            catch (Exception error) { completed.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static void Layout(Grid root)
    {
        root.Measure(new Size(960, 1080));
        root.Arrange(new Rect(0, 0, 960, 1080));
        root.UpdateLayout();
    }

    private static Grid LoadPassiveOverlay()
    {
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "MainWindow.xaml")).Root!;
        var app = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "TestAssets", "App.xaml")).Root!;
        var root = new XElement(source.Element(ui + "Grid")!);
        foreach (var ns in source.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            root.SetAttributeValue(ns.Name, ns.Value);
        root.AddFirst(new XElement(ui + "Grid.Resources",
            app.Element(ui + "Application.Resources")!.Elements().Select(element => new XElement(element)),
            source.Element(ui + "Window.Resources")!.Elements().Select(element => new XElement(element))));

        // Exercise real WPF styles/layout only. No window, hotkeys, capture,
        // account access, settings writes or interaction with the user's game.
        string[] events = ["SizeChanged", "Click", "PreviewMouseLeftButtonDown", "PreviewMouseMove",
            "PreviewMouseLeftButtonUp", "DragStarted", "DragDelta", "DragCompleted"];
        foreach (var attribute in root.DescendantsAndSelf().Attributes()
                     .Where(attribute => events.Contains(attribute.Name.LocalName)).ToArray())
            attribute.Remove();
        return (Grid)XamlReader.Parse(root.ToString());
    }
}
