using System.Windows;
using TheIsleOverlay.TeamRelay;

namespace TheIsleOverlay.App;

public partial class HomeWindow
{
    private bool _teamOperationRunning;
    private string _teamStatus = "Chọn quy mô phòng, sau đó nhập tên của bạn. Số chỗ tính cả người tạo.";

    private async void CreateSizedTeam_Click(object sender, RoutedEventArgs e)
    {
        if (_teamOperationRunning) return;
        _teamOperationRunning = true;
        try
        {
            if (_proLoadTask is not null) await _proLoadTask;
            _shutdown.Token.ThrowIfCancellationRequested();
            var tier = _pro.Entitlement.IsProAt(DateTimeOffset.UtcNow) ? TeamAccessTier.Pro : TeamAccessTier.Free;
            var capacity = new TeamCapacityWindow(tier) { Owner = this };
            if (capacity.ShowDialog() != true || capacity.SelectedCapacity is not { } size) return;
            var name = PromptTeamName($"TẠO PHÒNG {size} NGƯỜI");
            if (name is null) return;
            // Refresh signed proof only for a paid room; do not make free
            // creation dependent on the licensing service. Recheck after dialogs.
            if (size > TeamRoomLimits.FreeMaxMembers) await LoadProAsync();
            _shutdown.Token.ThrowIfCancellationRequested();
            tier = _pro.Entitlement.IsProAt(DateTimeOffset.UtcNow) ? TeamAccessTier.Pro : TeamAccessTier.Free;
            if (!TeamRoomLimits.CanCreate(tier, size))
            { _teamStatus = TeamCapacityWindow.ProRequiredMessage(size); return; }
            App.CurrentTeam.ConfigureAccess(tier, _pro.EntitlementProof);
            _teamStatus = $"ĐANG TẠO PHÒNG {size} NGƯỜI…";
            if (_page == "team") ReplacePage(BuildTeam);
            var session = await App.CurrentTeam.CreateAsync(name, tier, size, _shutdown.Token);
            if (session.MaxMembers != size)
            {
                await App.CurrentTeam.LeaveAsync(_shutdown.Token);
                _teamStatus = "Relay chưa hỗ trợ đúng quy mô đã chọn. Không tạo phòng sai giới hạn; hãy thử lại sau.";
            }
            else _teamStatus = $"Đã tạo phòng {session.MaxMembers} người, bao gồm bạn.";
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception error) { _teamStatus = FriendlyTeamErrorText(error); }
        finally
        {
            _teamOperationRunning = false;
            if (!_shutdown.IsCancellationRequested && _page == "team") ReplacePage(BuildTeam);
        }
    }
}
