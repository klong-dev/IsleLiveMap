using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace TheIsleOverlay.App;

public partial class DonateWindow : Window
{
    private const string AccountNumber = "1029118580";
    public const int CloseDelaySeconds = 7;

    private readonly DispatcherTimer _closeTimer;
    private int _remainingCloseSeconds = CloseDelaySeconds;
    private bool _canClose;

    public DonateWindow()
    {
        InitializeComponent();
        _closeTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Normal,
            CloseTimer_Tick,
            Dispatcher);
    }

    private void CopyAccountButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(AccountNumber);
            CopyStateLabel.Text = "ĐÃ COPY · 1029118580";
            CopyStateLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(55, 212, 198));
        }
        catch
        {
            CopyStateLabel.Text = "Không copy được · STK 1029118580";
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateCloseCountdown();
        _closeTimer.Start();
    }

    private void CloseTimer_Tick(object? sender, EventArgs e)
    {
        _remainingCloseSeconds--;
        if (_remainingCloseSeconds <= 0)
        {
            _remainingCloseSeconds = 0;
            _canClose = true;
            _closeTimer.Stop();
            TopCloseButton.IsEnabled = true;
            EnterToolButton.IsEnabled = true;
        }

        UpdateCloseCountdown();
    }

    private void UpdateCloseCountdown()
    {
        EnterToolButton.Content = _canClose
            ? "ĐÓNG · VÀO TOOL  →"
            : $"ĐÓNG SAU {_remainingCloseSeconds}S  →";
        TopCloseButton.ToolTip = _canClose
            ? "Đóng lời mời ủng hộ"
            : $"Có thể đóng sau {_remainingCloseSeconds} giây";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_canClose)
        {
            Close();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_canClose)
            {
                Close();
            }

            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_canClose && Application.Current?.Dispatcher.HasShutdownStarted != true)
        {
            e.Cancel = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        _closeTimer.Tick -= CloseTimer_Tick;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
