# Nghiên cứu: Lấy toàn bộ mã vận đơn tồn kho nhanh nhất & đồng bộ tối ưu

> Nguồn: khảo sát trực tiếp trên `jms.jtexpress.vn` (Chỉ số vận hành → Khâu phát → **Giám sát tồn kho (Big Data)** → tab **Chi tiết** → nút **Tìm kiếm**) + đối chiếu code `InventorySyncService.cs`, `DatabaseTracking.cs`, migration DataHub. Ngày khảo sát: 2026-07-15.

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

- Đẩy theo lô bằng **một** request `POST /api/v1/sites/{siteId}/jms/observations`. Hai phương thức
  `DataHubClient.UpsertManyWaybillsAsync` / `MergeWaybillRowsV2Async` còn giữ tên từ thời RPC, nhưng cả
  hai đều gọi `SendObservationBatchAsync` → endpoint này. Giữ lô lớn, tránh nhiều lời gọi nhỏ.
- Đường đẩy cả chu kỳ fetch là `POST /jms/ingest` + header `Idempotency-Key` (bảng
  `idempotency_records` đỡ phía server), nên retry một lô là an toàn.
- ⚠️ `UpsertNewWaybillsOnlyAsync` (tên cũ `upsert_new_waybills`) **hiện chỉ đếm mã rồi trả về, không gửi
  gì**. Muốn đẩy "chỉ mã mới" thì phải gọi một trong hai đường trên.
- Tracking (`DatabaseTracking.cs`) đã chạy song song (`BatchSize = 40`, `MaxDegreeOfParallelism = 3`). Có thể nâng nhẹ batch lên 50 và theo dõi tỷ lệ 401/429 trước khi tăng luồng.
- **Fingerprint cache** (`_cloudRowFingerprintCache`) đã bỏ qua đơn không đổi → chỉ upload đơn thay đổi. Giữ nguyên, đây chính là cơ chế "chỉ cập nhật dữ liệu mới".

## 4. Không xóa dữ liệu cũ — chỉ cập nhật (ĐÃ ĐÁP ỨNG)

Đường ghi của DataHub không xoá:

- `waybill_scan_events` là **append-only**, chống trùng bằng `UNIQUE (site_id, event_fingerprint)` — event
  trùng bị bỏ qua, event cũ không bao giờ bị ghi đè.
- `waybill_projections` là **newest-wins theo slot**: reducer trong API chỉ ghi khi event mới hơn cái slot
  đang giữ. Dòng của đơn cũ vẫn còn nguyên.
- Xoá chỉ do retention (`retention_policies` + `BackgroundService` trong API) theo cửa sổ thời gian, và
  `005_change_retention_floor.sql` đặt sàn để không xoá mất cursor client đang dùng.

> ⚠️ **Không có cột `is_in_current_inventory`.** Schema hiện tại là 12 bảng ở
> `backend/datahub/migrations/001_core.sql` … `005_change_retention_floor.sql`; không có bảng `waybills`
> và không có cờ nào như vậy. Tín hiệu "còn tồn / đã rời" nằm ở nhóm slot `inventory_*` của
> `waybill_projections` (`inventory_code`, `inventory_name`, `inventory_event_at`, …), do
> `jms_event_policies` phân loại `event_kind = 'inventory'`. Muốn có một cờ boolean riêng thì phải mở
> migration `006_*.sql` — **không** phải sửa cột đã có. Nguyên tắc "chỉ cập nhật, không delete" vẫn đúng
> nguyên vẹn.

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

2. Khi terminal: đặt `IsActive = false` trên model phía client. ⚠️ **Không có cột `is_active` trên
   server** — `GetActiveWaybillsAsync` chỉ là alias của `ReadSnapshotAsync(pageSize)`, tức nó trả về
   nguyên snapshot chứ không lọc gì. Muốn server lọc thì cần cột mới (migration `006_*.sql`) + tham số
   lọc trên `/projections/snapshot`.
3. **Quan trọng cho tốc độ:** trước khi dựng danh sách tracking, lọc bỏ đơn terminal **ở client** (suy ra
   từ `state_name` / `last_activity_name` trong snapshot). Đơn đã "Ký nhận CPN"/"Kết thúc" sẽ **không bị
   hỏi API JMS lại** ở các chu kỳ sau → vừa đúng yêu cầu, vừa giảm tải và tăng tốc.

> Chuỗi `"Ký nhận CPN"` là giá trị có thật trong dữ liệu — `Main.cs:2551` đã map `"Ký nhận CPN" → ForestGreen`.

## 6. Việc cần làm tiếp (nếu muốn triển khai)

| # | Việc | File |
|---|------|------|
| 1 | Xác minh endpoint `_doris` vs `_doris2` | `InventorySyncService.cs:143` |
| 2 | Đổi kéo trang tuần tự → trang 1 rồi song song, bỏ delay 250ms | `InventorySyncService.cs:139–268` |
| 3 | Test & nâng `PageSize` (500/1000) | `InventorySyncService.cs:19` |
| 4 | Thêm `IsTerminalStatus` (gồm "Ký nhận CPN" + "Kết thúc") | `DatabaseTracking.cs:106` |
| 5 | Lọc đơn terminal khỏi danh sách tracking chu kỳ sau (ở client — `GetActiveWaybillsAsync` không lọc) | `DatabaseTracking.cs`, `DataHubClient.GetActiveWaybillsAsync` |
| 6 | Biểu diễn "đơn đã rời kho" bằng slot `inventory_*` (không delete); nếu cần cờ boolean thì mở migration `006_*.sql` + tham số lọc snapshot | reducer trong `AutoJMS.DataHub.Api` |

Tất cả thay đổi ở mục 6 đều tuân thủ nguyên tắc "chỉ cập nhật, không xóa" và không đụng file protected.
