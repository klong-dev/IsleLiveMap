# Isle Live Map 1.5.2

## Overlay nhẹ hơn, ổn định hơn

- Khung GATEWAY/LIVE gọn hơn, không chiếm thêm diện tích khi bật Pro.
- Giảm các lần vẽ lại UI, bỏ hiệu ứng marker tốn GPU, giữ snapshot mới nhất và chuyển ghi log chẩn đoán khỏi luồng giao diện.
- GPS và hướng không còn phụ thuộc nhịp render ưu tiên cao của WPF khi overlay đang khóa.
- Zone, food, heatmap, nhãn và danh sách nhiệm vụ chỉ cập nhật khi dữ liệu thực sự thay đổi.
- Layout, map tròn/vuông, kích thước từng block và trạng thái ẩn nhiệm vụ được ghi nhớ cho lần mở sau.

## Pro Tracking dễ hiểu và tự phục hồi

- Hiển thị trạng thái rõ khi đang chờ game, chờ server, mở adapter, chờ packet hoặc bộ quét gặp lỗi.
- Pro Agent tự khởi động lại với thời gian chờ tăng dần nếu tiến trình capture dừng ngoài dự kiến.
- Npcap thử mọi adapter phù hợp thay vì có thể bỏ sót adapter thật trên máy dùng VPN hoặc adapter ảo.
- Sửa tình trạng tích tụ tác vụ chờ trong pipeline packet khi một luồng dữ liệu im lặng lâu.
- Tiếp tục giữ bootstrap reconnect để không bỏ creation packet; marker vẫn được dựng từ các batch Iris đã xác minh.

## Free và quyền riêng tư

- Free vẫn có GPS, stats, minimap, zone/food local, chỉnh layout và ghi nhớ giao diện như trước.
- Player/AI Tracking, phân loại loài và cân nặng chỉ được kích hoạt khi quyền Pro còn hiệu lực.
- App chỉ hiển thị mục tiêu mà game đã gửi trong phạm vi replication; đây không phải wallhack toàn bản đồ.

## Modal Kích hoạt Pro

- Tài khoản Pro còn hiệu lực hoặc lifetime không bị làm phiền bởi modal quảng bá.
- Tài khoản Free, chưa đăng nhập hoặc đã hết hạn Pro sẽ nhận lại modal Kích hoạt Pro ở lần mở app tiếp theo.
- Nếu quyền hết hạn trong lúc app đang chạy, Home tự chuyển về Free và hiện lời mời kích hoạt lại ngay lúc đó.
- Modal cập nhật 1.5.2 gồm 5 trang; tùy chọn “Không hiển thị lại” chỉ xuất hiện ở trang cuối.

## Kỳ vọng sử dụng

Đây là tối ưu pipeline và UI; FPS thực tế còn phụ thuộc GPU, driver, Npcap và cấu hình máy. Bản này không cam kết một mức FPS cố định.
