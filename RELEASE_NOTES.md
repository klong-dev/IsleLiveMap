# Isle Live Map 1.4.6

## Hotfix giảm giật camera và game

- Overlay không còn xử lý mọi chuyển động chuột khi người dùng chỉ đang xoay camera trong game.
- Global mouse hook và Raw Input chỉ hoạt động khi giữ **ALT** hoặc đang kéo bản đồ.
- Thay đổi hướng camera chỉ cập nhật mũi tên GPS, không dựng lại toàn bộ map.
- Player/AI marker và trạng thái team không render lại nếu dữ liệu thực tế không đổi.
- Tắt chẩn đoán Iris sequence khỏi luồng production để giảm chi phí xử lý packet nền.

## Phạm vi cập nhật

- Hotfix áp dụng cho cả Free và Pro.
- Không thay đổi decoder GPS, quyền truy cập Pro hoặc các tính năng đã phát hành trong 1.4.5.
