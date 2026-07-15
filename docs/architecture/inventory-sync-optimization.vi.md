# Nghiên cứu: Lấy toàn bộ mã vận đơn tồn kho nhanh nhất & đồng bộ tối ưu

> Nguồn: khảo sát trực tiếp trên `jms.jtexpress.vn` (Chỉ số vận hành → Khâu phát → **Giám sát tồn kho (Big Data)** → tab **Chi tiết** → nút **Tìm kiếm**) + đối chiếu code `InventorySyncService.cs`, `DatabaseTracking.cs`, migration Supabase. Ngày khảo sát: 2026-07-15.

## 1. Phát hiện từ request thật

Khi bấm **Tìm kiếm**, trang gọi:

```
POST https://jmsgw.jtexpress.vn/businessindicator/bigdataReport/detail/take_ret_mon_detail_doris
```

- Trả về phân trang: `data.records[]`, `data.total`, `data.pages`. Lần khảo sát: **total = 24.410** đơn.
- Mã vận đơn nằm ở cột **"Mã vận đơn"** (trong record là trường `billcode`, ví dụ `861813631116`).
- Có sẵn bộ lọc **"Kiện đang tồn kho"** và **"Loại tồn kho"** → có thể thu hẹp tập dữ liệu ngay từ server.

### ⚠️ Cần xác minh: sai khác endpoint

Code hiện tại (`InventorySyncService.cs:143`) gọi:

```
businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2   ← có hậu tố "2"
```

Nhưng site thật gọi bản **không có "2"**. Cần kiểm tra bản nào là bản hiện hành — nếu JMS đã đổi tên, bản `_doris2` có thể sắp/đã bị bỏ. Đây là rủi ro "sync im lặng trả rỗng".

## 2. Cách lấy TẤT CẢ mã nhanh nhất

### Hiện trạng (chậm)

`FetchAllInventoryWaybillsWithRetryAsync` kéo **tuần tự** từng trang, `PageSize = 100`, `Task.Delay(250ms)` giữa mỗi trang.

- 24.410 đơn ÷ 100 = **~245 trang** × (≈300ms mạng + 250ms nghỉ) ≈ **2–3 phút** chỉ để lấy danh sách mã.

### Đề xuất (nhanh hơn ~5–10×)

1. **Trang 1 trước, phần còn lại song song.** Trang 1 đã trả về `pages`. Sau đó sinh số trang `2..N` và kéo bằng `Parallel.ForEachAsync` với `MaxDegreeOfParallelism = 4–5` thay vì tuần tự. Đây là đòn bẩy lớn nhất.
2. **Tăng `PageSize`.** Thử nghiệm `size = 500` rồi `1000` (report nền Doris thường cho phép). Ít vòng round-trip hơn hẳn — cần test thực tế vì server có thể chặn ở ngưỡng nào đó.
3. **Bỏ `Task.Delay(250ms)` cố định**, thay bằng giới hạn số luồng đồng thời + backoff khi gặp HTTP 429/401.
4. **Lọc tại server**: đặt "Kiện đang tồn kho = Có" để không kéo về đơn đã rời kho, giảm tổng số đơn phải xử lý.
5. Chỉ cần `billcode` → nếu API hỗ trợ tham số chọn cột, xin tối thiểu để giảm kích thước body (cần xác minh).

> Kết hợp (1)+(2): 24.410 đơn / size 1000 ≈ 25 trang, 5 luồng song song → có thể xong trong **~10–20 giây**.

## 3. Tăng tốc đồng bộ lên Database

- Đẩy mã mới bằng **một** RPC bulk `upsert_new_waybills(text[])` thay vì từng dòng (đã có sẵn).
- `merge_waybill_rows_v2` gộp theo lô `jsonb` — giữ nguyên lô lớn, tránh nhiều lời gọi nhỏ.
- Tracking (`DatabaseTracking.cs`) đã chạy song song (`BatchSize = 40`, `MaxDegreeOfParallelism = 3`). Có thể nâng nhẹ batch lên 50 và theo dõi tỷ lệ 401/429 trước khi tăng luồng.
- **Fingerprint cache** (`_cloudRowFingerprintCache`) đã bỏ qua đơn không đổi → chỉ upload đơn thay đổi. Giữ nguyên, đây chính là cơ chế "chỉ cập nhật dữ liệu mới".

## 4. Không xóa dữ liệu cũ — chỉ cập nhật (ĐÃ ĐÁP ỨNG)

Các RPC Supabase không bao giờ xóa:

- `merge_waybill_rows_v2`: `on conflict (waybill_no) do update ... where excluded.updated_at >= waybills.updated_at` → **newest-wins**, đơn cũ được giữ, chỉ ghi đè khi dữ liệu mới hơn.
- `upsert_new_waybills`: `on conflict do nothing` → không đụng đơn đã có.

**Lưu ý:** khi một đơn rời kho, đừng xóa — chỉ nên cập nhật cờ `is_in_current_inventory = false` (cột đã có trong schema). Việc phân biệt "đơn còn tồn / đã rời" nên xử lý bằng cờ, không phải bằng delete.

## 5. Dừng cập nhật khi trạng thái cuối = "Ký nhận CPN" hoặc "Kết thúc"

### Hiện trạng

`DatabaseTracking.cs:106–110` mới chỉ tắt `IsActive` khi `ThaoTacCuoi` chứa `"Ký nhận"` (chuỗi rộng), **chưa** xử lý `"Kết thúc"`, và chưa loại đơn terminal khỏi vòng tracking kế tiếp.

### Đề xuất

1. Thêm hàm chuẩn hoá trạng thái terminal:

```csharp
private static readonly string[] TerminalStatuses = { "Ký nhận CPN", "Kết thúc" };

private static bool IsTerminalStatus(WaybillDbModel row)
{
    string s = row.TrangThaiHienTai ?? row.ThaoTacCuoi ?? "";
    return TerminalStatuses.Any(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
}
```

2. Khi terminal: đặt `IsActive = false` (đã ghi lên DB, `is_active` newest-wins giữ nguyên).
3. **Quan trọng cho tốc độ:** trước khi dựng danh sách tracking, lọc bỏ đơn `is_active = false`. Đơn đã "Ký nhận CPN"/"Kết thúc" sẽ **không bị hỏi API JMS lại** ở các chu kỳ sau → vừa đúng yêu cầu, vừa giảm tải và tăng tốc.

> Chuỗi `"Ký nhận CPN"` là giá trị có thật trong dữ liệu — `Main.cs:2551` đã map `"Ký nhận CPN" → ForestGreen`.

## 6. Việc cần làm tiếp (nếu muốn triển khai)

| # | Việc | File |
|---|------|------|
| 1 | Xác minh endpoint `_doris` vs `_doris2` | `InventorySyncService.cs:143` |
| 2 | Đổi kéo trang tuần tự → trang 1 rồi song song, bỏ delay 250ms | `InventorySyncService.cs:139–268` |
| 3 | Test & nâng `PageSize` (500/1000) | `InventorySyncService.cs:19` |
| 4 | Thêm `IsTerminalStatus` (gồm "Ký nhận CPN" + "Kết thúc") | `DatabaseTracking.cs:106` |
| 5 | Lọc `is_active = false` khỏi danh sách tracking chu kỳ sau | `DatabaseTracking.cs`, `SupabaseDbService.GetActiveWaybillsAsync` |
| 6 | Cập nhật cờ `is_in_current_inventory` cho đơn đã rời kho (không delete) | RPC / merge |

Tất cả thay đổi ở mục 6 đều tuân thủ nguyên tắc "chỉ cập nhật, không xóa" và không đụng file protected.
