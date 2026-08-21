using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace TheIsleOverlay.App;

public partial class LoginWindow : Window
{
    private readonly TelemetrySourceDefinition _source;
    private readonly Func<string, CancellationToken, Task<LoginSessionValidationState>>? _sessionValidator;
    private bool _checkingCookie;
    private bool _closingWithCookie;

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
            Directory.CreateDirectory(AppPaths.WebView2Profile);
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.WebView2Profile);
            await LoginBrowser.EnsureCoreWebView2Async(environment);

            LoginBrowser.CoreWebView2.NavigationCompleted += Browser_NavigationCompleted;
            LoginBrowser.CoreWebView2.NewWindowRequested += Browser_NewWindowRequested;

            if (await TryCompleteFromCookieAsync())
            {
                return;
            }

            LoginBrowser.Source = _source.LoginUri;
        }
        catch (Exception exception)
        {
            BrowserLoadingPanel.Visibility = Visibility.Visible;
            LoginStatusLabel.Text = $"Không mở được trình đăng nhập: {exception.Message}";
        }
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        LoginBrowser.CoreWebView2.Navigate(e.Uri);
    }

    private async void Browser_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
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
        if (_checkingCookie || LoginBrowser.CoreWebView2 is null)
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
                using var validationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var validation = await _sessionValidator(sessionValue, validationTimeout.Token);
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
                    LoginStatusLabel.Text = "ĐÃ THẤY COOKIE · API ĐANG CHẬM, TIẾP TỤC KẾT NỐI…";
                }
            }

            CookieValue = sessionValue;
            _closingWithCookie = true;
            DialogResult = true;
            Close();
            return true;
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
        if (LoginBrowser.CoreWebView2 is not null)
        {
            LoginBrowser.CoreWebView2.NavigationCompleted -= Browser_NavigationCompleted;
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
