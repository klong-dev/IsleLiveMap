using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TheIsleOverlay.ProClient;

namespace TheIsleOverlay.App;

public partial class ProSteamLoginWindow : Window
{
    private readonly ProAccessService _accessService;
    private readonly string _hostVersion;
    private readonly CancellationTokenSource _shutdown = new();
    private ProLoginAttempt? _attempt;
    private bool _completing;
    private bool _completed;

    public ProSteamLoginWindow(ProAccessService accessService, string hostVersion)
    {
        _accessService = accessService ?? throw new ArgumentNullException(nameof(accessService));
        _hostVersion = hostVersion;
        InitializeComponent();
    }

    public ProAccessSnapshot? Access { get; private set; }

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
            LoginStatusLabel.Text = $"Không mở được Steam: {FriendlyMessage(exception)}";
        }
    }

    private async void Browser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (ProLoginAttempt.IsCallback(e.Uri))
        {
            e.Cancel = true;
            await CompleteAsync(e.Uri);
            return;
        }

        if (!ProLoginNavigationPolicy.IsAllowed(e.Uri, ProClientOptions.ProductionBaseUri))
        {
            e.Cancel = true;
            LoginStatusLabel.Text = "Đã chặn điều hướng nằm ngoài Isle Live Map và Steam.";
        }
    }

    private void Browser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        BrowserLoadingPanel.Visibility = Visibility.Collapsed;
        if (!e.IsSuccess && !_completed && !_completing)
        {
            LoginStatusLabel.Text = "Trang đăng nhập không tải được. Kiểm tra mạng rồi bấm THỬ LẠI.";
        }
    }

    private async void Browser_NewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (ProLoginAttempt.IsCallback(e.Uri))
        {
            await CompleteAsync(e.Uri);
            return;
        }

        if (ProLoginNavigationPolicy.IsAllowed(e.Uri, ProClientOptions.ProductionBaseUri))
        {
            LoginBrowser.CoreWebView2.Navigate(e.Uri);
            return;
        }

        LoginStatusLabel.Text = "Đã chặn cửa sổ nằm ngoài Isle Live Map và Steam.";
    }

    private async Task CompleteAsync(string callbackUri)
    {
        if (_completing || _attempt is null)
        {
            return;
        }

        _completing = true;
        BrowserLoadingPanel.Visibility = Visibility.Visible;
        LoginStatusLabel.Text = "Steam đã xác minh. Đang kiểm tra quyền Pro…";
        try
        {
            Access = await _accessService.CompleteLoginAsync(
                _attempt,
                callbackUri,
                _hostVersion,
                _shutdown.Token);
            _completed = true;
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LoginStatusLabel.Text = $"Không hoàn tất được xác minh: {FriendlyMessage(exception)}";
            BrowserLoadingPanel.Visibility = Visibility.Collapsed;
            _attempt = null;
        }
        finally
        {
            _completing = false;
        }
    }

    private void NavigateToLogin()
    {
        if (LoginBrowser.CoreWebView2 is null || _completing)
        {
            return;
        }

        _attempt = _accessService.CreateLoginAttempt();
        BrowserLoadingPanel.Visibility = Visibility.Visible;
        LoginStatusLabel.Text = "Đang chuyển tới Isle Live Map và Steam…";
        LoginBrowser.CoreWebView2.Navigate(_attempt.LoginUri.AbsoluteUri);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => NavigateToLogin();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _shutdown.Cancel();
        if (LoginBrowser.CoreWebView2 is not null)
        {
            LoginBrowser.CoreWebView2.NavigationStarting -= Browser_NavigationStarting;
            LoginBrowser.CoreWebView2.NavigationCompleted -= Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NewWindowRequested -= Browser_NewWindowRequested;
        }

        if (!_completed)
        {
            Access = null;
        }

        LoginBrowser.Dispose();
        _shutdown.Dispose();
    }

    private static string FriendlyMessage(Exception exception) => exception switch
    {
        WebView2RuntimeNotFoundException => "Máy chưa có Microsoft Edge WebView2 Runtime.",
        ProApiException => "dịch vụ cấp phép chưa phản hồi hoặc từ chối phiên.",
        _ => exception.Message
    };
}
