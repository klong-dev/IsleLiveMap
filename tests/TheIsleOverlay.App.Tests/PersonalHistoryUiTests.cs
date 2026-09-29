using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class PersonalHistoryUiTests
{
    public static void VerifyFreeHistoryOnApplicationThread()
    {
            var folder = Path.Combine(Path.GetTempPath(), "history-ui-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new MapNoteStore(Path.Combine(folder, "notes.json"));
                var position = GatewayMapProjection.Unproject(new MapPoint(.5, .5));
                var marker = store.AddHistory(position, "server", DateTimeOffset.UtcNow).Note!;
                store.AddHistory(position, "other-server", DateTimeOffset.UtcNow);
                store.AddDefault(.6, .6);
                var window = new MapNotesWindow(store, position with { X = position.X + 30000 }, 0, allowManualNotes: false, historyServer: "server");
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var visible = (IReadOnlyList<MapNotePresentation>)typeof(MapNotesWindow).GetMethod("VisibleNotes", flags)!.Invoke(window, null)!;
                if (!PersonalHistoryFeature.Enabled)
                {
                    Assert.Empty(visible);
                    var toggle = (CheckBox)window.FindName("HistoryRelayToggle");
                    Assert.Equal(Visibility.Collapsed, ((FrameworkElement)toggle.Parent).Visibility);
                    window.Close();
                    var proWindow = new MapNotesWindow(store, position, 0, historyServer: "server");
                    var proVisible = (IReadOnlyList<MapNotePresentation>)typeof(MapNotesWindow).GetMethod("VisibleNotes", flags)!.Invoke(proWindow, null)!;
                    Assert.False(Assert.Single(proVisible).IsPersonalHistory);
                    proWindow.Close();
                    var saved = new MapNoteStore(Path.Combine(folder, "notes.json"));
                    Assert.Equal(2, saved.Notes.Count(n => n.IsPersonalHistory));
                    Assert.Contains(saved.Notes, n => n.Id == marker.Id);
                    var overlay = new MainWindow();
                    try
                    {
                        typeof(MainWindow).GetMethod("InitializePersonalHistory", flags)!.Invoke(overlay, null);
                        Assert.Null(typeof(MainWindow).GetField("_historyTimer", flags)!.GetValue(overlay));
                        typeof(MainWindow).GetMethod("SetHistoryRelayEnabled", flags)!.Invoke(overlay, [true]);
                        Assert.False((bool)typeof(MainWindow).GetField("_historyRelayEnabled", flags)!.GetValue(overlay)!);
                    }
                    finally { overlay.Close(); }
                    return;
                }
                Assert.Equal(marker.Id, Assert.Single(visible).Id);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("CoordinateEntryPanel")).Visibility);
                var creation = (Task<string?>)typeof(MapNotesWindow).GetMethod("TryCreateNoteAtAsync", flags)!.Invoke(window, [new MapPoint(.2, .3)])!;
                Assert.NotNull(creation.GetAwaiter().GetResult());
                var root = (FrameworkElement)window.Content;
                ((FrameworkElement)window.FindName("MapFrame")).Width = 540;
                ((FrameworkElement)window.FindName("MapFrame")).Height = 540;
                root.Measure(new Size(600, 740)); root.Arrange(new Rect(0, 0, 600, 740)); root.UpdateLayout();
                typeof(MapNotesWindow).GetField("_selectedNoteId", flags)!.SetValue(window, marker.Id);
                typeof(MapNotesWindow).GetMethod("RenderMap", flags)!.Invoke(window, null);
                var capture = Environment.GetEnvironmentVariable("ISLE_HISTORY_UI_CAPTURE");
                if (!string.IsNullOrWhiteSpace(capture))
                {
                    Directory.CreateDirectory(capture);
                    var bitmap = new RenderTargetBitmap(600, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(capture, "personal-history-free.png")); encoder.Save(file);
                }
                var change = typeof(MapNotesWindow).GetMethod("DeleteOrChangeSelectedAsync", flags)!;
                ((Task)change.Invoke(window, [visible[0], MapNoteIconCatalog.For(MapNoteKind.Death)])!).GetAwaiter().GetResult();
                Assert.Equal(MapNoteKind.Death, store.Notes.Single(n => n.Id == marker.Id).Kind);
                ((Task)change.Invoke(window, [visible[0], MapNoteIconCatalog.Palette[0]])!).GetAwaiter().GetResult();
                Assert.DoesNotContain(store.Notes, n => n.Id == marker.Id);
                window.Close();
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
