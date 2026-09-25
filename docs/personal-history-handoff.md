# Mốc vị trí cuối / đã chết — 25/09/2026

Free và Pro đều dùng ALT+M để xem/xóa mốc cá nhân; không cần nhóm.
Đặt mốc thủ công/ping nhóm vẫn giữ quyền Pro cũ.

- Mất mẫu GPS hợp lệ liên tục 10 giây: tạo một mốc Vị trí cuối với thời gian
  mẫu thật; không refresh timestamp bằng timer và không lặp mốc trong cùng đợt mất.
- Có GPS trở lại: cho phép ghi một sự kiện mất mới. Đổi server hoặc đóng overlay
  ghi vị trí cuối của phiên cũ. Chỉ hiển thị mốc đúng server sau khi nhận GPS.
- Click mốc, chọn TÔI ĐÃ CHẾT TẠI ĐÂY để xác nhận thủ công. Không dùng mất stats
  hoặc HP=0 để tự kết luận chết. Xóa bằng thùng rác/Delete/chuột phải.
- Tối đa 20 mốc lịch sử, hết hạn 24h, local map-notes.json có lưu server/time.
- Checkbox Đồng bộ mốc cá nhân mặc định tắt mỗi lần mở overlay. Khi bật, chỉ
  các mốc lịch sử được gửi tới API riêng; không gửi cookie/Steam, không chia sẻ
  với đội hoặc người lạ. Token relay dùng file personal-markers.credential DPAPI.
- Bỏ chọn yêu cầu xóa bản sao relay; offline hiển thị chưa đồng bộ và retry.
  Local deletion là nguồn sự thật; GET khi nối lại tìm mốc cần xóa trên relay.
  Relay restart mất RAM; client có thể khôi phục các mốc local chưa xóa/chưa hết hạn.

API production: https://isle-relay.klong.dev/api/v1/personal-markers
Repo/server details: IsleLiveMap-Relay/docs/personal-markers.md.

Các file chính: MainWindow.PersonalHistory.cs, MapNotes.PersonalHistory.cs,
PersonalMarkerSync.cs, MapNotesWindow.xaml(.cs), PersonalMarkerCredentialStore.cs.
Không thay đổi backend key/Pro hay giao thức nhóm. Không publish desktop release.

Kiểm thử: tracker mất tín hiệu/đổi server/HP=0; persistence/TTL; Free UI xóa và
xác nhận không unlock Pro; HTTP fake kiểm tra không cookie, delete sau restart,
410 chống replay. API tests tách owner/capacity/TTL/retry và HTTPS smoke thực tế.
Không có phiên chết ingame thực để xác nhận end-to-end với game đang chơi.
