# Isle Live Map 2.4.0

## Nhóm sinh tồn

- Thêm hộp thoại chọn quy mô phòng 3, 7, 10 hoặc 21 người (tính cả chủ phòng); phòng 10/21 người yêu cầu Pro.
- Gửi quy mô đã chọn tới relay và kiểm tra quy mô được cấp; không tạo nhầm phòng sai giới hạn.
- Sửa bố cục hộp thoại để các lựa chọn luôn nằm trong khung, không bị che hoặc cắt.

## SDVN và kết nối server riêng

- Thêm một nút SDVN trên Home với logo riêng; chọn SDVN #1, #2 hoặc #3 trong cùng một cửa sổ và nhớ server gần nhất.
- Phiên SDVN được mã hóa bằng DPAPI, tách riêng từng tenant; không dùng chung cookie với DinoVietnam hoặc HoHo. SDVN #4 tạm ẩn do lỗi TLS phía website.
- Khôi phục thao tác thật cho nút GACHA và ORIGIN: đăng nhập, kiểm tra phiên và mở overlay; hiển thị trạng thái/lỗi ngay trên Home.
- Stats Origin được cập nhật theo nhịp mục tiêu 2,5 giây khi API đáp ứng; Prime chạy độc lập, không giữ HP/Hunger/Thirst/Stamina chờ theo.
- Command Origin chậm được tiếp tục theo cùng mã lệnh, có backoff/rate-limit; dữ liệu cũ được đánh dấu và chỉ giữ tối đa 10 giây, không tự gia hạn vô hạn.
- Các nút server cùng tuân theo bước kiểm tra cập nhật, Npcap và chống mở nhiều overlay; giữ nguyên GPS local, quyền Pro và relay nhóm.

# Isle Live Map 2.3.6

## Auto update và server riêng trong Pro

- Khi đang kiểm tra phiên bản, nút `MỞ MAP PRO` chuyển thành `ĐANG KIỂM TRA CẬP NHẬT` và bị khóa để không mở map trước khi có kết quả.
- Trong lúc tải bản mới, nút hiển thị `ĐANG CẬP NHẬT v<version>`; sau khi tải xong, text trở lại `MỞ MAP PRO` nhưng map vẫn chờ khởi động lại nếu bản cập nhật yêu cầu áp dụng.
- Làm lại cụm nút GACHA và ORIGIN 5x trong Pro: cùng lưới, logo lớn hơn, tên trắng căn giữa, nền pastel đặc theo nhận diện server và vùng bấm cân đối hơn.

# Isle Live Map 2.3.5

## Auto update và GPS local

- Launcher kiểm tra bản cập nhật trước khi cho mở Live Map. Nếu kiểm tra mạng thất bại, app báo rõ nhưng vẫn cho người dùng tiếp tục sử dụng; nếu tải được bản mới, map chỉ mở sau khi khởi động lại hoàn tất cập nhật.
- Khôi phục prewarm Npcap ngay khi launcher khởi động và probe Npcap đã sẵn sàng, giúp bắt process, adapter, UDP handshake và movement packet trước khi người dùng bấm mở map.
- Giữ yêu cầu movement local phải mới để không hiển thị vị trí cũ như vị trí hiện tại; khi đang chờ packet, launcher/telemetry hiển thị trạng thái chờ thay vì giả dữ liệu live.

# Isle Live Map 2.3.3

## Pro Agent và Live Map

- Sửa luồng mở map để chờ hoàn tất việc đọc quyền Pro trước khi tạo overlay, tránh phiên map nhận nhầm trạng thái Free khi launcher vẫn đang khởi tạo.
- Truyền nguồn Pro Agent thực tế vào phiên telemetry cùng với quyền Pro, khôi phục Player/AI tracking và phân loại marker trên overlay cho tài khoản Pro.
- Giữ nguyên cơ chế báo trạng thái Agent khi Agent chưa sẵn sàng; không thay đổi logic tracking trong đợt phát hành này.

# Isle Live Map 2.3.2

## Launcher và Npcap

- Khôi phục bước kiểm tra Npcap trước khi mở Live Map; nếu thiếu hoặc chưa sẵn sàng, launcher mở lại cửa sổ hỗ trợ cài đặt thay vì đi thẳng vào đăng nhập.
- Kiểm tra lại Npcap sau khi cài trong cùng phiên, chỉ tiếp tục mở map khi thư viện và adapter đã sẵn sàng.
- Cập nhật launcher workspace: nút server riêng có nền pastel rõ ràng, QR hỗ trợ cộng đồng lớn hơn, nhập tên nhóm qua hộp thoại riêng và nút lưu phím tắt chỉ bật khi có thay đổi.
- Sửa hiển thị phiên bản trên title bar để khớp với bản phát hành.

# Isle Live Map 2.3.1

## Nhóm sinh tồn và hỗ trợ cộng đồng

