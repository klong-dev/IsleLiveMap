using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

public partial class ProPromotionWindow : Window
{
    public static Uri ProLandingPageUri => ProClientOptions.ProductionBaseUri;

    public ProPromotionWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            PromotionContent.Opacity = 1d;
            PromotionEntranceTransform.Y = 0d;
            ActivateProButton.Focus();
            return;
        }

        PromotionContent.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(220)));
        PromotionEntranceTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10d, 0d, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        ActivateProButton.Focus();
    }

    private void ActivateProButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ProLandingPageUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Không thể mở trang kích hoạt Pro.\n\n{exception.Message}",
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
