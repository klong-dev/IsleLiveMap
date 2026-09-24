using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class ServerConnectionsTests
{
    [Theory]
    [InlineData("https://1.sdvn.org/me", true)]
    [InlineData("https://steamcommunity.com/openid/login", true)]
    [InlineData("https://2.sdvn.org/me", false)]
    [InlineData("https://1.sdvn.org.evil.test/", false)]
    [InlineData("http://1.sdvn.org/me", false)]
    [InlineData("https://1.sdvn.org:444/me", false)]
    public void TenantNavigationIsScoped(string url, bool allowed)
    {
        var tenant = SdvnTenant.All[0];
        var source = new TelemetrySourceDefinition
        {
            Id = tenant.Id, DisplayName = tenant.DisplayName, ShortName = "SDVN", Kind = TelemetrySourceKind.IslePilot,
            BaseUri = tenant.BaseUri, LoginUri = tenant.LoginUri, CookieName = "islepilot_player"
        };
        Assert.Equal(allowed, LoginNavigationPolicy.IsAllowed(source, url));
    }

    [Fact]
    public void CompiledShellIncludesRealAsyncHandlersAndSharedLaunchPath()
    {
        foreach (var name in new[] { "GachaServer_Click", "OriginServer_Click", "SdvnServer_Click", "LaunchServerAsync", "OpenOverlaySessionAsync" })
        {
            var method = typeof(HomeWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.NotNull(method.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>());
        }
        using var resources = typeof(HomeWindow).Assembly.GetManifestResourceStream("IsleLiveMap.g.resources")!;
        using var reader = new System.Resources.ResourceReader(resources);
        Assert.Contains(reader.Cast<System.Collections.DictionaryEntry>(), entry => (string)entry.Key == "assets/sdvnicon.png");
    }

    // Run separately with the env var set: actual compiled WPF layout, no
    // login, capture, updater, settings changes, or mouse/keyboard simulation.
    [Fact]
    public async Task CaptureCompiledHomeAndTenantModalWhenRequested()
    {
        var output = Environment.GetEnvironmentVariable("ISLE_SERVER_UI_CAPTURE");
        if (string.IsNullOrWhiteSpace(output)) return;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(output);
                var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var home = new HomeWindow();
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(HomeWindow).GetMethod("ApplyProPresentation", flags)!.Invoke(home, [ProAccessSnapshot.SignedOut, false]);
                typeof(HomeWindow).GetField("_mapLaunchGateState", flags)!.SetValue(home, MapLaunchGateState.Available);
                typeof(HomeWindow).GetMethod("BuildHome", flags)!.Invoke(home, null);
                typeof(HomeWindow).GetMethod("UpdateNavigationVisuals", flags)!.Invoke(home, null);
                foreach (var width in new[] { 820, 960, 1200 })
                {
                    home.Width = width;
                    typeof(HomeWindow).GetMethod("UpdateReleaseRailLayout", flags)!.Invoke(home, null);
                    var root = (FrameworkElement)home.Content;
                    Render(root, width, 600, Path.Combine(output, $"home-{width}.png"));
                    var buttons = Children(root).OfType<Button>().Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Server")).ToArray();
                    Assert.Equal(3, buttons.Length);
                    Assert.Single(buttons, b => AutomationProperties.GetAutomationId(b) == "ServerSDVNButton");
                    foreach (var button in buttons)
                    {
                        Assert.True(button.ActualWidth >= 100);
                        Assert.NotEqual(Brushes.Transparent, button.Background);
                    }
                }
                // Exercise the actual compiled handlers without invoking login,
                // Npcap or network: the update gate must block all three.
                foreach (var handler in new[] { "GachaServer_Click", "OriginServer_Click", "SdvnServer_Click" })
                {
                    typeof(HomeWindow).GetField("_mapLaunchGateState", flags)!.SetValue(home, MapLaunchGateState.UpdateRequired);
                    typeof(HomeWindow).GetMethod(handler, flags)!.Invoke(home, [home, new RoutedEventArgs()]);
                    Assert.Equal(0, typeof(HomeWindow).GetField("_mapOpenStarted", flags)!.GetValue(home));
                    Assert.Contains("khởi động lại", (string)typeof(HomeWindow).GetField("_launchStatus", flags)!.GetValue(home)!);
                }
                var premium = ProAccessSnapshot.SignedOut with
                { SteamId64 = "76561198000000000", Entitlement = new ProEntitlement("pro", "active", null) };
                typeof(HomeWindow).GetMethod("ApplyProPresentation", flags)!.Invoke(home, [premium, false]);
                ((Panel)home.FindName("Workspace")).Children.Clear();
                typeof(HomeWindow).GetMethod("BuildHome", flags)!.Invoke(home, null);
                home.Width = 960;
                typeof(HomeWindow).GetMethod("UpdateReleaseRailLayout", flags)!.Invoke(home, null);
                Render((FrameworkElement)home.Content, 960, 600, Path.Combine(output, "home-pro.png"));

                typeof(HomeWindow).GetField("_mapOpenStarted", flags)!.SetValue(home, 1);
                foreach (var handler in new[] { "GachaServer_Click", "OriginServer_Click", "SdvnServer_Click" })
                    typeof(HomeWindow).GetMethod(handler, flags)!.Invoke(home, [home, new RoutedEventArgs()]);
                Assert.Equal(1, typeof(HomeWindow).GetField("_mapOpenStarted", flags)!.GetValue(home));
                typeof(HomeWindow).GetField("_mapOpenStarted", flags)!.SetValue(home, 0);
                var modal = new SdvnTenantWindow(new SdvnCredentialStore(Path.Combine(output, "empty-credentials")));
                var list = (ListBox)modal.FindName("TenantList");
                Assert.Equal(3, list.Items.Count);
                Assert.DoesNotContain(list.Items.Cast<SdvnTenantWindow.TenantRow>(), row => row.Tenant.Id == "sdvn-4");
                Render((FrameworkElement)modal.Content, 490, 580, Path.Combine(output, "sdvn-modal.png"));
                modal.Close(); home.Close();
                app.Team.DisposeAsync().AsTask().GetAwaiter().GetResult();
                done.SetResult();
            }
            catch (Exception e) { done.SetException(e); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static void Render(FrameworkElement root, int width, int height, string path)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
}
