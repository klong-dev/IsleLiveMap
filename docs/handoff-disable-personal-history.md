# Bàn giao: tạm tắt vị trí cuối / mốc đã chết

## Yêu cầu

Chỉ commit để session khác phát hành. Không push, tạo tag, sửa release 2.4.5,
triển khai VPS hoặc tự phát hành phiên bản mới trong lượt này.

## Nguyên nhân lỗi trên 2.4.5

- Tag `v2.4.5` trỏ tới `b1e44adc0a1cf6bf8f5e9518f989e33228687f16`.
- Commit tắt `b017694` nằm ở nhánh `codex/autonomous-tracking-debug`, chưa vào release.
- Gói `IsleLiveMap-2.4.5-full.nupkg` có SHA256
  `cba761cbee6f7508ef0c1bf93dd03619b2d9dc4605edcde985c8a16e5a4515bb`,
  khớp asset GitHub; DLL không chứa `PersonalHistoryFeature`.
- Source 2.4.5 vẫn khởi chạy timer và tạo mốc khi mất GPS/đóng overlay.

## Commit đã đưa vào nhánh phát hành

`ced8096` (cherry-pick `b017694`) trên `codex/origin-inbound-stats-fusion`.
Worktree: `C:/Users/user/.codex/worktrees/release-2-4-4/The Isle Overlay`.

- `PersonalHistoryFeature.Enabled` trả false.
- Không khởi chạy timer, không tạo mốc khi tick hoặc đóng overlay.
- Không bật đồng bộ mốc cá nhân từ UI.
- Ẩn mốc lịch sử trên minimap/Alt+M và ẩn control đồng bộ.
- Giữ nguyên dữ liệu local; không xóa marker trên relay. TTL relay vẫn như cũ.
- Không thay ping nhóm, mốc thủ công hoặc marker tracking Pro.

## Khi phát hành

1. Đảm bảo commit `ced8096` có trong commit dùng để đóng gói.
2. Giữ riêng các thay đổi Origin/inbound đang có của session khác trong worktree.
3. Dùng phiên bản cao hơn 2.4.5 theo kế hoạch phát hành; không ghi đè asset cũ.
4. Kiểm tra DLL trong nupkg thực sự chứa cờ tắt và test hành vi,
   không chỉ kiểm tra source hoặc số phiên bản.
5. Smoke test với map-notes.json có sẵn LastKnown/Death: không hiện mốc,
   chết/reconnect/đóng rồi mở app không sinh thêm mốc; mốc thủ công vẫn hoạt động.
6. Không cần restart backend cho thay đổi này.

Lệnh kiểm thử tập trung:

Đã chạy trên worktree phát hành ngày 29/09/2026: build qua, 6/6 test pass,
bao gồm vòng đời cửa sổ map 50 lần và kiểm tra ẩn history/giữ dữ liệu.
Kết quả này dùng working tree hiện tại (có thay đổi chưa commit của session khác),
không thay thế kiểm thử gói release cuối cùng.

```powershell
dotnet test tests/TheIsleOverlay.App.Tests/TheIsleOverlay.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PersonalHistory|FullyQualifiedName~PersonalMarkerSync|FullyQualifiedName~MapNotesWindowLifecycle|FullyQualifiedName~MapNoteIconCatalog" --verbosity minimal
```
