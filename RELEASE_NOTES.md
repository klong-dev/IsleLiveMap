# Isle Live Map 1.3.0

- Chỉ còn một nút **MỞ MAP**: tọa độ và hướng quay lấy trực tiếp từ game, còn HP, đói, nước, growth và nhiệm vụ vẫn lấy từ nguồn server đã chọn.
- Không còn phụ thuộc tốc độ cập nhật marker của website; hỗ trợ cả server không cung cấp tọa độ qua live map.
- Sửa marker giật hoặc văng vị trí khi Pteranodon bay bằng cách lọc saved move, timestamp cũ và burst packet.
- Chặn MỞ MAP trong lúc kiểm tra/tải cập nhật; nếu có bản mới, app yêu cầu cập nhật trước để không restart giữa lúc đang chơi.
- Kiểm tra update có timeout 15 giây và tự cho dùng khi GitHub tạm thời không phản hồi.
- Modal donate có đếm ngược 7 giây trước khi cho phép đóng.

> Tọa độ trực tiếp cần cài Npcap. Isle Live Map chỉ đọc movement packet trên chính máy người chơi, không inject và không đọc bộ nhớ game.
