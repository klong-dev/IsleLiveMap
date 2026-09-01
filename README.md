# Isle Live Map

Ứng dụng overlay miễn phí, mã nguồn mở cho **The Isle Evrima**. Isle Live Map chạy ngoài process game, luôn nổi trên màn hình và hiển thị minimap Gateway bằng telemetry inbound/outbound đọc trực tiếp trên máy người chơi.

[Facebook K-Long.dev](https://www.facebook.com/klong.dev) · [YouTube Long Hoàng Kim](https://www.youtube.com/@longhoangkim2246) · [GitHub](https://github.com/klong-dev/IsleLiveMap)

> Nếu dự án hữu ích, xin một ⭐ cho repository và chia sẻ để nhiều người biết tới dự án phi lợi nhuận này hơn.

## Nguồn telemetry

- **Free / Local Telemetry** — đọc inbound và outbound trực tiếp từ game qua Npcap; không cần website riêng, plugin server hay server nằm trong danh sách hỗ trợ.
- **Isle Live Map Pro** — lớp nâng cấp tùy chọn, thêm nhận diện player/AI, loài, nhóm thức ăn và cân nặng. Free vẫn hoạt động độc lập khi không có quyền Pro.

Texture Gateway được đóng gói trong app dưới dạng JPEG tương thích native với WPF. Đây chỉ là ảnh nền local; marker đến từ dữ liệu game được giải mã trên máy.

## Tính năng

- Minimap Gateway dùng texture EraGaming/MyIsleMap đóng gói local dạng JPEG, không tải ảnh map qua mạng và không phụ thuộc codec WebP của Windows.
- Migration Zone, Patrol Zone và 13 vùng nguồn thức ăn Gateway được hiệu chỉnh rồi đóng gói local; app không tải lại các polygon IslePilot khi chạy.
- Player heatmap chỉ xuất hiện khi IslePilot trả cell heatmap thật cho server hiện tại; app không suy diễn heatmap từ marker player/AI.
- Marker luôn giữ giữa viewport và dùng chung map Gateway cho mọi server Evrima tương thích.
- Đặt nhiều mốc trực tiếp trên toàn bản đồ; mỗi mốc có line từ GPS và biểu tượng theo mục đích.
- Tọa độ và hướng realtime từ local packet capture, với tracker chống packet cũ, burst và candidate tọa độ giả.
- Growth, Health, Stamina, Hunger và Water khi nguồn cung cấp trường tương ứng.
- Danh sách nhiệm vụ Prime được Việt hóa; khi nhiệm vụ chuyển sang hoàn thành, overlay hiện notify ngắn rồi tự ẩn.
- Tạo nhóm tạm thời bằng mã mời 6 ký tự; đồng đội cùng server xuất hiện trên minimap với tên và hướng quay.
- Ping tạo trong phiên nhóm được relay realtime cho mọi thành viên; chỉ owner của từng ping được đổi biểu tượng hoặc xóa.
- Tùy chọn **Isle Live Map Pro** phân loại chấm đỏ (ăn thịt khác loài), xanh lá (cùng loài), xanh dương (ăn cỏ khác loài) và vàng (AI); nhãn rút gọn theo loài + cân nặng như `T-Rex 12T` hoặc `Trice 200K`.
- Mỗi đồng đội có một hàng HP, Đói và Nước; người mất tín hiệu hoặc đang ở server khác được đánh dấu riêng.
- Home ưu tiên **KÍCH HOẠT LIVE MAP** miễn phí; **KÍCH HOẠT PRO** nằm cuối trang như một nâng cấp tùy chọn.
- Phiên Pro được mã hóa bằng Windows DPAPI cho tài khoản Windows hiện tại; không lưu token plaintext.
- Pipeline capture có hàng đợi giới hạn, sequence-gap detection, cache state và recovery qua nhiều replication batch để giảm mất dữ liệu khi packet đến theo burst.
- HUD dọc, nền ngoài trong suốt, always-on-top, click-through và hỗ trợ phím tắt toàn cục.
- Resize đồng nhất toàn bộ HUD 65–175%, kéo trực tiếp trong Edit Mode và tự lưu kích thước/vị trí.
- Lời mời ủng hộ, hướng dẫn phím tắt và bảng “Có gì mới” theo phiên bản đều có thể đóng ngay.
- Auto-update qua GitHub Releases bằng Velopack.
- Đăng nhập Steam để app tự nhận quyền Pro theo SteamID64; license hỗ trợ vĩnh viễn hoặc có thời hạn và không giới hạn thiết bị.

## Nhóm sinh tồn

1. Nhập tên hiển thị rồi bấm **TẠO NHÓM**.
2. Gửi mã mời 6 ký tự cho bạn bè. Người nhận nhập tên + mã rồi bấm **NHẬP MÃ**.
3. Mở overlay như bình thường. Vị trí, hướng quay và status sẽ tự đồng bộ khi mọi người cùng server.
4. Bấm `Alt + M` và click bản đồ để ping cho cả nhóm. Mọi người đều nhìn thấy, nhưng chỉ người tạo ping được sửa/xóa.

Nhóm không phải tài khoản cố định: mã, thành viên và telemetry chỉ nằm trong RAM của app/relay. Khi app đóng hoặc cả nhóm ngừng gửi heartbeat, phiên tự hết hạn và không thể khôi phục; lần sau cần tạo/nhập lại mã. Client chỉ kết nối endpoint cố định `https://isle-relay.klong.dev` và không ghi member token xuống ổ đĩa.

## Phím tắt toàn cục

| Phím | Tác dụng |
|---|---|
| `Alt + kéo chuột trái trên map` | Chuyển sang Free Look; map đứng yên tại vùng đang xem trong khi GPS vẫn cập nhật |
| `Alt + chuột phải trên map` | Trở về Follow GPS và tự center người chơi |
| `Alt + cuộn lên` | Zoom in map |
| `Alt + cuộn xuống` | Zoom out map |
| `Alt + nút chuột giữa` | Ẩn / hiện map |
| `Alt + N` | Ẩn / hiện danh sách nhiệm vụ Prime |
| `Alt + P` | Ẩn / hiện toàn bộ HUD |
| `Ctrl + Shift + O` | Mở / khóa Edit Mode để kéo độc lập từng block hoặc resize HUD |
| `Alt + M` | Mở / đóng toàn bản đồ để tạo, đổi loại hoặc xóa mốc |

Các phím tắt hoạt động kể cả khi game hoặc ứng dụng khác đang focus. Low-level mouse hook chỉ nhận tổ hợp có `Alt`, không inject DLL và không đọc memory game.

Để chỉnh bố cục, bấm `Ctrl + Shift + O`, kéo riêng Map, Status, Team hoặc Prime; dùng `− / RESET / +` hay `DRAG ↘` để resize toàn HUD. Bấm `Alt + M` để mở toàn bản đồ, click tạo mốc rồi chọn biểu tượng. Mốc cá nhân được lưu trên máy; ping nhóm chỉ tồn tại trong room relay hiện tại.

## Đăng nhập và quyền riêng tư

Live Map Free không yêu cầu đăng nhập website. Npcap chỉ thu UDP gắn với process game và không inject DLL hay đọc memory game.

Phiên Pro cũng đăng nhập qua Steam. Refresh token được mã hóa bằng Windows DPAPI,
gói Pro chỉ được cài sau khi app kiểm tra chữ ký RSA và SHA-256, sau đó giao tiếp
với app Free qua named pipe giới hạn cho tài khoản Windows hiện tại. Source public
không chứa decoder hoặc thuật toán nhận diện player/AI của module Pro. Backend chỉ
trả manifest và artifact Pro khi JWT thuộc SteamID64 có entitlement đang hoạt động;
Agent tiếp tục tự kiểm tra license RS256 trước khi bật telemetry.

## Yêu cầu chạy

- Windows 10/11 x64.
- Microsoft Edge WebView2 Runtime (đã có sẵn trên hầu hết Windows 10/11 hiện tại).
- Game ở Borderless hoặc Windowed; Exclusive Fullscreen có thể che overlay WPF.

Tải installer mới nhất trong [GitHub Releases](https://github.com/klong-dev/IsleLiveMap/releases/latest).

## Build từ source

Yêu cầu .NET 8 SDK:

```powershell
dotnet tool restore
dotnet restore .\TheIsleOverlay.sln
dotnet test .\TheIsleOverlay.sln --configuration Release
dotnet build .\TheIsleOverlay.sln --configuration Release
```

Build installer/update package:

```powershell
.\scripts\Package-Release.ps1 -Version 1.4.0
```

Output nằm trong `artifacts/distribution`.

## Kiến trúc

```text
TheIsleOverlay.App        Home, WPF overlay, updater và global shortcuts
TheIsleOverlay.Core       Telemetry session contract, reducer support, projection và heading
TheIsleOverlay.LocalTelemetry  Npcap inbound/outbound, local movement tracker và packet pipeline
TheIsleOverlay.ProClient  Steam auth, license, signed updater và named-pipe contract cho module Pro private
TheIsleOverlay.TeamRelay  REST + SignalR cho nhóm tạm thời, heartbeat, reconnect và relay telemetry
TheIsleOverlay.Tests      Unit/integration tests auth, transport, reducer, projection và heading
```

`ITelemetrySession` là ranh giới của UI. `MainWindow` chỉ nhận `TelemetrySnapshot`; vòng REST, WebSocket, reconnect và stale detection nằm ngoài cửa sổ.

Texture nền Gateway được nhúng vào ứng dụng. Các provider chỉ lấy telemetry như tọa độ, yaw và status để overlay vẽ lên texture local; chúng không cung cấp hoặc tải ảnh map.

## Giới hạn

- Marker nhóm chỉ được vẽ khi hai người đang ở cùng server; status khác server vẫn hiện trong danh sách.
- Nhóm là phiên tạm thời, tối đa 10 người và phải tạo lại sau khi đóng app.
- Mất Internet không ảnh hưởng texture, zone hay vùng thức ăn local; đăng nhập, heatmap live và cập nhật quyền Pro cần backend tương ứng hoạt động.
- Isle Live Map Pro cần SteamID64 đang có quyền hợp lệ; app giữ license offline ngắn hạn để chịu được gián đoạn mạng tạm thời.
- Texture Gateway local được cập nhật theo từng bản phát hành của ứng dụng khi map game thay đổi.
- Bản phát hành chưa được ký bằng chứng thư thương mại, vì vậy Windows SmartScreen có thể cảnh báo ở lần chạy đầu.

## License

[MIT](LICENSE) — sử dụng, kiểm tra và đóng góp tự do; vui lòng giữ thông báo bản quyền.
