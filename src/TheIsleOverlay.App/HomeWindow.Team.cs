using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private bool _teamPanelInitialized;
    private bool _teamChanging;
    private bool _normalizingInviteCode;

    private void InitializeTeamPanel()
    {
        if (_teamPanelInitialized)
        {
            return;
        }

        _teamPanelInitialized = true;
        App.CurrentTeam.StateChanged += TeamCoordinator_StateChanged;
        ApplyTeamState(App.CurrentTeam.CurrentState);
    }

    private void DetachTeamPanel()
    {
        if (!_teamPanelInitialized)
        {
            return;
        }

        App.CurrentTeam.StateChanged -= TeamCoordinator_StateChanged;
        _teamPanelInitialized = false;
    }

    private async void CreateTeamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_teamChanging || !TryGetTeamDisplayName(out var displayName))
        {
            return;
        }

        SetTeamBusy(true, "ĐANG TẠO NHÓM…");
        try
        {
            await App.CurrentTeam.CreateAsync(displayName, _shutdown.Token);
            TeamErrorLabel.Text = "Nhóm đã sẵn sàng. Gửi mã mời cho bạn bè.";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TeamErrorLabel.Text = FriendlyTeamError(exception);
        }
        finally
        {
            SetTeamBusy(false);
        }
    }

    private async void JoinTeamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_teamChanging || !TryGetTeamDisplayName(out var displayName))
        {
            return;
        }

        var inviteCode = NormalizeInviteCode(InviteCodeTextBox.Text);
        if (inviteCode.Length != 6)
        {
            TeamErrorLabel.Text = "Mã mời phải có đúng 6 chữ hoặc số.";
            return;
        }

        SetTeamBusy(true, "ĐANG VÀO NHÓM…");
        try
        {
            await App.CurrentTeam.JoinAsync(inviteCode, displayName, _shutdown.Token);
            TeamErrorLabel.Text = "Đã vào nhóm. Mở overlay để chia sẻ vị trí và status.";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TeamErrorLabel.Text = FriendlyTeamError(exception);
        }
        finally
        {
            SetTeamBusy(false);
        }
    }

    private async void LeaveTeamButton_Click(object sender, RoutedEventArgs e)
    {
        if (_teamChanging)
        {
            return;
        }

        SetTeamBusy(true, "ĐANG RỜI NHÓM…");
        try
        {
            await App.CurrentTeam.LeaveAsync(_shutdown.Token);
            TeamErrorLabel.Text = "Đã rời nhóm. Dữ liệu phiên của bạn đã được dọn.";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TeamErrorLabel.Text = FriendlyTeamError(exception);
        }
        finally
        {
            SetTeamBusy(false);
        }
    }

    private void CopyInviteCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var inviteCode = App.CurrentTeam.CurrentState.Session?.InviteCode;
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            return;
        }

        try
        {
            Clipboard.SetText(inviteCode);
            TeamErrorLabel.Text = $"Đã copy mã {inviteCode}.";
        }
        catch
        {
            TeamErrorLabel.Text = "Windows chưa cho phép copy. Hãy nhập mã đang hiển thị.";
        }
    }

    private void InviteCodeTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        InviteCodePlaceholder.Visibility = string.IsNullOrEmpty(InviteCodeTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_normalizingInviteCode)
        {
            return;
        }

        var normalized = NormalizeInviteCode(InviteCodeTextBox.Text);
        if (normalized == InviteCodeTextBox.Text)
        {
            return;
        }

        _normalizingInviteCode = true;
        InviteCodeTextBox.Text = normalized;
        InviteCodeTextBox.CaretIndex = normalized.Length;
        _normalizingInviteCode = false;
    }

    private void TeamCoordinator_StateChanged(object? sender, TeamRelayState state)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyTeamState(state);
        }
        else
        {
            Dispatcher.BeginInvoke(() => ApplyTeamState(state));
        }
    }

    private void ApplyTeamState(TeamRelayState state)
    {
        var active = state.HasActiveSession;
        TeamInactivePanel.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        TeamActivePanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

        TeamStateLabel.Text = state.ConnectionState switch
        {
            TeamRelayConnectionState.Connecting => "ĐANG KẾT NỐI",
            TeamRelayConnectionState.Live => "RELAY TRỰC TUYẾN",
            TeamRelayConnectionState.Reconnecting => "ĐANG NỐI LẠI",
            TeamRelayConnectionState.Expired => "PHIÊN ĐÃ HẾT",
            TeamRelayConnectionState.Error => "KHÔNG KẾT NỐI",
            _ => "CHƯA VÀO NHÓM"
        };
        TeamStateDot.Fill = HomeBrush(state.ConnectionState switch
        {
            TeamRelayConnectionState.Live => "#37D4C6",
            TeamRelayConnectionState.Connecting or TeamRelayConnectionState.Reconnecting => "#E7B74E",
            TeamRelayConnectionState.Expired or TeamRelayConnectionState.Error => "#DC5A56",
            _ => "#607E76"
        });

        if (active && state.Session is { } session)
        {
            ActiveInviteCodeLabel.Text = string.Join(" ", session.InviteCode.ToCharArray());
            TeamMemberCountLabel.Text = $"{state.Members.Count} / {session.MaxMembers} NGƯỜI";
            TeamErrorLabel.Text = state.ConnectionState switch
            {
                TeamRelayConnectionState.Reconnecting => "Mạng gián đoạn; app đang tự nối lại mà không làm mất nhóm.",
                TeamRelayConnectionState.Connecting => "Đang mở kênh realtime bảo mật…",
                _ => "Minimap và status đồng đội sẽ xuất hiện trong overlay."
            };
        }
        else if (state.ConnectionState is TeamRelayConnectionState.Expired or TeamRelayConnectionState.Error)
        {
            TeamErrorLabel.Text = state.Message ?? "Phiên nhóm đã kết thúc. Hãy tạo hoặc nhập mã lại.";
        }

        SetTeamBusy(_teamChanging);
    }

    private void SuggestTeamDisplayName(string steamIdSuffix)
    {
        if (string.IsNullOrWhiteSpace(TeamDisplayNameTextBox.Text)
            || string.Equals(TeamDisplayNameTextBox.Text.Trim(), "Survivor", StringComparison.OrdinalIgnoreCase))
        {
            TeamDisplayNameTextBox.Text = $"Survivor {steamIdSuffix}";
        }
    }

    private bool TryGetTeamDisplayName(out string displayName)
    {
        displayName = TeamDisplayNameTextBox.Text.Trim();
        if (displayName.Length is >= 1 and <= 32)
        {
            return true;
        }

        TeamErrorLabel.Text = "Tên hiển thị phải có từ 1 đến 32 ký tự.";
        return false;
    }

    private void SetTeamBusy(bool busy, string? message = null)
    {
        _teamChanging = busy;
        CreateTeamButton.IsEnabled = !busy;
        JoinTeamButton.IsEnabled = !busy;
        LeaveTeamButton.IsEnabled = !busy;
        TeamDisplayNameTextBox.IsEnabled = !busy;
        InviteCodeTextBox.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            TeamErrorLabel.Text = message;
        }
    }

    private static string FriendlyTeamError(Exception exception) => exception switch
    {
        TeamRelayApiException { Code: "invite_not_found" } => "Không tìm thấy mã mời hoặc nhóm đã tự hết hạn.",
        TeamRelayApiException { Code: "team_full" } => "Nhóm đã đủ thành viên.",
        TeamRelayApiException { Code: "rate_limited" } => "Bạn thao tác quá nhanh. Chờ một chút rồi thử lại.",
        HttpRequestException => "Không liên lạc được isle-relay.klong.dev.",
        _ => $"Không mở được nhóm: {exception.Message}"
    };

    private static string NormalizeInviteCode(string? value) => new(
        (value ?? string.Empty)
        .Where(char.IsAsciiLetterOrDigit)
        .Select(char.ToUpperInvariant)
        .Take(6)
        .ToArray());

    private static SolidColorBrush HomeBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
