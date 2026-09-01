using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class MainWindow
{
    private readonly PrimeQuestCompletionTracker _primeQuestCompletionTracker = new();
    private readonly Queue<string> _missionToastQueue = new();
    private bool _missionsVisible = true;
    private bool _hasMissions;
    private bool _missionToastPumpRunning;

    private void RenderPrimeMissions(PrimeTelemetry? prime)
    {
        var quests = prime?.Quests?
            .Where(quest => !string.IsNullOrWhiteSpace(quest.Name))
            .ToArray() ?? [];

        _hasMissions = quests.Length > 0;
        RefreshOptionalWidgetVisibility();
        MissionList.ItemsSource = quests
            .Select(quest => new MissionRowViewModel
            {
                Name = PrimeQuestVietnamese.Translate(quest.Name),
                StateGlyph = quest.Done == true ? "✓" : "◇",
                StateBrush = quest.Done == true ? OnlineBrush : WaitingBrush,
                TextBrush = quest.Done == true ? BrushFrom("#8FA8A0") : BrushFrom("#E3EEE9")
            })
            .ToArray();

        var done = prime?.Done ?? quests.Count(quest => quest.Done == true);
        var required = prime?.Required ?? quests.Length;
        MissionProgressLabel.Text = $"{Math.Max(0, done)} / {Math.Max(0, required)}";

        foreach (var completed in _primeQuestCompletionTracker.Capture(quests))
        {
            EnqueueMissionToast(PrimeQuestVietnamese.Translate(completed.Name));
        }
    }

    private void ClearPrimeMissions()
    {
        _hasMissions = false;
        RefreshOptionalWidgetVisibility();
        MissionList.ItemsSource = null;
        MissionProgressLabel.Text = "0 / 0";
        _primeQuestCompletionTracker.Reset();
    }

    private void ToggleMissions()
    {
        _missionsVisible = !_missionsVisible;
        RefreshOptionalWidgetVisibility();
        RefreshWindowSizeToContent();
        KeepOverlayVisible();
        PositionMap();
    }

    private void ToggleHud()
    {
        _hudVisible = !_hudVisible;
        if (!_hudVisible && !_clickThrough)
        {
            SetClickThrough(true);
        }

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation
            {
                To = _hudVisible ? 1d : 0d,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            },
            HandoffBehavior.SnapshotAndReplace);
        PositionMap();
    }

    private void EnqueueMissionToast(string missionName)
    {
        _missionToastQueue.Enqueue(missionName);
        if (!_missionToastPumpRunning)
        {
            _ = ShowQueuedMissionToastsAsync();
        }
    }

    private async Task ShowQueuedMissionToastsAsync()
    {
        _missionToastPumpRunning = true;
        try
        {
            while (_missionToastQueue.TryDequeue(out var missionName))
            {
                MissionToastLabel.Text = missionName;
                MissionToast.Visibility = Visibility.Visible;
                MissionToast.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(170)));
                MissionToastTransform.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty,
                    new DoubleAnimation(-8d, 0d, TimeSpan.FromMilliseconds(220))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    });

                await Task.Delay(TimeSpan.FromSeconds(2.5), _shutdown.Token);
                MissionToast.BeginAnimation(
                    OpacityProperty,
                    new DoubleAnimation(1d, 0d, TimeSpan.FromMilliseconds(220)));
                MissionToastTransform.BeginAnimation(
                    System.Windows.Media.TranslateTransform.YProperty,
                    new DoubleAnimation(0d, -5d, TimeSpan.FromMilliseconds(220)));
                await Task.Delay(TimeSpan.FromMilliseconds(240), _shutdown.Token);
                MissionToast.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            MissionToast.BeginAnimation(OpacityProperty, null);
            MissionToastTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            MissionToast.Visibility = Visibility.Collapsed;
            _missionToastPumpRunning = false;
        }
    }

    public sealed record MissionRowViewModel
    {
        public required string Name { get; init; }
        public required string StateGlyph { get; init; }
        public required Brush StateBrush { get; init; }
        public required Brush TextBrush { get; init; }
    }
}
