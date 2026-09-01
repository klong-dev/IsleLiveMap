using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class IslePilotSteamLoginWindow : Window
{
    private bool _completed;
    private bool _completing;

    public IslePilotSteamLoginWindow()
    {
        InitializeComponent();
    }

    public IslePilotOverlayAuthResult? Credentials { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.WebView2Profile);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.WebView2Profile);
            await LoginBrowser.EnsureCoreWebView2Async(environment);

            LoginBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            LoginBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            LoginBrowser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            LoginBrowser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;
            NavigateToLogin();
        }
        catch (Exception exception)
        {
            BrowserLoadingPanel.Visibility = Visibility.Visible;
            LoginStatusLabel.Text = $"Không mở được đăng nhập Steam: {FriendlyMessage(exception)}";
        }
    }

    private void Browser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (TryCompleteFromCallback(e.Uri))
        {
            e.Cancel = true;
            return;
        }

        if (!IslePilotOverlayLoginNavigationPolicy.IsAllowed(e.Uri))
        {
            e.Cancel = true;
            LoginStatusLabel.Text = "Đã chặn điều hướng nằm ngoài IslePilot và Steam.";
        }
    }

    private void Browser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        BrowserLoadingPanel.Visibility = Visibility.Collapsed;
        if (!e.IsSuccess && !_completed)
        {
            LoginStatusLabel.Text = "Trang đăng nhập không tải được. Kiểm tra mạng rồi bấm THỬ LẠI.";
        }
    }

    private void Browser_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (TryCompleteFromCallback(e.Uri))
        {
            return;
        }

        if (IslePilotOverlayLoginNavigationPolicy.IsAllowed(e.Uri))
        {
            LoginBrowser.CoreWebView2.Navigate(e.Uri);
            return;
        }

        LoginStatusLabel.Text = "Đã chặn cửa sổ nằm ngoài IslePilot và Steam.";
    }

    private bool TryCompleteFromCallback(string? callback)
    {
        if (!Uri.TryCreate(callback, UriKind.Absolute, out var uri)
            || !string.Equals(
                uri.Scheme,
                IslePilotOverlayAuthService.CallbackScheme,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IslePilotOverlayAuthService.TryParseCallback(callback, out var credentials)
            || credentials is null)
        {
            LoginStatusLabel.Text = "IslePilot trả về callback không hợp lệ. Hãy thử đăng nhập lại.";
            return true;
        }

        if (!_completing)
        {
            _completing = true;
            BrowserLoadingPanel.Visibility = Visibility.Visible;
            LoginStatusLabel.Text = "Đang hoàn tất phiên IslePilot…";
            _ = CompleteFromCallbackAsync(credentials);
        }

        return true;
    }

    private async Task CompleteFromCallbackAsync(IslePilotOverlayAuthResult credentials)
    {
        string? playerCookie = null;
        try
        {
            if (LoginBrowser.CoreWebView2 is not null)
            {
                playerCookie = await IslePilotPlayerCookieReader.ReadAsync(
                    LoginBrowser.CoreWebView2.CookieManager);
            }
        }
        catch
        {
            // The overlay token remains valid if the optional tenant cookie
            // cannot be read. Only server-specific heatmap will be unavailable.
        }

        Credentials = credentials with { PlayerCookie = playerCookie };
        _completed = true;
        DialogResult = true;
        Close();
    }

    private void NavigateToLogin()
    {
        if (LoginBrowser.CoreWebView2 is null)
        {
            return;
        }

        BrowserLoadingPanel.Visibility = Visibility.Visible;
        LoginStatusLabel.Text = "Đang chuyển tới IslePilot và Steam…";
        LoginBrowser.CoreWebView2.Navigate(IslePilotOverlayAuthService.LoginUri.AbsoluteUri);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => NavigateToLogin();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_completing)
        {
            Close();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        if (LoginBrowser.CoreWebView2 is not null)
        {
            LoginBrowser.CoreWebView2.NavigationStarting -= Browser_NavigationStarting;
            LoginBrowser.CoreWebView2.NavigationCompleted -= Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NewWindowRequested -= Browser_NewWindowRequested;
        }

        if (!_completed)
        {
            Credentials = null;
        }

        LoginBrowser.Dispose();
    }

    private static string FriendlyMessage(Exception exception) => exception switch
    {
        WebView2RuntimeNotFoundException => "Máy chưa có Microsoft Edge WebView2 Runtime.",
        _ => exception.Message
    };
}
