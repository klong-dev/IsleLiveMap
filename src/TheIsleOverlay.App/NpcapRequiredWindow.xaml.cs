using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TheIsleOverlay.App;

public partial class NpcapRequiredWindow : Window
{
    private static readonly Brush ActiveBackground = MakeBrush("#193D37");
    private static readonly Brush ActiveBorder = MakeBrush("#37D4C6");
    private static readonly Brush ActiveForeground = MakeBrush("#DFFFF9");
    private static readonly Brush CompleteBackground = MakeBrush("#15312D");
    private static readonly Brush CompleteBorder = MakeBrush("#37645C");
    private static readonly Brush CompleteForeground = MakeBrush("#8FC4B9");
    private static readonly Brush IdleBackground = MakeBrush("#101F1D");
    private static readonly Brush IdleBorder = MakeBrush("#2A4540");
    private static readonly Brush IdleForeground = MakeBrush("#607B74");
    private static readonly Brush ErrorBrush = MakeBrush("#FF8A7A");
    private static readonly Brush WarningBrush = MakeBrush("#FFE18A");

    private readonly INpcapSetupService _setupService;
    private CancellationTokenSource? _setupCancellation;
    private bool _setupInProgress;
    private bool _canCancel;
    private bool _closeAfterCancellation;

    public NpcapRequiredWindow()
        : this(new NpcapSetupService())
    {
    }

    internal NpcapRequiredWindow(INpcapSetupService setupService)
    {
        _setupService = setupService;
        InitializeComponent();
        ApplyStage(NpcapSetupStage.Downloading, 0);
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_setupInProgress)
        {
            return;
        }

        _setupCancellation = new CancellationTokenSource();
        _setupInProgress = true;
        _canCancel = true;
        _closeAfterCancellation = false;
        InstallButton.IsEnabled = false;
        LaterButton.Content = "HỦY";
        SetupStatusDot.Fill = ActiveBorder;
        SetupStatusLabel.Foreground = ActiveForeground;

