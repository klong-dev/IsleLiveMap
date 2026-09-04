using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

public partial class ReleaseHighlightsWindow : Window
{
    public const string ReleaseVersion = "1.4.9";
    public const int PageCount = 5;

    private static readonly Brush ActiveMarkerBrush = new SolidColorBrush(Color.FromRgb(0xF1, 0xC7, 0x5B));
    private static readonly Brush ActiveDigitBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x04));
    private static readonly Brush InactiveMarkerBrush = new SolidColorBrush(Color.FromRgb(0x0B, 0x1B, 0x17));
    private static readonly Brush InactiveBorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x5B, 0x53));
    private static readonly Brush InactiveDigitBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x91, 0x88));

    private readonly bool _hasCurrentProAccess;
    private readonly ReleaseHighlightsPreferenceStore _preferenceStore;
    private Grid[] _pages = [];
    private Border[] _markers = [];
    private int _currentPage;

    public static Uri ProLandingPageUri => ProClientOptions.ProductionBaseUri;

    public ReleaseHighlightsWindow(
        string version,
        bool hasCurrentProAccess = false,
        ReleaseHighlightsPreferenceStore? preferenceStore = null)
    {
        _hasCurrentProAccess = hasCurrentProAccess;
        _preferenceStore = preferenceStore ?? new ReleaseHighlightsPreferenceStore();
        InitializeComponent();

        _pages = [PageOne, PageTwo, PageThree, PageFour, PageFive];
        _markers = [StepOneMarker, StepTwoMarker, StepThreeMarker, StepFourMarker, StepFiveMarker];
        var displayedVersion = string.IsNullOrWhiteSpace(version) ? ReleaseVersion : version.Trim();
        TitleVersionLabel.Text = $"   ISLE LIVE MAP · v{displayedVersion}";
        UpdatePage();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            ReleaseContent.Opacity = 1d;
            ReleaseEntranceTransform.Y = 0d;
            NextButton.Focus();
            return;
        }

        ReleaseContent.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(220)));
        ReleaseEntranceTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(10d, 0d, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        NextButton.Focus();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage <= 0)
        {
            return;
        }

        _currentPage--;
        UpdatePage();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage >= PageCount - 1)
        {
            return;
        }

        _currentPage++;
        UpdatePage();
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage != PageCount - 1)
        {
            return;
        }

        if (DoNotShowAgainCheckBox.IsChecked == true)
        {
            _preferenceStore.HideVersion(ReleaseVersion);
        }

        DialogResult = true;
        Close();
    }

    private void UpdatePage()
    {
        for (var index = 0; index < _pages.Length; index++)
        {
            var active = index == _currentPage;
            _pages[index].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            _markers[index].Background = active ? ActiveMarkerBrush : InactiveMarkerBrush;
            _markers[index].BorderBrush = active ? ActiveMarkerBrush : InactiveBorderBrush;
            if (_markers[index].Child is TextBlock digit)
            {
                digit.Foreground = active ? ActiveDigitBrush : InactiveDigitBrush;
            }
        }

        var isFirst = _currentPage == 0;
        var isLast = _currentPage == PageCount - 1;
        BackButton.Visibility = isFirst ? Visibility.Hidden : Visibility.Visible;
        NextButton.Visibility = isLast ? Visibility.Collapsed : Visibility.Visible;
        FinishButton.Visibility = isLast ? Visibility.Visible : Visibility.Collapsed;
        OpenProButton.Visibility = isLast && !_hasCurrentProAccess
            ? Visibility.Visible
            : Visibility.Collapsed;
        PageProgressLabel.Text = $"{_currentPage + 1:00} / {PageCount:00}";

        if (isLast)
        {
            FinishButton.Focus();
        }
        else
        {
            NextButton.Focus();
        }
    }

    private void ShowProButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProLandingPageUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Không thể mở trang Isle Live Map Pro.\n\n{exception.Message}",
                "Isle Live Map",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
