using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TheIsleOverlay.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class IslePilotLoginRegressionTests
{
    [Fact]
    public void ActiveHomeUsesCredentialRecoveryAndOnlyExplicitLocalMode()
    {
        var code = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "HomeWindow.xaml.cs"));
        var handler = code[code.IndexOf("private async void OpenMap_Click", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("private async Task<bool> OpenProOnlyOverlayAsync", StringComparison.Ordinal)];
        Assert.Contains("new IslePilotOverlayLoginFlow(authHttp, store).ResolveAsync", handler);
        Assert.Contains("localOnlyRequested = login.LocalOnlyRequested", handler);
        Assert.Contains("if (localOnlyRequested)", handler);
        Assert.DoesNotContain("if (proPresentation.HasCurrentProAccess)", handler);
    }

    [Fact]
    public async Task CompiledLoginRequiresExplicitOptOutAndExplainsMissingStats()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var free = new IslePilotSteamLoginWindow();
                Assert.False(free.AllowLocalOnly);
                Assert.False(free.LocalOnlyRequested);
                typeof(IslePilotSteamLoginWindow).GetMethod("LocalOnlyButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(free, [free, new RoutedEventArgs()]);
                Assert.False(free.LocalOnlyRequested);
                free.Close();

                var pro = new IslePilotSteamLoginWindow { AllowLocalOnly = true };
                var button = (Button)pro.FindName("LocalOnlyButton");
                Assert.Equal(Visibility.Visible, button.Visibility);
                Assert.Contains("KHÔNG STATS", button.Content.ToString());
                Assert.False(pro.LocalOnlyRequested);
                var capture = Environment.GetEnvironmentVariable("ISLE_LOGIN_UI_CAPTURE");
                if (!string.IsNullOrWhiteSpace(capture))
                {
                    Directory.CreateDirectory(capture);
                    var content = (FrameworkElement)pro.Content;
                    content.Measure(new Size(820, 620));
                    content.Arrange(new Rect(0, 0, 820, 620));
                    content.UpdateLayout();
                    Assert.True(button.ActualWidth > 140);
                    var bitmap = new RenderTargetBitmap(820, 620, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(capture, "islepilot-login-pro.png"));
                    encoder.Save(file);
                }
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(pro.LocalOnlyRequested);
                Assert.Null(pro.Credentials);
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
