using System.Windows;

namespace TheIsleOverlay.App;

public partial class HomeReleaseNoteWindow : Window
{
    public HomeReleaseNoteWindow(string version, string title, string summary, IReadOnlyList<string> details)
    {
        InitializeComponent();
        VersionLabel.Text = version;
        TitleLabel.Text = title;
        SummaryLabel.Text = summary;
        DetailsList.ItemsSource = details;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
