using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App.Tests;

public sealed class TeamCapacityWindowTests
{
    [Theory]
    [InlineData(TeamAccessTier.Free, 3, true)] [InlineData(TeamAccessTier.Free, 7, true)]
    [InlineData(TeamAccessTier.Free, 10, false)] [InlineData(TeamAccessTier.Free, 21, false)]
    [InlineData(TeamAccessTier.Pro, 3, true)] [InlineData(TeamAccessTier.Pro, 7, true)]
    [InlineData(TeamAccessTier.Pro, 10, true)] [InlineData(TeamAccessTier.Pro, 21, true)]
    public async Task ChoiceHonorsCreatorTier(TeamAccessTier tier, int size, bool allowed)
    {
        await Sta(() =>
        {
            var window = new TeamCapacityWindow(tier);
            Assert.Equal(allowed, window.TrySelect(size));
            Assert.Equal(allowed ? size : (int?)null, window.SelectedCapacity);
            if (!allowed) Assert.Equal(TeamCapacityWindow.ProRequiredMessage(size), ((TextBlock)window.FindName("MessageLabel")).Text);
            window.Close();
        });
    }

    [Fact]
    public async Task LockedChoiceExplainsProAndDoesNotAdvance()
    {
        await Sta(() =>
        {
            var window = new TeamCapacityWindow(TeamAccessTier.Free);
            var panel = (UniformGrid)window.FindName("ChoicesPanel");
            Assert.Equal(4, panel.Children.Count);
            var locked = panel.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "RoomCapacity21");
            Assert.True(locked.Opacity < 1);
            locked.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Null(window.SelectedCapacity);
            Assert.Contains("21 người", ((TextBlock)window.FindName("MessageLabel")).Text);
            var output = Environment.GetEnvironmentVariable("ISLE_TEAM_UI_CAPTURE");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                Render(window, Path.Combine(output, "team-capacity-free.png"));
                var pro = new TeamCapacityWindow(TeamAccessTier.Pro);
                Render(pro, Path.Combine(output, "team-capacity-pro.png"));
                pro.Close();
            }
            window.Close();
        });
    }

    [Fact]
    public async Task ModalSelectionReturnsCapacityBeforeNameStep()
    {
        await Sta(() =>
        {
            var window = new TeamCapacityWindow(TeamAccessTier.Pro);
            window.Loaded += (_, _) =>
            {
                var choices = (UniformGrid)window.FindName("ChoicesPanel");
                choices.Children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "RoomCapacity10")
                    .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            };
            Assert.True(window.ShowDialog());
            Assert.Equal(10, window.SelectedCapacity);
        });
    }

    private static async Task Sta(Action action)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completed.SetResult(); } catch (Exception e) { completed.SetException(e); } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static void Render(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(490, 450)); root.Arrange(new Rect(0, 0, 490, 450)); root.UpdateLayout();
        var bmp = new RenderTargetBitmap(490, 450, 96, 96, PixelFormats.Pbgra32); bmp.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
