# Isle Live Map 1.5.0

## Kích hoạt Pro rõ ràng hơn

- Modal xác minh Steam/Pro được viết lại với điểm nhấn **CHỈ TỪ 28K** ngay ở tiêu đề và khu vực license.
- Nội dung giải thích ngắn gọn: quyền Pro đi theo SteamID64, không giới hạn thiết bị và không tạo khóa theo máy.
- Dòng trạng thái trong modal cũng giữ thông tin giá để người dùng luôn thấy lựa chọn nâng cấp trong lúc chờ Steam xác minh.
- Khu vực Pro trên Home hiển thị rõ **KÍCH HOẠT PRO · CHỈ TỪ 28K** khi tài khoản chưa có quyền; giao diện Gold của tài khoản Pro vẫn giữ nguyên.

## Luồng khởi động gọn hơn

- Đã bỏ modal donate tự động khi mở ứng dụng.
- Modal hướng dẫn và modal release highlights 1.4.9 không thay đổi.
- DonateWindow và tài sản donate vẫn được giữ trong ứng dụng cho các luồng gọi chủ động trong tương lai.

## Tương thích và quyền truy cập

- Không thay đổi decoder, tracking, map, layout hoặc phân quyền Free/Pro.
- Free tiếp tục hoạt động độc lập; Player/AI Tracking Pro chỉ chạy khi entitlement Pro còn hiệu lực.
