using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace TheIsleOverlay.App;

public partial class ReleaseHighlightsWindow : Window
{
    public const string ReleaseVersion = "1.3.0";

    public ReleaseHighlightsWindow(string version)
    {
        InitializeComponent();
        var displayedVersion = string.IsNullOrWhiteSpace(version) ? ReleaseVersion : version.Trim();
        TitleVersionLabel.Text = $"   ISLE LIVE MAP · v{displayedVersion}";
        HeroVersionLabel.Text = displayedVersion;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ReleaseContent.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(240)));
        ReleaseEntranceTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10d, 0d, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        EnterToolButton.Focus();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

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
