# Isle Live Map 1.4.2

## Home Pro Gold–Black

- Tài khoản Pro còn hiệu lực nhận giao diện Home vàng Gold–đen riêng, đồng nhất từ tiêu đề, nút, viền đến trạng thái hoạt động.
- Nút chính đổi thành **MỞ MAP PRO** để phân biệt rõ trải nghiệm premium với Live Map Free.
- Trạng thái quyền đổi từ **XÁC MINH LẠI** sang **ĐÃ XÁC MINH** và khóa thao tác sau khi Agent đã sẵn sàng; muốn đổi tài khoản, người dùng chủ động chọn **ĐĂNG XUẤT PRO**.
- Tài khoản đã có Pro không còn thấy modal mời kích hoạt Pro khi mở ứng dụng.
- Khi quyền có thời hạn kết thúc, Home tự trở lại theme Free và dừng cấp nguồn Player + AI Tracking Pro.

## Player + AI Tracking ổn định hơn

- Phát hành Pro Agent 0.3.19 với pipeline packet đã được kiểm thử qua restart Live Map, reconnect và đổi nhiều server.
- Giữ bootstrap replication lâu hơn trong lúc game tải server để giảm thiếu player sau khi connect chậm.
- Ổn định roster trước packet đến sai thứ tự, loại PlayerState ghost và hạn chế chấm player nhấp nháy.
- Bổ sung nhận diện Hypsilophodon bằng nhãn **Hypsi** từ protocol/archetype đã được xác minh bằng packet.
- Phạm vi player và AI Pro tiếp tục hỗ trợ tối đa 1 km; chính sách AI mục tiêu vẫn được giữ nguyên.

## Trải nghiệm rõ ràng hơn

- Nút đăng xuất IslePilot giờ có nhãn **ĐĂNG XUẤT STEAM**, không còn xuất hiện như một đường điều khiển trống.
- Nội dung trạng thái Home thay đổi theo đúng Free, Pro đang tải, Pro đã sẵn sàng, offline license hoặc hết hạn.
- GPS và dino stats Free vẫn hoạt động độc lập; mã nguồn Pro tiếp tục được phân phối bằng Agent riêng có chữ ký và chỉ tải cho SteamID64 có quyền.
