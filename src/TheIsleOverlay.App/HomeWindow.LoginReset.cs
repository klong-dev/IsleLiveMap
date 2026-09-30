using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private bool _clearingServerLogin;
    private TextBlock? _loginResetStatus;
    private Button? _loginResetButton;
    private string _loginResetMessage = "Xóa phiên website trong app (gồm cookie ORIGIN và Steam SSO); không xóa giấy phép Pro hoặc đăng xuất Steam/game.";

    private async void ClearServerLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_clearingServerLogin) return;
        if (_mapOpenStarted != 0)
        {
            UpdateLoginResetStatus("Đang mở map; chờ thao tác kết nối kết thúc rồi thử lại.");
            return;
        }
        _clearingServerLogin = true;
        if (_loginResetButton is not null) _loginResetButton.IsEnabled = false;
        RefreshLaunchButtons();
        UpdateLoginResetStatus("ĐANG XÓA PHIÊN ĐĂNG NHẬP…");
        try
        {
            var result = await new ServerLoginResetService().ClearAsync(new WindowInteropHelper(this).Handle, _shutdown.Token);
            _snapshots.Clear();
            UpdateLoginResetStatus(result.Message);
            if (!_shutdown.IsCancellationRequested)
                MessageBox.Show(this, result.Message, result.Success ? "Đã xóa đăng nhập" : "Xóa đăng nhập chưa hoàn tất",
                    MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        { UpdateLoginResetStatus("Đã hủy thao tác; chưa xác nhận xóa hết phiên đăng nhập."); }
        catch
        {
            const string message = "Không hoàn tất xóa phiên đăng nhập. Hãy thử lại; phiên cũ có thể vẫn còn.";
            UpdateLoginResetStatus(message);
            if (!_shutdown.IsCancellationRequested) MessageBox.Show(this, message, "Xóa đăng nhập thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _clearingServerLogin = false;
            if (_loginResetButton is not null) _loginResetButton.IsEnabled = true;
            RefreshLaunchButtons();
        }
    }

    private void UpdateLoginResetStatus(string message)
    {
        _loginResetMessage = message;
        if (_loginResetStatus is not null) _loginResetStatus.Text = message;
        SetStatus(message);
    }
}
