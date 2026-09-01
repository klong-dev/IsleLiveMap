# Isle Live Map 1.4.3

## Map linh hoạt hơn cho mọi người dùng Free

- Giữ **ALT + kéo chuột trái** để di chuyển minimap tự do và **ALT + chuột phải** để đưa map trở lại chính giữa, tiếp tục bám GPS.
- **Ctrl + Shift + O** nay cho phép kéo Map, Status, Team và Prime thành từng block độc lập thay vì cả cụm.
- Cải thiện pipeline Npcap/Iris với sequence-gap detection, partial-bunch assembly, cache state và recovery từ nhiều replication batch để giảm mất dấu sau reconnect hoặc đổi server.

## Tactical map mới cho Pro

- **ALT + M** mở bản đồ lớn; click vào vị trí muốn đặt set point và chọn biểu tượng phù hợp.
- Set point có thể chia sẻ realtime giữa các đồng đội trong cùng nhóm sinh tồn.
- Bổ sung lớp Migration Zone, Patrol Zone, vùng thức ăn local và Player heatmap khi server IslePilot hỗ trợ.
- Ghép các mảnh ongoing replication để dựng player sớm hơn, tăng độ đầy đủ và độ ổn định của Player + AI Tracking.

## Hướng dẫn update dạng từng bước

- Modal mới chia nội dung thành 4 trang ngắn, phân biệt rõ tính năng **Free** và **Pro**, kèm ảnh thao tác thật.
- Modal phím tắt được cập nhật đầy đủ cho các thao tác map mới.
- Sau khi xem hết, người dùng có thể chọn **Không hiển thị lại thông báo này** cho riêng phiên bản 1.4.3.

## Kiểm duyệt quyền Pro

- Player/AI tracking, Zone, full-map set point và ping nhóm chỉ khởi tạo khi SteamID64 có Pro access còn hiệu lực.
- Free không đăng ký hotkey Pro, không load dữ liệu Zone và không nhận marker/ping Pro.
- Khi Pro hết hạn trong lúc Live Map đang mở, các lớp Pro được đóng và gỡ khỏi giao diện mà không ảnh hưởng GPS/stats Free.
