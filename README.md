# Isle Live Map

Ứng dụng overlay miễn phí, mã nguồn mở cho **The Isle Evrima**. Isle Live Map chạy ngoài process game, luôn nổi trên màn hình và hiển thị minimap Gateway, telemetry cá nhân cùng vị trí/status của nhóm bạn từ nguồn server tương thích.

[Facebook K-Long.dev](https://www.facebook.com/klong.dev) · [YouTube Long Hoàng Kim](https://www.youtube.com/@longhoangkim2246) · [GitHub](https://github.com/klong-dev/IsleLiveMap)

> Nếu dự án hữu ích, xin một ⭐ cho repository và chia sẻ để nhiều người biết tới dự án phi lợi nhuận này hơn.

## Nguồn telemetry

- **IslePilot Network** — đăng nhập Steam một lần và tự nhận server hiện tại; hỗ trợ DinoVietNam, Premium, HoHo cùng các server đã cài plugin IslePilot.
- **EraGaming** — kết nối trực tiếp bằng phiên đăng nhập tại `https://eragamingvn.net/live-map`.
- **PANDORA** — kết nối trực tiếp bằng phiên đăng nhập tại `https://islapandora.eu/live-map`.

Texture Gateway dùng bản EraGaming/MyIsleMap đóng gói trong app dưới dạng JPEG tương thích native với WPF. Đây chỉ là ảnh nền local; telemetry và marker đến từ nguồn đã chọn.

## Tính năng

- Minimap Gateway dùng texture EraGaming/MyIsleMap đóng gói local dạng JPEG, không tải ảnh map qua mạng và không phụ thuộc codec WebP của Windows.
- Marker luôn giữ giữa viewport và dùng chung map Gateway cho mọi server Evrima tương thích.
- Dán tọa độ `Lat, Long, Alt` copy từ game trong Edit Mode để vẽ đường, beacon và khoảng cách tới đích.
- Tọa độ, yaw và status realtime từ WebSocket `/ows`; `/api/overlay/me` và `/api/overlay/map` làm baseline/fallback.
- Growth, Health, Stamina, Hunger và Water khi nguồn cung cấp trường tương ứng.
- Danh sách nhiệm vụ Prime được Việt hóa; khi nhiệm vụ chuyển sang hoàn thành, overlay hiện notify ngắn rồi tự ẩn.
- Tạo nhóm tạm thời bằng mã mời 6 ký tự; đồng đội cùng server xuất hiện trên minimap với tên và hướng quay.
- Mỗi đồng đội có một hàng HP, Đói và Nước; người mất tín hiệu hoặc đang ở server khác được đánh dấu riêng.
- Home có Steam Login cho IslePilot và hai nút riêng cho EraGaming/PANDORA, tránh dùng nhầm phiên giữa các website.
- Token overlay được mã hóa bằng Windows DPAPI cho tài khoản Windows hiện tại; không lưu plaintext.
- Tự reconnect với backoff, giữ snapshot cuối và báo `RECONNECTING`/`DATA STALE` khi mạng yếu.
- HUD dọc, nền ngoài trong suốt, always-on-top, click-through và hỗ trợ phím tắt toàn cục.
- Resize đồng nhất toàn bộ HUD 65–175%, kéo trực tiếp trong Edit Mode và tự lưu kích thước/vị trí.
- Lời mời ủng hộ, hướng dẫn phím tắt và bảng “Có gì mới” theo phiên bản đều có thể đóng ngay.
- Auto-update qua GitHub Releases bằng Velopack.

## Nhóm sinh tồn

1. Nhập tên hiển thị rồi bấm **TẠO NHÓM**.
2. Gửi mã mời 6 ký tự cho bạn bè. Người nhận nhập tên + mã rồi bấm **NHẬP MÃ**.
3. Mở overlay như bình thường. Vị trí, hướng quay và status sẽ tự đồng bộ khi mọi người cùng server.

Nhóm không phải tài khoản cố định: mã, thành viên và telemetry chỉ nằm trong RAM của app/relay. Khi app đóng hoặc cả nhóm ngừng gửi heartbeat, phiên tự hết hạn và không thể khôi phục; lần sau cần tạo/nhập lại mã. Client chỉ kết nối endpoint cố định `https://isle-relay.klong.dev` và không ghi member token xuống ổ đĩa.

## Phím tắt toàn cục

| Phím | Tác dụng |
|---|---|
| `Alt + cuộn lên` | Zoom in map |
| `Alt + cuộn xuống` | Zoom out map |
| `Alt + nút chuột giữa` | Ẩn / hiện map |
| `Alt + N` | Ẩn / hiện danh sách nhiệm vụ Prime |
| `Alt + P` | Ẩn / hiện toàn bộ HUD |
| `Ctrl + Shift + O` | Mở / khóa Edit Mode để kéo, resize hoặc nhập tọa độ chỉ đường |

Các phím tắt hoạt động kể cả khi game hoặc ứng dụng khác đang focus. Low-level mouse hook chỉ nhận tổ hợp có `Alt`, không inject DLL và không đọc memory game.

Để đổi kích thước, bấm `Ctrl + Shift + O`, sau đó dùng `− / RESET / +` hoặc giữ `DRAG ↘` ở cuối overlay. Cũng tại đây, dán chuỗi như `-3,007.455, -4,606.069, 44,728.061` rồi bấm **CHỈ ĐƯỜNG**. Map, status, nhiệm vụ và danh sách đồng đội cùng scale; thiết lập được lưu tự động.

## Đăng nhập và quyền riêng tư

App mở website đăng nhập trong Microsoft Edge WebView2 với profile riêng tại:

```text
%LocalAppData%\KLongDev\IsleLiveMap\WebView2
```

Callback IslePilot `isle-overlay://` được bắt ngay bên trong WebView2. Isle Live Map không đăng ký hoặc chiếm protocol này trong Windows.

Overlay Bearer token được lưu tại `%LocalAppData%\KLongDev\IsleLiveMap` sau khi mã hóa bằng DPAPI CurrentUser. Token:

- Không được ghi vào log, source, `.env` hoặc JSON plaintext.
- Chỉ được gửi tới host cố định `https://islepilot.eu` và `wss://islepilot.eu/ows`.
- Chỉ dùng để đọc `/api/overlay/me`, `/api/overlay/map` và frame `live` từ `/ows`.
- Bị xóa khi người dùng đăng xuất hoặc API trả 401/403.

Với nguồn trực tiếp, app chỉ đọc cookie từ đúng host đang mở: `era_session` chỉ được gửi lại `eragamingvn.net`; phiên PANDORA chỉ được gửi lại `islapandora.eu`. WebView2 quản lý cookie trong profile riêng; header dùng gọi API chỉ được ghép trong bộ nhớ và không được ghi vào log hoặc source.

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
.\scripts\Package-Release.ps1 -Version 1.2.0
```

Output nằm trong `artifacts/distribution`.

## Kiến trúc

```text
TheIsleOverlay.App        Home, Steam WebView2 login, WPF overlay, updater, global shortcuts
TheIsleOverlay.Core       Telemetry session contract, reducer support, projection và heading
TheIsleOverlay.EraGaming  Adapter JSON API EraGaming
TheIsleOverlay.IslePilot  Bearer REST, WebSocket realtime, auth, reducer và DPAPI credential store
TheIsleOverlay.Pandora    Adapter session API PANDORA
TheIsleOverlay.TeamRelay  REST + SignalR cho nhóm tạm thời, heartbeat, reconnect và relay telemetry
TheIsleOverlay.Tests      Unit/integration tests auth, transport, reducer, projection và heading
```

`ITelemetrySession` là ranh giới của UI. `MainWindow` chỉ nhận `TelemetrySnapshot`; vòng REST, WebSocket, reconnect và stale detection nằm ngoài cửa sổ.

Texture nền Gateway được nhúng vào ứng dụng. Các provider chỉ lấy telemetry như tọa độ, yaw và status để overlay vẽ lên texture local; chúng không cung cấp hoặc tải ảnh map.

## Giới hạn

- IslePilot dùng WebSocket realtime; EraGaming và PANDORA cập nhật theo nhịp API do website tương ứng cho phép.
- Marker nhóm chỉ được vẽ khi hai người đang ở cùng server; status khác server vẫn hiện trong danh sách.
- Nhóm là phiên tạm thời, tối đa 10 người và phải tạo lại sau khi đóng app.
- Mất Internet không ảnh hưởng texture map local nhưng telemetry sẽ chuyển `RECONNECTING` hoặc `DATA STALE`.
- Nhiệm vụ Prime hiện chỉ có trên nguồn IslePilot khi server/API cung cấp danh sách tương ứng.
- Website nguồn thay đổi endpoint, callback hoặc payload có thể yêu cầu cập nhật client.
- Texture Gateway local được cập nhật theo từng bản phát hành của ứng dụng khi map game thay đổi.
- Bản phát hành chưa được ký bằng chứng thư thương mại, vì vậy Windows SmartScreen có thể cảnh báo ở lần chạy đầu.

## License

[MIT](LICENSE) — sử dụng, kiểm tra và đóng góp tự do; vui lòng giữ thông báo bản quyền.
