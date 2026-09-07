# Isle Live Map 1.5.1

## Khôi phục đúng modal Kích hoạt Pro

- Tạo lại modal quảng bá Pro độc lập với cửa sổ đăng nhập Steam và modal thông báo phiên bản.
- Nhấn mạnh **CHỈ TỪ 28K**, quyền theo SteamID64, Player/AI Tracking, Tactical Map và hỗ trợ tất cả server.
- Modal được hiển thị ở mỗi lần mở app cho tài khoản Free, chưa đăng nhập hoặc đã hết hạn Pro.
- Tài khoản Pro còn hiệu lực và lifetime không bị làm phiền bởi modal quảng bá.

## Luồng khởi động gọn hơn

- Đã xóa hoàn toàn DonateWindow và tài nguyên donate khỏi bản build, không chỉ bỏ lời gọi startup.
- Modal hướng dẫn và modal release highlights 1.4.9 không thay đổi.

## Tương thích và quyền truy cập

- Không thay đổi decoder, tracking, map, layout hoặc phân quyền Free/Pro so với 1.5.0.
- Free tiếp tục hoạt động độc lập; Player/AI Tracking Pro chỉ chạy khi entitlement Pro còn hiệu lực.
