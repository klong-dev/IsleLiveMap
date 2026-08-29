# Isle Live Map 1.4.1

## Khôi phục dino stats ổn định

- Growth, Health, Stamina, Hunger và Water tiếp tục được đồng bộ từ IslePilot thay vì decoder inbound đang thử nghiệm.
- GPS và hướng vẫn đọc trực tiếp từ game bằng Npcap, vì vậy Live Map cơ bản tiếp tục hoạt động trên mọi server.
- Nút mở Live Map sẽ dùng phiên Steam/IslePilot đã mã hóa hoặc hướng dẫn đăng nhập nếu chưa có phiên.
- Decoder inbound vitals được giữ ngoài production để tiếp tục nghiên cứu Iris/GAS mà không hiển thị giá trị suy đoán cho người dùng.

## Live Map trên mọi server

- GPS của Live Map đọc trực tiếp dữ liệu game, không còn phụ thuộc website map hoặc plugin riêng của từng server.
- Cải thiện pipeline Npcap, buffer packet và phục hồi replication để giảm mất marker khi reconnect, đổi khu vực hoặc gặp packet burst.
- Ổn định GPS khi di chuyển, bay và respawn; hạn chế vị trí giả, saved move cũ và các bước nhảy marker bất thường.

## Isle Live Map Pro

- Thêm đăng nhập Steam và quyền Pro gắn theo SteamID64, không giới hạn thiết bị.
- Hiển thị player và AI gần bạn trên minimap, phân màu theo cùng loài, ăn thịt, ăn cỏ và AI.
- Bổ sung nhãn loài cùng cân nặng rút gọn để nhận diện mục tiêu nhanh hơn.
- Pro Agent được tải và kiểm tra chữ ký riêng; tính năng Pro không được đóng gói trực tiếp trong bản Free.
- Thêm màn giới thiệu Pro với ảnh Live Map thực tế và nút **KÍCH HOẠT PRO NGAY** mở trang mua Pro.

## Trải nghiệm và độ ổn định

- Tự khởi động lại capture khi process game hoặc UDP endpoint thay đổi sau reconnect.
- Giữ trạng thái telemetry qua nhiều replication batch và loại marker cũ khi session thay đổi.
- Tiếp tục hỗ trợ tự động kiểm tra, tải và cài đặt bản cập nhật qua Isle Live Map.

> Bản Free vẫn dùng được Live Map và GPS cơ bản. Player/AI tracking, phân loại loài và cân nặng là quyền Pro theo tài khoản Steam.
