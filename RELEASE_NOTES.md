# Isle Live Map 1.2.1

- Sửa marker player bị lệch địa danh do dùng calibration của basemap server trên texture Gateway nhúng khác.
- Marker player và thành viên nhóm nay luôn ưu tiên world coordinate rồi chiếu theo đúng texture local.
- Điểm chuẩn hóa từ provider chỉ được dùng làm fallback khi không có world coordinate hợp lệ.
- Sửa tính năng chỉ đường để hiểu đúng chuỗi game copy theo thứ tự `Lat, Long, Alt`.
