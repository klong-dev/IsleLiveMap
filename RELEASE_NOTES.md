# Isle Live Map 1.4.7

## Player Tracking Pro nhanh và đầy đủ hơn

- Pro Agent khởi động ngay sau khi tài khoản Pro được xác minh tại Home, giúp giữ lại creation packet trước khi người dùng mở map.
- Sửa parser Unreal Iris theo destroy-list hiện tại của game; các player batch nằm sau destroy entry không còn bị bỏ sót.
- Player đã được xác minh và vẫn còn trong replication scope không còn tự biến mất chỉ vì đứng yên lâu.
- Bổ sung phục hồi late-attach từ ongoing actor data đã kiểm chứng để giảm tình trạng vào server nhưng map trống hoặc tải player chậm.
- Giữ marker ổn định qua các replication batch thưa, giảm nhấp nháy và rơi chấm tạm thời.

## Chính xác và an toàn hơn

- Packet-only recovery chỉ chấp nhận actor có bằng chứng creation của đúng loài playable; manager, replicated array, AI và local player không thể trở thành Player giả.
- Xử lý cả packet chỉ chứa destroy event để marker rời tầm nhìn được gỡ đúng thời điểm.
- Cache phiên mới ghi nhớ destroy barrier, không hồi sinh marker cũ sau restart.
- Hỗ trợ nâng cấp trực tiếp từ cache của 1.4.6 bằng cửa sổ phục hồi ngắn, tránh vừa cập nhật xong phải chờ dựng lại toàn bộ player.

## Ổn định

- Giữ nguyên toàn bộ tối ưu giảm giật camera/game của 1.4.6.
- Luồng telemetry và UI tiếp tục dùng snapshot mới nhất; không tích lũy frame cũ.
- Cải thiện dữ liệu chẩn đoán để có thể đối chiếu packet, tracker và marker khi cần hỗ trợ.

## Phạm vi cập nhật

- Cải thiện Player/AI Tracking chỉ hoạt động với tài khoản Pro hợp lệ.
- Người dùng Free vẫn nhận các sửa lỗi host và không thể truy cập decoder hoặc telemetry Pro.