        try
        {
            var progress = new Progress<NpcapSetupProgress>(ApplyProgress);
            var result = await _setupService.InstallAsync(progress, _setupCancellation.Token);
            _setupInProgress = false;
            _canCancel = false;

            if (result.Outcome == NpcapSetupOutcome.Ready)
            {
                ApplyStage(NpcapSetupStage.Checking, 100);
                SetupStatusDot.Fill = ActiveBorder;
                SetupStatusLabel.Foreground = ActiveForeground;
                SetupStatusLabel.Text = "NPCAP ĐÃ SẴN SÀNG";
                SetupDetailLabel.Text = result.Message;
                SetupPercentLabel.Text = "XONG";
                LaterButton.IsEnabled = false;
                TopCloseButton.IsEnabled = false;
                await Task.Delay(650);
                DialogResult = true;
                return;
            }

            if (_closeAfterCancellation && result.Outcome == NpcapSetupOutcome.Cancelled)
            {
                DialogResult = false;
                return;
            }

            ShowResult(result);
        }
        catch (Exception)
        {
            _setupInProgress = false;
            _canCancel = false;
            ShowResult(new NpcapSetupResult(
                NpcapSetupOutcome.Failed,
                "Không thể khởi chạy bộ cài Npcap. Hãy thử lại."));
        }
        finally
        {
            _setupCancellation?.Dispose();
            _setupCancellation = null;
        }
    }

    private void ApplyProgress(NpcapSetupProgress progress)
    {
        ApplyStage(progress.Stage, progress.Percent);
        SetupStatusLabel.Text = progress.Stage switch
        {
            NpcapSetupStage.Downloading => "ĐANG TẢI TỪ NPCAP.COM",
            NpcapSetupStage.Verifying => "ĐANG XÁC MINH FILE CHÍNH THỨC",
            NpcapSetupStage.Installing => "ĐANG CHỜ CỬA SỔ NPCAP",
            NpcapSetupStage.Checking => "ĐANG KIỂM TRA SAU CÀI ĐẶT",
            _ => "ĐANG THIẾT LẬP NPCAP"
        };
        SetupDetailLabel.Text = progress.Message;
        SetupPercentLabel.Text = progress.Percent is int percent ? $"{percent}%" : string.Empty;
        SetupStatusDot.Fill = ActiveBorder;
        SetupStatusLabel.Foreground = ActiveForeground;

        _canCancel = progress.Stage is NpcapSetupStage.Downloading or NpcapSetupStage.Verifying;
        LaterButton.IsEnabled = _canCancel;
        TopCloseButton.IsEnabled = _canCancel;
        LaterButton.Content = _canCancel ? "HỦY" : "ĐANG CÀI…";
    }

    private void ApplyStage(NpcapSetupStage activeStage, int? percent)
    {
        var activeIndex = (int)activeStage;
        var steps = new (Border Border, TextBlock Label)[]
        {
            (DownloadStep, DownloadStepLabel),
            (VerifyStep, VerifyStepLabel),
            (InstallStep, InstallStepLabel),
            (CheckStep, CheckStepLabel)
        };

        for (var index = 0; index < steps.Length; index++)
        {
            var state = index.CompareTo(activeIndex);
            steps[index].Border.Background = state < 0 ? CompleteBackground : state == 0 ? ActiveBackground : IdleBackground;
            steps[index].Border.BorderBrush = state < 0 ? CompleteBorder : state == 0 ? ActiveBorder : IdleBorder;
            steps[index].Label.Foreground = state < 0 ? CompleteForeground : state == 0 ? ActiveForeground : IdleForeground;
        }

        SetupProgressBar.IsIndeterminate = false;
        SetupProgressBar.Value = activeStage switch
        {
            NpcapSetupStage.Downloading => (percent ?? 0) * 0.25,
            NpcapSetupStage.Verifying => 40,
            NpcapSetupStage.Installing => 68,
            NpcapSetupStage.Checking => percent == 100 ? 100 : 90,
            _ => 0
        };
    }

    private void ShowResult(NpcapSetupResult result)
    {
        InstallButton.IsEnabled = true;
        InstallButton.Content = "THỬ CÀI LẠI  →";
        LaterButton.IsEnabled = true;
        LaterButton.Content = "ĐỂ SAU";
        TopCloseButton.IsEnabled = true;
        SetupProgressBar.IsIndeterminate = false;
        SetupProgressBar.Value = 0;
        SetupPercentLabel.Text = string.Empty;
        SetupDetailLabel.Text = result.Message;

        if (result.Outcome == NpcapSetupOutcome.RebootRequired)
        {
            SetupStatusDot.Fill = WarningBrush;
            SetupStatusLabel.Foreground = WarningBrush;
            SetupStatusLabel.Text = "CẦN KHỞI ĐỘNG LẠI WINDOWS";
            return;
        }

        SetupStatusDot.Fill = result.Outcome == NpcapSetupOutcome.Cancelled
            ? WarningBrush
            : ErrorBrush;
        SetupStatusLabel.Foreground = SetupStatusDot.Fill;
        SetupStatusLabel.Text = result.Outcome == NpcapSetupOutcome.Cancelled
            ? "CHƯA CÀI NPCAP"
            : "CÀI ĐẶT CHƯA THÀNH CÔNG";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_setupInProgress)
        {
            if (_canCancel)
            {
                _closeAfterCancellation = true;
                _canCancel = false;
                LaterButton.IsEnabled = false;
                TopCloseButton.IsEnabled = false;
                SetupStatusLabel.Text = "ĐANG HỦY TẢI…";
                _setupCancellation?.Cancel();
            }

            return;
        }

        DialogResult = false;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_setupInProgress)
        {
            return;
        }

        e.Cancel = true;
        if (_canCancel)
        {
            _closeAfterCancellation = true;
            _canCancel = false;
            _setupCancellation?.Cancel();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_setupInProgress)
        {
            DragMove();
        }
    }

    private static SolidColorBrush MakeBrush(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));
}
