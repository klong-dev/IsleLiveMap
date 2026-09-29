using System.Windows;
using System.Windows.Input;

namespace TheIsleOverlay.App;

public partial class UpdateReadyWindow : Window
{
    public bool ApplyRequested { get; private set; }

    public UpdateReadyWindow(string? version)
    {
        InitializeComponent();
        VersionLabel.Text = string.IsNullOrWhiteSpace(version) ? "BẢN MỚI" : $"v{version}";
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyRequested = true;
        DialogResult = true;
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
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
