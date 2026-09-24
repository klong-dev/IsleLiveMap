using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class SdvnTenantWindow : Window
{
    private readonly SdvnCredentialStore _store;
    private readonly CancellationTokenSource _stop = new();
    private bool _busy;
    public SdvnTenant? SelectedTenant { get; private set; }
    public string? Cookie { get; private set; }
    public SdvnTenantWindow(SdvnCredentialStore store)
    {
        _store = store; InitializeComponent();
        TenantList.ItemsSource = SdvnTenant.Available.Select(tenant => new TenantRow(tenant)).ToArray();
        var last = store.LastTenant;
        TenantList.SelectedItem = TenantList.Items.Cast<TenantRow>().FirstOrDefault(row => row.Tenant == last)
            ?? TenantList.Items.Cast<TenantRow>().First();
        LastTenantLabel.Text = last is null ? "MỘT NÚT · NHIỀU SERVER" : $"Gần nhất: {last.DisplayName}";
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        try
        {
            foreach (var row in TenantList.Items.Cast<TenantRow>())
            {
                var saved = await _store.LoadAsync(row.Tenant, _stop.Token);
                if (!_busy) row.Status = saved is null ? "CHƯA ĐĂNG NHẬP" : "ĐÃ LƯU PHIÊN · CHỜ XÁC MINH";
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch { StatusLabel.Text = "Không đọc được phiên đã lưu. Bạn có thể đăng nhập lại."; }
    }
    private void TenantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusLabel is not null && TenantList.SelectedItem is TenantRow row)
            StatusLabel.Text = $"Sẽ kết nối {row.Tenant.DisplayName}.";
    }
    private void ChangeTenant_Click(object sender, RoutedEventArgs e)
    { TenantList.SelectedIndex = -1; TenantList.Focus(); StatusLabel.Text = "Chọn server SDVN khác trong danh sách phía trên."; }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || TenantList.SelectedItem is not TenantRow row) return;
        _busy = true; ConnectButton.IsEnabled = TenantList.IsEnabled = ChangeTenantButton.IsEnabled = false;
        ConnectButton.Content = "ĐANG KIỂM TRA…";
        try
        {
            row.Status = "ĐANG KIỂM TRA";
            var cookie = await _store.LoadAsync(row.Tenant, _stop.Token);
            _stop.Token.ThrowIfCancellationRequested();
            var verified = false;
            if (cookie is not null)
            {
                var state = await ValidateAsync(row.Tenant, cookie, _stop.Token);
                if (state == LoginSessionValidationState.Invalid)
                { _store.Clear(row.Tenant); cookie = null; row.Status = "PHIÊN HẾT HẠN"; }
                else if (state == LoginSessionValidationState.Unavailable)
                { row.Status = "WEBSITE PHẢN HỒI CHẬM"; StatusLabel.Text = "Chưa xác minh được; phiên đã lưu được giữ nguyên. Hãy thử lại."; return; }
                else verified = true;
            }
            _stop.Token.ThrowIfCancellationRequested();
            if (cookie is null)
            {
                var source = new TelemetrySourceDefinition
                {
                    Id = row.Tenant.Id, DisplayName = row.Tenant.DisplayName, ShortName = row.Tenant.DisplayName,
                    Kind = TelemetrySourceKind.IslePilot, BaseUri = row.Tenant.BaseUri, LoginUri = row.Tenant.LoginUri,
                    ServerSlug = row.Tenant.ServerSlug, CookieName = "islepilot_player"
                };
                var login = new LoginWindow(source, (value, ct) => ValidateAsync(row.Tenant, value, ct))
                { Owner = this };
                if (login.ShowDialog() != true || login.CookieValue is null)
                { StatusLabel.Text = "Đã hủy đăng nhập. Chọn server hoặc thử lại."; row.Status = "CHƯA ĐĂNG NHẬP"; return; }
                cookie = login.CookieValue;
                verified = true; // LoginWindow only returns after validator success.
            }
            // Only a confirmed tenant response may be persisted or handed off.
            if (!verified) throw new InvalidOperationException("Phiên chưa xác minh.");
            await _store.SaveAsync(row.Tenant, cookie, _stop.Token);
            _store.Remember(row.Tenant);
            _stop.Token.ThrowIfCancellationRequested();
            row.Status = "ĐÃ SẴN SÀNG";
            SelectedTenant = row.Tenant; Cookie = cookie; DialogResult = true;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (IslePilotAuthenticationException)
        { row.Status = "PHIÊN HẾT HẠN"; StatusLabel.Text = "Phiên hết hạn. Bấm KẾT NỐI để đăng nhập lại."; }
        catch
        { row.Status = "CHƯA KẾT NỐI"; StatusLabel.Text = "Website không phản hồi hoặc dữ liệu chưa hợp lệ. Phiên đã lưu được giữ nguyên; hãy thử lại."; }
        finally
        { _busy = false; ConnectButton.IsEnabled = TenantList.IsEnabled = ChangeTenantButton.IsEnabled = true; ConnectButton.Content = "KẾT NỐI"; }
    }
    private static async Task<LoginSessionValidationState> ValidateAsync(SdvnTenant tenant, string cookie, CancellationToken ct)
    {
        try { using var client = new SdvnClient(tenant, cookie); await client.ValidateAsync(ct); return LoginSessionValidationState.Valid; }
        catch (IslePilotAuthenticationException) { return LoginSessionValidationState.Invalid; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return LoginSessionValidationState.Unavailable; }
    }
    private void Window_Closed(object? sender, EventArgs e) => _stop.Cancel();
    public sealed class TenantRow(SdvnTenant tenant) : INotifyPropertyChanged
    {
        private string _status = "CHƯA ĐĂNG NHẬP";
        public SdvnTenant Tenant { get; } = tenant;
        public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
