using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TheIsleOverlay.App;

public partial class KLongServicesAdWindow : Window
{
    public static readonly TimeSpan RequiredDisplayDuration = TimeSpan.FromSeconds(5);

    private readonly MandatoryModalDelay _closeDelay = new(RequiredDisplayDuration);
    private readonly ZaloChannelInvitePreferenceStore _preferenceStore;
    private readonly Stopwatch _displayClock = new();
    private readonly DispatcherTimer _countdownTimer;
    private bool _allowClose;

    public KLongServicesAdWindow()
        : this(new ZaloChannelInvitePreferenceStore())
    {
    }

    internal KLongServicesAdWindow(ZaloChannelInvitePreferenceStore preferenceStore)
    {
        _preferenceStore = preferenceStore ?? throw new ArgumentNullException(nameof(preferenceStore));
        InitializeComponent();
        _countdownTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            CountdownTimer_Tick,
            Dispatcher);
        _countdownTimer.Stop();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _displayClock.Restart();
        _countdownTimer.Start();
        UpdateCountdown();

        if (!SystemParameters.ClientAreaAnimation)
        {
            AdContent.Opacity = 1d;
            AdEntranceTransform.Y = 0d;
            return;
        }

        AdContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(220)));
        AdEntranceTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(12d, 0d, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e) => UpdateCountdown();

    private void UpdateCountdown()
    {
        var elapsed = _displayClock.Elapsed;
        var remaining = _closeDelay.Remaining(elapsed);
        CountdownProgress.Value = _closeDelay.Progress(elapsed) * 100d;

        if (_closeDelay.CanClose(elapsed))
        {
            _allowClose = true;
            _countdownTimer.Stop();
            CountdownLabel.Text = "CHỌN CÁCH ĐÓNG THÔNG BÁO";
            NeverShowAgainButton.IsEnabled = true;
            CloseButton.Content = "ĐÓNG";
            CloseButton.IsEnabled = true;
            CloseButton.Focus();
            return;
        }

        var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
        CountdownLabel.Text = $"CÓ THỂ ĐÓNG SAU {seconds} GIÂY";
        CloseButton.Content = $"ĐÓNG SAU {seconds} GIÂY";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && !Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _countdownTimer.Stop();
        _displayClock.Stop();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_allowClose && (e.Key == Key.Escape || e.SystemKey == Key.F4))
        {
            e.Handled = true;
        }
    }

    private void NeverShowAgainButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowClose)
        {
            return;
        }

        try
        {
            _preferenceStore.HidePermanently();
        }
        catch (IOException)
        {
            // Closing remains available if Windows temporarily blocks the settings file.
        }
        catch (UnauthorizedAccessException)
        {
            // The modal must not trap the user when the profile is read-only.
        }

        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_allowClose)
        {
            Close();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
