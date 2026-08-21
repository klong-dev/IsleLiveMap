# Chưa phát hành

- Thay login cookie theo từng server trên Home bằng một Steam Login IslePilot.
- Tự nhận server hỗ trợ và nhận tọa độ, yaw, vitals qua `/me`, `/map` và WebSocket `/ows`.
- Lưu overlay token bằng Windows DPAPI CurrentUser; không đăng ký protocol `isle-overlay` trong Windows.
- Tự reconnect, phát hiện dữ liệu stale sau bốn giây và xóa token khi API trả 401/403.

# Isle Live Map 1.0.3

- Dùng texture Gateway EraGaming/MyIsleMap đóng gói local cho toàn bộ nguồn telemetry.
- Loại bỏ việc tải texture map online khi mở overlay, tránh lỗi bản đồ trên kết nối mạng yếu.
- Giữ nguyên API tọa độ, marker, yaw và status của EraGaming, DinoVietNam, DinoVietNam Premium và HoHo.
