# Isle Live Map

Ứng dụng overlay miễn phí, mã nguồn mở cho **The Isle Evrima**. Isle Live Map chạy ngoài process game, luôn nổi trên màn hình và hiển thị minimap Gateway cùng telemetry cá nhân từ các website cộng đồng được người chơi tự chọn.

[Facebook K-Long.dev](https://www.facebook.com/klong.dev) · [YouTube Long Hoàng Kim](https://www.youtube.com/@longhoangkim2246) · [GitHub](https://github.com/klong-dev/IsleLiveMap)

> Nếu dự án hữu ích, xin một ⭐ cho repository và chia sẻ để nhiều người biết tới dự án phi lợi nhuận này hơn.

## Nguồn đang hỗ trợ

- EraGaming — `https://eragamingvn.net`
- DinoVietNam — `https://dinovietnam.islepilot.eu`
- DinoVietNam Premium — `https://dinovietnampremium.islepilot.eu`

## Tính năng

- Minimap Gateway độc lập với website, marker luôn giữ giữa viewport.
- Yaw server cho hai nguồn IslePilot; EraGaming suy hướng di chuyển từ các mẫu tọa độ.
- Growth, Health, Stamina, Hunger và Water khi nguồn cung cấp trường tương ứng.
- Home chọn nguồn và cửa sổ đăng nhập WebView2 riêng của ứng dụng.
- Cookie do WebView2 lưu trong profile người dùng; source code không chứa cookie và app không đọc kho cookie Chrome/Edge.
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

## Đăng nhập và quyền riêng tư

Khi chọn nguồn, app mở website thật trong Microsoft Edge WebView2 với profile riêng tại:

```text
%LocalAppData%\KLongDev\IsleLiveMap\WebView2
```

App chỉ yêu cầu đúng cookie của hostname đã chọn:

- EraGaming: `era_session`
- IslePilot: `islepilot_player`

Cookie được chuyển trực tiếp vào request telemetry trong memory, không ghi vào source, `.env`, log hoặc GitHub. Đây là lựa chọn có chủ đích thay cho việc giải mã/đọc trộm cookie của trình duyệt mặc định.

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
TheIsleOverlay.App        Home, WebView2 login, WPF overlay, updater, global shortcuts
TheIsleOverlay.Core       Telemetry contract, vital math, projection và heading
TheIsleOverlay.EraGaming  Adapter JSON API EraGaming
TheIsleOverlay.IslePilot  Adapter markers JSON + semantic /me parser cho IslePilot
TheIsleOverlay.Tests      Unit tests provider, parser, projection và heading
```

`ITelemetryProvider` là ranh giới mở rộng. Muốn thêm server mới chỉ cần tạo source definition/provider; UI không phụ thuộc schema riêng của website.

## Giới hạn

- `POLL 2S` là nhịp client kiểm tra API, không đảm bảo plugin/server tạo dữ liệu mới mỗi hai giây.
- Live map chính thức của IslePilot hiện poll marker mỗi 15 giây; yaw có thể giữ nguyên qua nhiều tick.
- `/me` của IslePilot không cung cấp Stamina nên HUD hiển thị `—`.
- Parser HTML cần cập nhật nếu IslePilot thay đổi nhãn hoặc cấu trúc trang.
- Bản phát hành chưa được ký bằng chứng thư thương mại, vì vậy Windows SmartScreen có thể cảnh báo ở lần chạy đầu.

## License

[MIT](LICENSE) — sử dụng, kiểm tra và đóng góp tự do; vui lòng giữ thông báo bản quyền.