- Phát hành workspace nhóm sinh tồn với phòng Free tối đa 7 người và phòng Pro tối đa 21 người.
- Relay xác thực entitlement Pro bằng JWT đã ký trước khi cấp dung lượng 21 người; request tự khai Pro không thể nâng giới hạn.
- Thêm tab liên hệ/báo lỗi, QR Zalo và luồng đăng nhập/đăng ký Pro inline trong launcher.

# Isle Live Map 2.3.0

## Nhóm sinh tồn và hỗ trợ cộng đồng

- Thêm tab `NHÓM SINH TỒN` ngay sau Trang chủ với luồng tạo phòng, vào phòng, copy mã mời, xem trạng thái relay và rời phòng trong cùng workspace.
- Phòng Free có giới hạn 7 người tính cả chủ phòng; phòng Pro có giới hạn 21 người. Client truyền tier/quota hint qua contract relay; relay production phải xác thực entitlement server-side trước khi cấp `MaxMembers`.
- Thêm tab `LIÊN HỆ & BÁO LỖI` trước Hướng dẫn với Facebook Hoàng Kim Long, Zalo 0705 8787 81 và QR nhóm Góp ý - Báo lỗi.
- Trang Pro đưa `ĐĂNG NHẬP / XÁC MINH` lên đầu, kèm `ĐĂNG KÝ PRO` mở https://isle.klong.dev.
- Việt hóa trạng thái nhóm, tier phòng và thông báo lỗi chính trong launcher.

# Isle Live Map 2.2.7

## Launcher workspace dễ đọc hơn

- Làm lại các nút server riêng trên Trang chủ với nền màu riêng, logo và tên server căn giữa trong đúng vùng nút: GACHA xanh lá nhạt, ORIGIN 5x xanh dương nhạt.
- Tinh chỉnh nền server thành màu pastel đặc, mờ nhạt nhẹ và dễ nhận biết trên ảnh nền; không còn hiệu ứng xuyên thấu làm mất màu nút.
- Rút gọn nhãn quyền Pro ở sidebar thành `PRO ĐANG BẬT` để không tràn, vẫn giữ tên trợ năng đầy đủ.
- Tăng độ rộng rail ghi chú phiên bản, tăng tương phản, cỡ chữ và line-height; nội dung mở rộng dễ quét hơn.
- Làm lại trang `PHÍM TẮT`: mỗi shortcut là một hàng riêng có hierarchy rõ, ô nhập nhận tổ hợp phím trực tiếp, trạng thái `HỢP LỆ` / `BỊ TRÙNG` / `CHƯA GÁN` / `KHÔNG HỢP LỆ`, nút mặc định gọn và footer lưu thay đổi.
- Việt hóa toàn bộ nhãn thao tác mới và giữ các thay đổi trong workspace inline, không mở modal shortcut.
- Đồng bộ nhãn Pro ngắn gọn trên title bar với sidebar, tránh lặp hoặc tràn trạng thái.

# Isle Live Map 2.2.3

## Bản đồ nước tùy biến và HUD dễ đọc hơn

