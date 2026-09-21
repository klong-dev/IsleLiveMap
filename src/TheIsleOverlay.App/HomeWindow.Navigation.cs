using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private readonly Dictionary<string, Button> _homeNavigationButtons = new(StringComparer.Ordinal);
    private readonly GitHubReleaseNotesService _releaseNotesService = new();
    private readonly Dictionary<string, HomeReleaseNote> _releaseNotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly MapLayerPreferencesStore _homeLayerPreferencesStore = new();
    private MapLayerPreferences _homeLayerPreferences = new();

    private void InitializeHomeNavigation()
    {
        _homeNavigationButtons.Clear();
        foreach (var button in new[] { HomeTabButton, InfoTabButton, LocalizationTabButton, ShortcutTabButton, SettingsTabButton, GuideTabButton })
        {
            if (button.Tag is string key)
            {
                _homeNavigationButtons[key] = button;
            }
        }

        SelectHomeTab("home");
        LoadHomeLayerPreferences();
    }

    private void HomeTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
        {
            SelectHomeTab(key);
        }
    }

    private void SelectHomeTab(string key)
    {
        HomeContentScrollViewer.Visibility = key == "home" ? Visibility.Visible : Visibility.Collapsed;
        InfoTabContent.Visibility = key == "info" ? Visibility.Visible : Visibility.Collapsed;
        LocalizationTabContent.Visibility = key == "localization" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutTabContent.Visibility = key == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        SettingsTabContent.Visibility = key == "settings" ? Visibility.Visible : Visibility.Collapsed;
        GuideTabContent.Visibility = key == "guide" ? Visibility.Visible : Visibility.Collapsed;

        foreach (var pair in _homeNavigationButtons)
        {
            pair.Value.Background = pair.Key == key
                ? (Brush)FindResource("HomeNavSelected")
                : Brushes.Transparent;
            pair.Value.Foreground = pair.Key == key
                ? (Brush)FindResource("HomeBone")
                : (Brush)FindResource("HomeMuted");
        }
    }

    private void ProNavButton_Click(object sender, RoutedEventArgs e) => ProAccessButton_Click(sender, e);

    private void LoadHomeLayerPreferences()
    {
        try
        {
            var layers = GatewayStaticMapLayerCatalog.LoadBundled();
            _homeLayerPreferences = _homeLayerPreferencesStore.Load(layers.Defaults);
            SetHomeLayerToggleState(HomeWaterToggle, _homeLayerPreferences.Water);
            SetHomeLayerToggleState(HomeZoneToggle, _homeLayerPreferences.Migration || _homeLayerPreferences.Patrol || _homeLayerPreferences.Sanctuary);
            SetHomeLayerToggleState(HomeRoadToggle, _homeLayerPreferences.Roads);
            SetHomeLayerToggleState(HomeResourceToggle, _homeLayerPreferences.Animals || _homeLayerPreferences.Plants || _homeLayerPreferences.Earth);
        }
        catch (Exception)
        {
            // Keep Home usable if a local map-layer file is unavailable.
        }
    }

    private static void SetHomeLayerToggleState(CheckBox toggle, bool value) => toggle.IsChecked = value;

    private void HomeLayerToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string key, IsChecked: bool enabled })
        {
            return;
        }

        switch (key)
        {
            case "water":
                _homeLayerPreferences.Water = enabled;
                break;
            case "zone":
                _homeLayerPreferences.Migration = enabled;
                _homeLayerPreferences.Patrol = enabled;
                _homeLayerPreferences.Sanctuary = enabled;
                break;
            case "road":
                _homeLayerPreferences.Roads = enabled;
                break;
            case "resource":
                _homeLayerPreferences.Animals = enabled;
                _homeLayerPreferences.Plants = enabled;
                _homeLayerPreferences.Earth = enabled;
                break;
        }

        _homeLayerPreferences.Version = MapLayerPreferences.CurrentVersion;
        _homeLayerPreferencesStore.TrySave(_homeLayerPreferences, out _);
        HomeSettingsStatusLabel.Text = "Đã lưu thiết lập map trên thiết bị này.";
    }

    private async Task LoadReleaseNotesAsync()
    {
        try
        {
            var notes = await _releaseNotesService.LoadAsync(_shutdown.Token);
            if (notes.Count > 0)
            {
                _releaseNotes.Clear();
                foreach (var note in notes)
                {
                    _releaseNotes[note.Version] = note;
                }

                ApplyReleaseNoteCard(ReleaseNoteCard1, ReleaseNoteVersion1, ReleaseNoteSummary1, notes.ElementAtOrDefault(0));
                ApplyReleaseNoteCard(ReleaseNoteCard2, ReleaseNoteVersion2, ReleaseNoteSummary2, notes.ElementAtOrDefault(1));
                ApplyReleaseNoteCard(ReleaseNoteCard3, ReleaseNoteVersion3, ReleaseNoteSummary3, notes.ElementAtOrDefault(2));
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch
        {
            // The offline cards are useful even when GitHub is unavailable.
        }
    }

    private void ApplyReleaseNoteCard(Button card, TextBlock version, TextBlock summary, HomeReleaseNote? note)
    {
        card.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        if (note is null)
        {
            return;
        }

        card.Tag = note.Version;
        version.Text = $"v{note.Version}";
        summary.Text = note.Summary;
    }

    private void ReleaseNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string version })
        {
            return;
        }

        var note = _releaseNotes.TryGetValue(version, out var loadedNote)
            ? loadedNote
            : version switch
        {
            "2.2.3" => new HomeReleaseNote("2.2.3", "Bản đồ nước uống được", "Bản đồ mới dùng lớp nước được tô lại, bật sẵn khi mở app.", ["Lớp BẢN ĐỒ dễ nhìn hơn", "Minimap gọn hơn với la bàn 4 hướng", "Không nhận telemetry cũ khi game chưa chạy"]),
            "2.2.2" => new HomeReleaseNote("2.2.2", "Theo dõi team ổn định hơn", "Cải thiện relay nhóm và trạng thái đồng đội trong overlay.", ["Đồng bộ vị trí mượt hơn", "Hiển thị trạng thái kết nối rõ ràng"]),
            _ => new HomeReleaseNote("2.2.1", "Tinh chỉnh trải nghiệm", "Các thay đổi nhỏ giúp mở map nhanh và dễ kiểm soát hơn.", ["Tối ưu luồng mở map", "Sửa lỗi giao diện"])
        };

        new HomeReleaseNoteWindow($"v{note.Version}", note.Title, note.Summary, note.Details) { Owner = this }.ShowDialog();
    }
}
