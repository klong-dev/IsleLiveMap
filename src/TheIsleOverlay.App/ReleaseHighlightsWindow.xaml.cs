using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace TheIsleOverlay.App;

public partial class ReleaseHighlightsWindow : Window
{
    public const string ReleaseVersion = "1.4.0";
    public const string ProPreviewResourcePath = "Assets/ProMapPreview.png";

    public bool WantsProDetails { get; private set; }

    public ReleaseHighlightsWindow(string version)
    {
        InitializeComponent();
        var displayedVersion = string.IsNullOrWhiteSpace(version) ? ReleaseVersion : version.Trim();
        TitleVersionLabel.Text = $"   ISLE LIVE MAP · v{displayedVersion}";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            ReleaseContent.Opacity = 1d;
            ReleaseEntranceTransform.Y = 0d;
            EnterToolButton.Focus();
            return;
        }

        ReleaseContent.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(240)));
        ReleaseEntranceTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10d, 0d, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        RadarScanLine.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.72d, 0d, TimeSpan.FromMilliseconds(2_500))
            {
                BeginTime = TimeSpan.FromMilliseconds(320),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            });
        RadarScanTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(8d, 490d, TimeSpan.FromMilliseconds(2_500))
            {
                BeginTime = TimeSpan.FromMilliseconds(320),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            });
        EnterToolButton.Focus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowProButton_Click(object sender, RoutedEventArgs e)
    {
        WantsProDetails = true;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
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
