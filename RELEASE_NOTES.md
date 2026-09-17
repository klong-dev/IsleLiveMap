# Isle Live Map 2.1.2

## Kênh thông báo Zalo chính thức

- Thay modal quảng cáo dịch vụ bằng lời mời tham gia kênh thông báo Isle Live Map trên Zalo dành cho cả Free và Pro.
- QR được tích hợp trực tiếp trong ứng dụng để người dùng quét, nhận thông báo sớm, góp ý tính năng và báo lỗi trực tiếp cho Long.
- Giữ thời gian đọc tối thiểu 5 giây; sau đó người dùng có thể chọn **Đóng** hoặc **Không hiển thị lại nữa**.
- Lựa chọn không hiển thị lại được lưu bền vững trên máy; chỉ bấm Đóng thì lời mời vẫn xuất hiện ở lần mở app sau.
- Loại bỏ video YouTube, quảng cáo dịch vụ và các icon game khỏi modal cũ để giao diện tập trung vào cộng đồng hỗ trợ Live Map.

# Isle Live Map 2.1.1

## Hotfix khôi phục stats ổn định

- Khôi phục toàn bộ pipeline stats và lựa chọn nguồn server đã ổn định ở bản 2.0.3.
- Tạm rút decoder stats trực tiếp thử nghiệm của 2.1.0 do có thể thiếu Health/Max, Stamina/Max, sai loài hoặc sai Growth trên một số loài và server.
- Giữ nguyên bản sửa Npcap của 2.0.3: sau khi cài driver, app có thể kiểm tra và sử dụng lại ngay trong phiên hiện tại.
- Player/AI Tracking, Live Map Pro và các tính năng trước 2.1.0 tiếp tục hoạt động như bản 2.0.3.

# Isle Live Map 2.0.3

## Nạp Npcap ngay trong phiên hiện tại

- Không còn yêu cầu đóng/mở lại app sau khi bộ cài Npcap hoàn tất.
- Probe Npcap dùng native `pcap_findalldevs` độc lập, không khởi tạo SharpPcap quá sớm rồi giữ lỗi trong process.
- Sau khi cài xong, app kiểm tra lại service, thư viện và adapter rồi tiếp tục mở map ngay.
- Nếu máy thật sự thiếu quyền hoặc driver, thông báo chuyển sang hướng dẫn thử lại/cài lại rõ ràng hơn.

# Isle Live Map 2.0.2

## Chọn nguồn server ngay từ Home

- Modal cập nhật có thêm trang đầu giới thiệu nguồn **Origin x5** và **Gacha**.
- Chọn nguồn ngay bên dưới nút **Mở Map** để dùng đúng luồng stats của server; IslePilot vẫn là nguồn mặc định.
- Bổ sung logo Origin và Gacha vào trang hướng dẫn để nhận diện nhanh hơn.

## Sửa nhận diện thư mục The Isle qua Steam

- Không còn phụ thuộc duy nhất vào khóa `Steam App 376210` trong Windows Registry.
- Tự đọc Steam root từ Registry, biến môi trường và các vị trí Steam chuẩn.
- Tự quét toàn bộ thư viện trong `libraryfolders.vdf`, bao gồm game cài ở ổ đĩa/thư viện phụ.
- Đọc `installdir` và `buildid` từ `appmanifest_376210.acf`, có kiểm tra đường dẫn an toàn trước khi dùng.
- Giữ nguyên kiểm tra build và không ghi bất kỳ file game nào nếu không xác định chắc chắn cài đặt.
- Sổ tay Mutation `Alt + U` là dữ liệu cục bộ, có thể mở ngay khi Live Map chạy; không còn bị chặn bởi trạng thái xác nhận server tạm thời.

# Isle Live Map 2.0.1

## Sửa kiểm tra Npcap trên Windows

- Nhận diện Npcap bằng cả thư viện native và danh sách adapter thực tế, không chỉ dựa vào trạng thái service.
- Tự tìm `wpcap.dll` và `Packet.dll` trong các thư mục System32/SysWOW64 chuẩn, đồng thời nạp native library ổn định hơn.
- Làm mới danh sách adapter thay vì dùng singleton có thể bị stale sau khi người dùng cài Npcap trong lúc app đang mở.
- Phân biệt rõ thiếu DLL, không có adapter, thiếu quyền và lỗi native để người dùng biết cách xử lý.
- Nếu cài đặt thành công nhưng process hiện tại chưa nạp được DLL, app báo cần mở lại thay vì báo cài đặt thất bại.

# Isle Live Map 2.0.0

## Stats, Việt hóa và thao tác nhanh

- Stats dino cá nhân tiếp tục lấy từ IslePilot; server Gacha có adapter API/WebSocket chính thức riêng và không làm ảnh hưởng GPS/Live Map.
- Thêm lựa chọn English / Tiếng Việt và sổ tay tra cứu 43 Mutation bằng `Alt + U`; lớp hỗ trợ không inject, không đọc memory và không thay native UI của game.
- Set point Pro hỗ trợ mở map lớn bằng `Alt + M`, nhập tọa độ XYZ, click mốc cũ để đổi icon hoặc xóa bằng thùng rác / `Delete`.
- Bổ sung cài đặt phím tắt có kiểm tra xung đột; `Ctrl + Shift + O` mở Edit Mode, `Alt + P` ẩn/hiện toàn HUD.
- Từng block Map, Status, Team và Prime có thể bật/tắt, resize độc lập; layout, hình map và trạng thái block được lưu lại.
- Modal cập nhật 2.0.0 gồm 5 trang, phân biệt rõ Free/Pro; checkbox “Không hiển thị lại” chỉ xuất hiện sau khi xem đến trang cuối.

## Nền tảng từ 1.5.2

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
- Modal cập nhật 1.5.3 gồm 5 trang; tùy chọn “Không hiển thị lại” chỉ xuất hiện ở trang cuối.

## Kỳ vọng sử dụng

Đây là tối ưu pipeline và UI; FPS thực tế còn phụ thuộc GPU, driver, Npcap và cấu hình máy. Bản này không cam kết một mức FPS cố định.
