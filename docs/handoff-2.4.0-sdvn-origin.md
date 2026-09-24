# Handoff 2.4.0: SDVN, GACHA/ORIGIN và Origin stats

## Yêu cầu mới nhất của owner

Chỉ triển khai, test và commit. **Không tạo release, tag, push hoặc sửa VPS.**
Version source là 2.4.0 và release notes đã chuẩn bị, không có nghĩa đã phát hành.

## Commit và ranh giới working tree

- a5050a1: tách Origin health/Prime, nhịp 2.5s, stale window, command lane và tests.
- 6f7a9fa: SDVN tenant catalog, cookie store, provider/session, modal và nối lại Home server actions.
- b48146b: commit prerequisite IsCompleted của prewarmed source để source đã commit tự build được.
- Commit chứa tài liệu này: hoàn thiện layout/status, kiểm tra actual compiled handlers và bàn giao.

Home đã có ProTelemetryWarmup/anti-double-click/lifetime changes trước khi task này bắt đầu.
Phần Home bị chồng lấn đã được giữ và commit cùng prerequisite cần thiết. Các thay đổi decoder,
tracking lifecycle, IPC diagnostics và script TrackingLab khác vẫn để unstaged/untracked;
không reset, không gom chúng vào feature SDVN. Một session khác đang làm tracking cùng workspace.

## Đã triển khai

### SDVN

| ID | Host | Slug | Trạng thái |
| --- | --- | --- | --- |
| sdvn-1 | 1.sdvn.org | sdvn | Bật |
| sdvn-2 | 2.sdvn.org | sdvn2234234 | Bật |
| sdvn-3 | 3.sdvn.org | sdvn3123123123 | Bật |
| sdvn-4 | 4.sdvn.org | Chưa xác minh | Ẩn; TLS 525 khi khảo sát |

- Một nút SDVN trên Home, logo gốc từ D:/shydino-icon.png; modal chọn #1–#3, nhớ lựa chọn gần nhất.
- Cookie islepilot_player chỉ gửi tới HTTPS host được chọn; không redirect API sang host khác, không dùng cookie DinoVietnam/HoHo.
- Lưu DPAPI riêng tại %LOCALAPPDATA%/KLongDev/IsleLiveMap/SDVN/sdvn-N.credential.
  Entropy và payload đều gắn tenant. JWT exp chỉ là kiểm tra expiry sơ bộ, không thay thế server authentication.
- WebView2 profile riêng tại SDVN/sdvn-N/WebView2; chỉ cho điều hướng tenant đó và Steam.
- Gọi /me và /api/p/{slug}/map/markers. Stats poll 5s sau mỗi lượt; marker 2.5s; hai luồng độc lập.
- Chỉ chọn self marker; không dùng marker người khác khi thiếu self. GPS overlay vẫn do local movement ưu tiên.
- Hỗ trợ parser hiện có cho Growth/Health/Hunger/Thirst, thêm Stamina khi HTML có trường đó.
- Bản adapter tenant này **chưa parse Prime từ HTML SDVN**; không ngầm lấy nhiệm vụ từ tenant khác.

### Home, GACHA và ORIGIN

- Handler thật nằm ở HomeWindow.ServerConnections.cs, không bật lại các partial cũ bị loại khỏi csproj.
- GACHA kiểm tra/refresh credential rồi mở GachaTelemetrySession; Origin dùng cookie playorigin.gg trong profile của app, validate trước khi mở OriginStatsSession.
- Dùng chung update gate, Npcap gate, Pro source handoff và chống mở hai overlay.
- Trạng thái hiển thị ngay trong khung hành động Home; các nút bị khóa lúc kết nối.
- Navigation styling không còn ghi đè màu nút server. Release rail tạm thu gọn ở chiều rộng dưới 1100 DIP để ba nút không bị che; trên 1100 vẫn hiện rail.
- Login timeout hiển thị lỗi, không crash async void và không coi cookie chưa xác minh là phiên valid.

### Origin

