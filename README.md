# Isle Live Map

Ứng dụng overlay miễn phí, mã nguồn mở cho **The Isle Evrima**. Isle Live Map chạy ngoài process game, luôn nổi trên màn hình và hiển thị minimap Gateway cùng telemetry cá nhân do IslePilot cung cấp.

[Facebook K-Long.dev](https://www.facebook.com/klong.dev) · [YouTube Long Hoàng Kim](https://www.youtube.com/@longhoangkim2246) · [GitHub](https://github.com/klong-dev/IsleLiveMap)

> Nếu dự án hữu ích, xin một ⭐ cho repository và chia sẻ để nhiều người biết tới dự án phi lợi nhuận này hơn.

## Nguồn telemetry

- IslePilot Network — `https://islepilot.eu`
- Tự nhận server hiện tại sau một lần đăng nhập Steam; không cần chọn DinoVietNam, Premium hay HoHo.
- Chỉ server đã cài plugin IslePilot mới cung cấp tọa độ và status.

Texture Gateway dùng bản EraGaming/MyIsleMap đóng gói trong app. Đây chỉ là ảnh nền local; telemetry và marker vẫn đến từ API IslePilot.

## Tính năng

- Minimap Gateway dùng texture EraGaming/MyIsleMap đóng gói local, không tải ảnh map qua mạng khi khởi động.
- Marker luôn giữ giữa viewport và dùng chung map Gateway cho mọi server Evrima tương thích.
- Tọa độ, yaw và status realtime từ WebSocket `/ows`; `/api/overlay/me` và `/api/overlay/map` làm baseline/fallback.
- Growth, Health, Stamina, Hunger và Water khi nguồn cung cấp trường tương ứng.
- Home chỉ có một Steam Login và tự nhận server IslePilot đang chơi.
- Token overlay được mã hóa bằng Windows DPAPI cho tài khoản Windows hiện tại; không lưu plaintext.
- Tự reconnect với backoff, giữ snapshot cuối và báo `RECONNECTING`/`DATA STALE` khi mạng yếu.
- HUD dọc, nền ngoài trong suốt, always-on-top, click-through và hỗ trợ phím tắt toàn cục.
- Auto-update qua GitHub Releases bằng Velopack.

## Phím tắt toàn cục

| Phím | Tác dụng |
|---|---|
| `Alt + cuộn lên` | Zoom in map |
| `Alt + cuộn xuống` | Zoom out map |
| `Alt + nút chuột giữa` | Ẩn / hiện map |
| `Alt + ]` | Ẩn block hướng dẫn phím tắt |
| `Ctrl + Shift + O` | Mở / khóa Edit Mode để kéo overlay |

Các phím tắt hoạt động kể cả khi game hoặc ứng dụng khác đang focus. Low-level mouse hook chỉ nhận tổ hợp có `Alt`, không inject DLL và không đọc memory game.

## Steam Login và quyền riêng tư

App mở luồng đăng nhập IslePilot/Steam trong Microsoft Edge WebView2 với profile riêng tại:

```text
%LocalAppData%\KLongDev\IsleLiveMap\WebView2
```

Callback `isle-overlay://` được bắt ngay bên trong WebView2. Isle Live Map không đăng ký hoặc chiếm protocol này trong Windows.

Overlay Bearer token được lưu tại `%LocalAppData%\KLongDev\IsleLiveMap` sau khi mã hóa bằng DPAPI CurrentUser. Token:

- Không được ghi vào log, source, `.env` hoặc JSON plaintext.
- Chỉ được gửi tới host cố định `https://islepilot.eu` và `wss://islepilot.eu/ows`.
- Chỉ dùng để đọc `/api/overlay/me`, `/api/overlay/map` và frame `live` từ `/ows`.
- Bị xóa khi người dùng đăng xuất hoặc API trả 401/403.

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
.\scripts\Package-Release.ps1 -Version 1.0.0
```

Output nằm trong `artifacts/distribution`.

## Kiến trúc

```text
TheIsleOverlay.App        Home, Steam WebView2 login, WPF overlay, updater, global shortcuts
TheIsleOverlay.Core       Telemetry session contract, reducer support, projection và heading
TheIsleOverlay.EraGaming  Adapter JSON API EraGaming
TheIsleOverlay.IslePilot  Bearer REST, WebSocket realtime, auth, reducer và DPAPI credential store
TheIsleOverlay.Tests      Unit/integration tests auth, transport, reducer, projection và heading
```

`ITelemetrySession` là ranh giới của UI. `MainWindow` chỉ nhận `TelemetrySnapshot`; vòng REST, WebSocket, reconnect và stale detection nằm ngoài cửa sổ.

Texture nền Gateway được nhúng vào ứng dụng. Các provider chỉ lấy telemetry như tọa độ, yaw và status để overlay vẽ lên texture local; chúng không cung cấp hoặc tải ảnh map.

## Giới hạn

- Realtime chỉ hoạt động trên server đã cài và bật plugin IslePilot.
- Mất Internet không ảnh hưởng texture map local nhưng telemetry sẽ chuyển `RECONNECTING` hoặc `DATA STALE`.
- IslePilot thay đổi endpoint, callback hoặc payload có thể yêu cầu cập nhật client.
- Texture Gateway local được cập nhật theo từng bản phát hành của ứng dụng khi map game thay đổi.
- Bản phát hành chưa được ký bằng chứng thư thương mại, vì vậy Windows SmartScreen có thể cảnh báo ở lần chạy đầu.

## License

[MIT](LICENSE) — sử dụng, kiểm tra và đóng góp tự do; vui lòng giữ thông báo bản quyền.
