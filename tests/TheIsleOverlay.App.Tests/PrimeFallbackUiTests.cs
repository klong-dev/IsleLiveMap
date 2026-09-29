using System.Reflection;
using System.Windows.Controls;
using TheIsleOverlay.Core;
namespace TheIsleOverlay.App.Tests;

[Collection(WpfApplicationCollection.Name)]
public sealed class PrimeFallbackUiTests
{
    [Fact]
    public async Task PrimeRowsRemainDuringFallbackAndRecoverWithoutDuplicates()
    {
        // WPF allows only one Application per process. Run this compiled-window
        // check in its own test process, like the existing UI capture tests.
        if (Environment.GetEnvironmentVariable("ISLE_PRIME_UI_CHECK") != "1") return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                var window = new MainWindow();
                var render = typeof(MainWindow).GetMethod("RenderPrimeMissions", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var prime = new PrimeTelemetry { Done = 1, Required = 2,
                    Quests = [new() { Name = "Travel", Done = true }, new() { Name = "Eat", Done = false }] };
                render.Invoke(window, [prime, false]);
                var list = (ItemsControl)window.FindName("MissionList");
                var label = (TextBlock)window.FindName("MissionProgressLabel");
                Assert.Equal(2, list.Items.Count);
                render.Invoke(window, [prime, true]);
                Assert.Equal(2, list.Items.Count);
                Assert.Contains("ISLEPILOT", label.Text);
                render.Invoke(window, [prime, false]);
                Assert.Equal(2, list.Items.Count);
                Assert.Equal("1 / 2", label.Text);
                window.Close();
                app.Team.DisposeAsync().AsTask().GetAwaiter().GetResult();
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
