# Isle Live Map 1.1.0

- Thêm **Nhóm sinh tồn** bằng mã mời 6 ký tự, không cần tài khoản nhóm và không lưu dữ liệu phiên.
- Hiển thị marker, tên và hướng quay của đồng đội đang ở cùng server trên minimap.
- Thêm hàng status HP, Đói và Nước cho từng đồng đội; nhận biết mất tín hiệu/khác server.
- Dùng relay realtime tại `isle-relay.klong.dev`, tự heartbeat và reconnect; member token chỉ giữ trong RAM.
- Thay login cookie từng server bằng một Steam Login IslePilot và tự nhận server đang chơi.
- Nhận tọa độ, yaw và vitals realtime qua WebSocket `/ows`; tự phát hiện stale và reconnect.
- Dùng texture Gateway đóng gói local, không tải ảnh map khi mở overlay.
- Mã hóa overlay token bằng Windows DPAPI CurrentUser và tự xóa khi phiên IslePilot hết hạn.