- Tích hợp nền nước ngọt do người dùng vẽ lại tại \u0060GatewayMapWater.jpg\u0060; lớp Water bật mặc định và có thể bật/tắt để đổi qua lại với bản đồ thường trên minimap và bản đồ \u0060Alt + M\u0060.
- Khi bật Water, bản đồ chỉ dùng phần nước đã tô sáng trong ảnh nền; bỏ toàn bộ vòng tròn/glow marker nước chồng lên minimap và bản đồ lớn.
- Làm lớn inspector \u0060LỚP BẢN ĐỒ\u0060, typography, icon và vùng bấm để thao tác layer rõ ràng hơn khi đổi trạng thái.
- Thu gọn minimap: bỏ khối \u0060PLAYER / AI\u0060, nền của \u0060FOLLOW · GPS/FREE\u0060, và block Heading; thêm chỉ báo hướng \u0060Đ T N B\u0060 màu đỏ đậm, in đậm.
- Compass minimap hiển thị bốn ký tự theo đúng vị trí địa lý: Bắc ở trên, Đông bên phải, Nam ở dưới và Tây bên trái.
- Không hiển thị player/loài cũ từ IslePilot khi game chưa chạy; telemetry local yêu cầu chuyển động mới từ phiên game hiện tại.
- Đồng bộ cách hiển thị layer giữa minimap và \`Alt + M\`, gồm water glow, zone/AI label và kích thước icon tài nguyên.

# Isle Live Map 2.2.2

## Edit Mode dễ nhìn hơn

- Làm lớn và tăng tương phản bốn nút `HOME`, `PHÍM`, `MỐC` và `LỚP BẢN ĐỒ` để dễ nhận biết trên overlay.
- Bổ sung icon, nền màu theo nhóm chức năng, chữ đậm và trạng thái focus/hover rõ ràng hơn.
- Nút `LỚP BẢN ĐỒ` đổi màu khi inspector đang mở; trạng thái khóa hiển thị trực tiếp bằng `KHÓA`/`ĐÃ KHÓA`.
- Thanh công cụ mới vẫn dùng các luồng cũ, có nút `MỐC` dự phòng cho trường hợp Windows chiếm `Alt + M`.

# Isle Live Map 2.2.1

## Hotfix nhóm sinh tồn ngang quyền

- Xác nhận nhóm không có leader đặc quyền: mọi thành viên đều nhận cùng snapshot, stats, minimap marker và team ping của tất cả thành viên còn lại.
- Sửa trường hợp máy thành viên chưa đọc được endpoint local: marker đồng đội vẫn hiển thị với trạng thái `CHỜ SERVER`; chỉ ẩn khi có bằng chứng chắc chắn hai người đang ở khác server.
- Freshness của stats/marker dùng thời điểm client nhận telemetry thay vì timestamp từ đồng hồ Windows của máy khác, tránh máy lệch giờ coi toàn bộ đồng đội là stale.
- Telemetry không đổi nhưng nguồn vẫn đang hoạt động sẽ được refresh tối đa một lần mỗi 5 giây; không còn trường hợp người đứng yên/AFK biến mất khỏi máy người mới vào nhóm.
- Heartbeat không làm mới giả telemetry đã dừng; nếu nguồn thực sự im lặng quá 10 giây thì dữ liệu vẫn hết hạn đúng sau TTL.
- Bổ sung regression ba thành viên: từ góc nhìn của từng người đều phải thấy đủ hai peer, stats và marker như nhau; bài test 3 client production cũng xác nhận relay broadcast đối xứng.

# Isle Live Map 2.2.0

## Bản đồ offline và điều khiển layer

- Thay dữ liệu zone/food/heat cũ bằng snapshot offline từ MyIsleMap: 12 Migration Zone, 61 Patrol Zone, 7 Sanctuary, 52 AI Spawn Zone, 32 tuyến đường, 28 nguồn nước và 953 điểm tài nguyên.
- Trong Edit Mode, `LỚP BẢN ĐỒ` cho phép bật/tắt Zone, Roads, Water, Animals, Plants/Fungi và Earth; có thể lọc riêng từng loài hoặc tài nguyên.
- Layer offline dùng được cho cả Free và Pro, không gọi website khi app đang chạy và tự ghi nhớ lựa chọn sau khi khởi động lại.
- Static geometry chỉ được dựng lại khi filter, zoom, resize hoặc catalog thay đổi; GPS, Player và AI telemetry không còn khiến layer tĩnh rebuild liên tục.

## Alt+M và set point ổn định hơn

- Hotkey được đăng ký độc lập; một phím khác bị trùng không còn làm `Alt + M` ngừng hoạt động.
- Edit Mode có nút `MỐC` để mở bản đồ lớn khi Windows hoặc ứng dụng khác chiếm hotkey.
- Marker được cache theo ID thay vì bị xóa/tạo lại theo mỗi nhịp GPS, giúp click và popup không mất giữa chừng.
- Mốc của bạn có thể xóa bằng thùng rác, nút `XÓA MỐC`, phím `Delete` hoặc chuột phải; ping đồng đội vẫn chỉ chủ sở hữu được sửa/xóa.

## Nhóm sinh tồn tự phục hồi

- Relay bổ sung snapshot/revision tương thích ngược; client lấy snapshot khi connect, reconnect và định kỳ để phục hồi delta bị bỏ lỡ.
- So khớp cùng server ưu tiên endpoint `IP/DNS + port`, đồng thời tương thích `ServerKey` của relay cũ để tránh ẩn nhầm đồng đội khi tên server khác nhau.
- Chống telemetry, member removal và team ping đến sai thứ tự; dữ liệu cũ không còn kéo marker về vị trí trước hoặc làm mốc đã xóa xuất hiện lại.
- Marker giữ vị trí cuối tối đa 15 giây và giảm opacity khi reconnect; dòng thành viên giữ đến 35 giây rồi được relay dọn.
- Chuyển Home ↔ Overlay sẽ publish lại telemetry mới nhất mà không cần tạo hoặc vào lại nhóm.

## HUD gọn và thông báo cập nhật mới

- Bỏ `GATEWAY / LIVE`, tọa độ XYZ và dòng MMZ/PZ/FOOD khỏi góc trái minimap; giữ số Player/AI, legend phân loại và trạng thái đồng bộ.
- Modal cập nhật 5 trang dùng ảnh chụp đúng từng khung UI: layer offline, Alt+M, nhóm sinh tồn và minimap mới.
- Checkbox “Không hiển thị lại” chỉ xuất hiện ở trang cuối và dùng briefing key riêng cho đợt cập nhật này.

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
