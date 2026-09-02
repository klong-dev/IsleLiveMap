# Isle Live Map 1.4.5

## Giảm lag cho Free và Pro

- Giới hạn render UI ở **20 Hz** và luôn lấy telemetry snapshot mới nhất, không xếp hàng các frame cũ trong packet burst.
- Heatmap, zone và food geometry chỉ được dựng lại khi dữ liệu thực sự thay đổi.
- Kết quả đo beta trên máy phát triển giảm CPU overlay từ khoảng **5,8–6,1% xuống 2,8%** khi hiển thị.
- Tăng mức zoom tối đa của minimap thêm **25%** (từ 9x lên 11,25x).

## Đặt mốc bằng tọa độ cho Pro

- Nhấn **ALT + M**, dán đủ tọa độ X, Y, Z theo định dạng game, ví dụ `-238,743.261, 88,587.6, 28,509.171`, rồi nhấn **Enter** hoặc **ĐẶT MỐC**.
- Mốc tọa độ dùng đúng calibration Gateway và cùng pipeline với mốc tạo bằng click.
- Khi đang trong nhóm sinh tồn, mốc tọa độ tiếp tục được chia sẻ realtime cho đồng đội.

## Kết nối team rõ ràng hơn

- Create/join team có deadline 12 giây bao trùm cả REST bootstrap và SignalR handshake.
- Nếu relay hoặc mạng không phản hồi, UI thoát trạng thái chờ, hiển thị lỗi và mở lại nút để người dùng thử lại.
- Production relay đã được kiểm tra health và smoke test REST + SignalR trước khi phát hành.

## Quyền truy cập

- Tối ưu render, zoom và cải thiện lỗi team có hiệu lực cho mọi người dùng.
- ALT + M, nhập tọa độ, chia sẻ set point, Player/AI tracking và map layers vẫn chỉ khởi tạo khi Pro access còn hiệu lực.
