using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public partial class TeamCapacityWindow : Window
{
    private readonly TeamAccessTier _tier;
    public int? SelectedCapacity { get; private set; }
    public TeamCapacityWindow(TeamAccessTier tier)
    {
        _tier = tier; InitializeComponent();
        TierLabel.Text = tier == TeamAccessTier.Pro ? "PRO · TỐI ĐA 21 NGƯỜI" : "FREE · TỐI ĐA 7 NGƯỜI";
        MessageLabel.Text = "Chọn quy mô để tiếp tục nhập tên.";
        foreach (var size in TeamRoomLimits.Choices)
        {
            var allowed = TeamRoomLimits.CanCreate(tier, size);
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = $"{size} người", FontSize = 23, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
            label.Children.Add(new TextBlock { Text = size <= 7 ? "MIỄN PHÍ" : allowed ? "PRO" : "KHÓA · CẦN PRO", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            // Remain clickable to explain locked choices; click never advances.
            var button = new Button { Content = label, Style = (Style)FindResource("CapacityChoice"), Opacity = allowed ? 1 : .5 };
            AutomationProperties.SetAutomationId(button, $"RoomCapacity{size}");
            AutomationProperties.SetName(button, $"Phòng {size} người" + (allowed ? "" : " · Cần Pro"));
            AutomationProperties.SetHelpText(button, allowed ? "Chọn quy mô này" : ProRequiredMessage(size));
            button.Click += (_, _) => { if (TrySelect(size)) DialogResult = true; };
            ChoicesPanel.Children.Add(button);
        }
    }
    internal bool TrySelect(int size)
    {
        if (!TeamRoomLimits.CanCreate(_tier, size))
        { MessageLabel.Text = ProRequiredMessage(size); return false; }
        SelectedCapacity = size; return true;
    }
    public static string ProRequiredMessage(int size) => $"Bạn cần phải đăng ký Pro để tạo phòng {size} người.";
}