- IOriginStatsClient cho phép test không gọi mạng. Health mục tiêu mỗi 2.5s, tính thời gian request trong chu kỳ; Prime riêng mỗi 15s.
- Probe Main/Voice song song; dùng server có dino; chuyển server khi có kết quả terminal/no-dino. Pending/transport failure không tự chứng minh người chơi chuyển server.
- Poll command tối đa 12 lần, cách nhau 250ms, budget HTTP khoảng 4s.
- Local timeout không hủy được command server: giữ mã pending và tiếp tục poll mã đó, không enqueue lặp. HTTP 429 tôn trọng Retry-After; health lỗi backoff 5s rồi 10s.
- Kết quả có RequestedAt để dữ liệu queue lâu không giả làm realtime; health cũ quá 10s không hiển thị.
- Snapshot cũ giữ tối đa 10s từ mốc đo bảo thủ, không gia hạn bằng lượt fallback.
- Prime chậm không chặn health; HUD Prime có IsSynchronizing riêng.

## Verification ngày 24/09/2026

- Full working tree: dotnet test TheIsleOverlay.sln -c Release --no-restore — 576/576 passed (380 core/provider, 36 ProClient, 160 App).
- Snapshot commit b48146b lấy bằng git archive, build riêng: 0 warning, 0 error.
- Full test trên snapshot commit: 558/558 passed (367 core/provider, 31 ProClient, 160 App).
  Số test ít hơn vì không mang các thay đổi tracking đang unstaged vào snapshot.
- ServerConnectionsTests chạy riêng bật capture: 8/8, kiểm tra resource SDVN, actual compiled handlers, update gate và double-click gate.
- Ảnh WPF đã render và xem trực tiếp: artifacts/qa-sdvn-240/home-820.png, home-960.png, home-1200.png, home-pro.png, sdvn-modal.png.
- Build/test artifacts không commit. Snapshot dùng đối chiếu: artifacts/committed-sdvn-20260924-171242/source.

Lệnh cho session sau (chạy tại repo root):

```powershell
dotnet test TheIsleOverlay.sln -c Release --no-restore
dotnet build TheIsleOverlay.sln -c Release --no-restore
$env:ISLE_SERVER_UI_CAPTURE = Join-Path (Get-Location) "artifacts/qa-sdvn-240"
dotnet test tests/TheIsleOverlay.App.Tests/TheIsleOverlay.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~ServerConnectionsTests
Remove-Item Env:ISLE_SERVER_UI_CAPTURE
```

Chạy capture riêng; test tạo WPF Application và không được bật chung với các test khác tạo Application trên thread khác.
Không giả lập input game, không tự nhập Steam credentials, không thay thế phiên live map tracking đang chạy.

## Chưa nghiệm thu live / bước tiếp theo

1. Chưa có cookie SDVN thật. Tab #1 trong trình duyệt vẫn hiện Sign in with Steam ở lần kiểm tra cuối.
   Fixtures là HTML/JSON mô phỏng từ contract hiện có, **không phải bản ghi response sau đăng nhập SDVN**.
2. Chưa xác nhận /me sau đăng nhập của #1–#3 có markup giống parser, gồm trạng thái offline/no-dino và Stamina.
   Cần smoke test bằng dino thật trước khi tuyên bố hỗ trợ production hoàn tất. Nếu không khớp, lưu fixture đã bỏ định danh/cookie và sửa parser.
3. Chưa đo Origin thật khi dino đang chơi; 2–3s là nhịp client với API bình thường, không phải SLA của server.
4. Chưa smoke test GACHA/Origin bằng phiên thật trong lượt triển khai này. Cần kiểm tra hết hạn/refresh/cancel login và quay về Home.
5. Giữ SDVN #4 ẩn; chỉ bật sau khi xác minh TLS, slug và API thật. Không tự thử slug sdvn4.
6. **Không publish** nếu owner chưa yêu cầu lại. Chưa tạo installer/tag/release, chưa cập nhật VPS/Pro backend/relay.
