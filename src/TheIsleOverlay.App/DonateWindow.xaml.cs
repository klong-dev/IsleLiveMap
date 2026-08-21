using System.Windows;
using System.Windows.Input;

namespace TheIsleOverlay.App;

public partial class DonateWindow : Window
{
    private const string AccountNumber = "1029118580";

    public DonateWindow()
    {
        InitializeComponent();
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
