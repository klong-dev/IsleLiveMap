using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

public partial class LoginWindow : Window
{
    private readonly TelemetrySourceDefinition _source;
    private readonly Func<string, CancellationToken, Task<LoginSessionValidationState>>? _sessionValidator;
    private bool _checkingCookie;
    private bool _closingWithCookie;
    private readonly CancellationTokenSource _stop = new();

    public LoginWindow(
        TelemetrySourceDefinition source,
        Func<string, CancellationToken, Task<LoginSessionValidationState>>? sessionValidator = null)
    {
        _source = source;
        _sessionValidator = sessionValidator;
        InitializeComponent();
        SourceTitleLabel.Text = $"KẾT NỐI {_source.DisplayName.ToUpperInvariant()}";
        HostLabel.Text = _source.BaseUri.Host;
        CookieScopeLabel.Text = _source.CaptureAllHostCookies
            ? $"CHỈ ĐỌC PHIÊN ĐĂNG NHẬP @ {_source.BaseUri.Host}"
            : $"CHỈ ĐỌC {_source.CookieName} @ {_source.BaseUri.Host}";
    }

    public string? CookieValue { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var tenant = SdvnTenant.Find(_source.Id);
            var profilePath = tenant is null ? AppPaths.WebView2Profile
                : Path.Combine(AppPaths.Root, "SDVN", tenant.Id, "WebView2");
            Directory.CreateDirectory(profilePath);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: profilePath);
            await LoginBrowser.EnsureCoreWebView2Async(environment);
            if (_stop.IsCancellationRequested) return;

            LoginBrowser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NavigationStarting += Browser_NavigationStarting;
            LoginBrowser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;

            if (await TryCompleteFromCookieAsync())
            {
                return;
            }

            if (!_stop.IsCancellationRequested) LoginBrowser.Source = _source.LoginUri;
        }
        catch (Exception exception)
        {
            if (_stop.IsCancellationRequested) return;
            BrowserLoadingPanel.Visibility = Visibility.Visible;
            LoginStatusLabel.Text = $"Không mở được trình đăng nhập: {exception.Message}";
        }
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (LoginNavigationPolicy.IsAllowed(_source, e.Uri)) LoginBrowser.CoreWebView2.Navigate(e.Uri);
        else LoginStatusLabel.Text = "Đã chặn điều hướng ngoài website đăng nhập.";
    }
    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!LoginNavigationPolicy.IsAllowed(_source, e.Uri))
        { e.Cancel = true; LoginStatusLabel.Text = "Đã chặn điều hướng ngoài website đăng nhập."; }
    }

    private async void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_stop.IsCancellationRequested) return;
        BrowserLoadingPanel.Visibility = Visibility.Collapsed;
        if (!e.IsSuccess)
        {
            LoginStatusLabel.Text = "Website không tải được. Kiểm tra mạng rồi thử lại.";
            return;
        }

        await TryCompleteFromCookieAsync();
    }

    private async Task<bool> TryCompleteFromCookieAsync()
    {
        if (_checkingCookie || _stop.IsCancellationRequested || LoginBrowser.CoreWebView2 is null)
        {
            return false;
        }

        _checkingCookie = true;
        try
        {
            var cookieUri = _source.BaseUri.GetLeftPart(UriPartial.Authority);
            var cookies = await LoginBrowser.CoreWebView2.CookieManager.GetCookiesAsync(cookieUri);
            var sessionCookies = _source.CaptureAllHostCookies
                ? cookies
                    .Where(cookie =>
                        !string.IsNullOrWhiteSpace(cookie.Name) &&
                        !string.IsNullOrWhiteSpace(cookie.Value))
                    .OrderBy(cookie => cookie.Name, StringComparer.Ordinal)
                    .ToArray()
                : cookies
                    .Where(cookie =>
                        string.Equals(cookie.Name, _source.CookieName, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(cookie.Value))
                    .Take(1)
                    .ToArray();

            if (sessionCookies.Length == 0)
            {
                LoginStatusLabel.Text = "Chưa thấy phiên đăng nhập. Hoàn tất đăng nhập rồi bấm KIỂM TRA PHIÊN.";
                return false;
            }

            var sessionValue = _source.CaptureAllHostCookies
                ? string.Join("; ", sessionCookies.Select(cookie => $"{cookie.Name}={cookie.Value}"))
                : sessionCookies[0].Value;

            if (_sessionValidator is not null)
            {
                LoginStatusLabel.Text = "ĐÃ THẤY COOKIE · ĐANG XÁC MINH VỚI API…";
                using var validationTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                validationTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                var validation = await _sessionValidator(sessionValue, validationTimeout.Token);
                _stop.Token.ThrowIfCancellationRequested();
                if (validation == LoginSessionValidationState.Invalid)
                {
                    // OAuth sites may create an anonymous Express session before
                    // authentication and reuse it for the callback state. Keep that
                    // host session intact so the user can finish Discord/Steam login.
                    if (!_source.CaptureAllHostCookies)
                    {
                        foreach (var sessionCookie in sessionCookies)
                        {
                            LoginBrowser.CoreWebView2.CookieManager.DeleteCookie(sessionCookie);
                        }
                    }

                    LoginStatusLabel.Text = _source.CaptureAllHostCookies
                        ? "Đã thấy phiên website nhưng chưa đăng nhập. Hãy hoàn tất đăng nhập rồi bấm KIỂM TRA PHIÊN."
                        : "Phiên cũ không còn hợp lệ. Hãy đăng nhập lại trên website.";
                    if (LoginBrowser.Source != _source.LoginUri)
                    {
                        LoginBrowser.Source = _source.LoginUri;
                    }

                    return false;
                }

                if (validation == LoginSessionValidationState.Unavailable)
                {
                    LoginStatusLabel.Text = "Website phản hồi chậm. Phiên được giữ nguyên; bấm KIỂM TRA PHIÊN để thử lại.";
                    return false;
                }
            }

            CookieValue = sessionValue;
            _closingWithCookie = true;
            DialogResult = true;
            Close();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_stop.IsCancellationRequested) LoginStatusLabel.Text = "Website kiểm tra phiên quá chậm. Hãy bấm KIỂM TRA PHIÊN để thử lại.";
            return false;
        }
        catch (Exception)
        {
            if (!_stop.IsCancellationRequested) LoginStatusLabel.Text = "Không kiểm tra được phiên. Hãy thử lại sau; app chưa lưu phiên này.";
            return false;
        }
        finally
        {
            _checkingCookie = false;
        }
    }

    private async void CheckSessionButton_Click(object sender, RoutedEventArgs e)
    {
        LoginStatusLabel.Text = "ĐANG KIỂM TRA COOKIE CỦA HOST…";
        await TryCompleteFromCookieAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _stop.Cancel();
        if (LoginBrowser.CoreWebView2 is not null)
        {
            LoginBrowser.CoreWebView2.NavigationCompleted -= Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NavigationStarting -= Browser_NavigationStarting;
            LoginBrowser.CoreWebView2.NewWindowRequested -= Browser_NewWindowRequested;
        }

        if (!_closingWithCookie)
        {
            CookieValue = null;
        }

        LoginBrowser.Dispose();
    }
}

public enum LoginSessionValidationState
{
    Valid,
    Invalid,
    Unavailable
}
