using System.Windows;
using System.Windows.Input;

namespace TheIsleOverlay.App;

public partial class NpcapRequiredWindow : Window
{
    public NpcapRequiredWindow()
    {
        InitializeComponent();
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
