# FullStackOperation — Kế hoạch đồng bộ tồn kho (JMS Big Data)

Nguồn dữ liệu: **JMS → Chỉ số vận hành → Khâu phát → Giám sát tồn kho (Big Data) → tab Chi tiết**.
Khảo sát thực hiện ngày 2026-07-15, tài khoản `(LCI)Kim Tân` (mã BC `214A02`, chi nhánh Thái Nguyên `208001`).

## 1. Kết quả khảo sát request

Bộ lọc dùng để lấy danh sách công việc tồn kho:

| Trường | Giá trị |
|---|---|
| Phạm vi lựa chọn | **Theo TTTC quét gửi kiện** |
| Phạm vi thời gian | `today-30` → `today` (2026-06-15 → 2026-07-15) |
| Còn lại | mặc định (Tất cả) |

Kết quả: **~4.225 vận đơn** trong cửa sổ 1 tháng (số thay đổi realtime), mặc định 20 dòng/trang = 214 trang.

### API backend

```
POST https://jmsgw.jtexpress.vn/businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2
```

- Backend là **Apache Doris (OLAP)** — tối ưu quét khối lớn, phân trang theo `pageNum`/`pageSize`.
- `pageSize` tối đa qua UI: **100** (tuỳ chọn 10/20/30/40/50/100). Response trả kèm `total` ⇒ tính được tổng số trang.
- Bộ lọc **Mã vận đơn** hỗ trợ tra **tối đa 500 mã/lần** (phân tách bằng dấu) — dùng cho việc re-check có chủ đích.
- Có nút **"Xuất dữ liệu"** và **"Tải xuống"** → export server-side toàn bộ kết quả trong 1 job.

### Cột dữ liệu tab Chi tiết (đã ánh xạ sang `public.waybills`)

`STT, Thời gian TTTC phát hàng, Mã vận đơn, Mã khách hàng, Đích đến, Tiền thu hộ COD, Bưu cục thao tác, Mã bưu cục thao tác, Tên PIC, Mã PIC, BC thao tác thuộc chi nhánh, Mã BC thao tác thuộc chi nhánh, Thời gian quy hoạch/thực tế hàng đến, Thời gian quét mã gửi hàng, Trạm quét, Thời gian tồn kho, Loại tồn kho, Kiện đang tồn kho, Thời gian bắt đầu/kết thúc tồn kho`.

> Lưu ý quan trọng: view này là **danh sách tồn kho** — KHÔNG chứa trạng thái cuối "Ký nhận CPN"/"Kết thúc". Khi vận đơn đạt trạng thái cuối, nó **rời khỏi danh sách tồn kho** (không còn trả về ở lần query sau). Trạng thái cuối được xác nhận qua API **Tra hành trình** (route tracking).

## 2. Phương pháp lấy TOÀN BỘ mã vận đơn nhanh nhất

Xếp theo tốc độ:

1. **Export server-side ("Xuất dữ liệu"/"Tải xuống")** — 1 request duy nhất trả toàn bộ ~4.225 dòng dưới dạng file. Parse cột "Mã vận đơn". Nhanh & nhẹ nhất cho lần lấy đầy đủ (full snapshot).
2. **Phân trang song song ở `pageSize=100`** — `ceil(total/100) ≈ 43` request thay vì 214. Thuật toán:
   - Gọi trang 1 → lấy `total` → tính số trang `N`.
   - Fan-out các trang `2..N` với **concurrency 5–8** (không nối tiếp). Tổng ~5–10s thay vì hàng phút.
   - Gộp cột `waybill_no` → tập mã đầy đủ.
3. **Tra theo lô 500 mã** (bộ lọc Mã vận đơn) — chỉ dùng khi cần cập nhật một tập mã cụ thể đã biết, không dùng cho quét toàn bộ.

Khuyến nghị: **(1) cho full-refresh định kỳ**, **(2) cho đồng bộ delta thường xuyên** (chạy mỗi vài phút, chỉ trang đầu vài trang là đủ vì sắp xếp theo thời gian).

## 3. Thuật toán đồng bộ (upsert — không xoá — dừng theo trạng thái cuối)

Nguyên tắc: **không bao giờ DELETE**. Chỉ INSERT mã mới + UPDATE mã đã có (newest-wins). Vận đơn dừng cập nhật khi trạng thái cuối là **"Ký nhận CPN"** hoặc **"Kết thúc"**.

```
1. Lấy snapshot tồn kho hiện tại  → tập mã S (mục 2).
2. Upsert theo lô: merge_waybill_rows_v2(site, jsonb[ ...S... ])
     • mã mới      → INSERT (is_active=true, is_in_current_inventory=true)
     • mã đã có    → UPDATE các trường (chỉ ghi đè nếu updated_at mới hơn)
3. mark_left_inventory(site, seen := S, left_at)
     • mã đang active trong DB nhưng KHÔNG có trong S
       → is_in_current_inventory=false, ghi left_inventory_at
       → VẪN giữ is_active=true (tiếp tục theo dõi tới khi biết trạng thái cuối)
4. Với các mã đã rời kho: xác nhận trạng thái cuối qua API Tra hành trình.
     • nếu "Ký nhận CPN" / "Kết thúc"
       → finalize_waybills(site, [mã...], status)  → is_active=false, dừng lịch tracking
     • ngược lại → giữ active, đặt lại next_track_at
```

### RPC hỗ trợ (đã tạo — migration `202607150003_inventory_sync_helpers.sql`)

| RPC / đối tượng | Vai trò |
|---|---|
| `merge_waybill_rows_v2(site, jsonb)` | Upsert newest-wins theo lô (đã có từ hybrid_sync). Truyền cả nghìn dòng/lần gọi. |
| `finalize_waybills(site, text[], status)` | Đánh dấu mã đạt trạng thái cuối → `is_active=false`, `is_in_current_inventory=false`, ghi status. Không xoá. |
| `mark_left_inventory(site, seen text[], left_at)` | Mã active không còn trong snapshot → đánh dấu rời kho, **giữ** tracking. |
| `is_final_status(text)` | Kiểm tra 1 status có thuộc tập trạng thái cuối không. |
| Trigger `waybills_stop_on_final` | Bất kỳ merge nào set status = "Ký nhận CPN"/"Kết thúc" → tự động `is_active=false` (defense-in-depth). |
| `app_configs['tracking_final_statuses']` | Danh sách trạng thái cuối cấu hình được (mặc định `["Ký nhận CPN","Kết thúc"]`). |
| Index `idx_waybills_site_due_active` | Tra nhanh vận đơn còn active đến hạn cập nhật. |

### Vì sao nhanh

- **Ghi theo lô JSONB** qua 1 RPC (chunk 500–1000 dòng) thay vì insert từng dòng → giảm round-trip.
- **newest-wins** bằng `where excluded.updated_at >= waybills.updated_at` → idempotent, chạy lại nhiều lần an toàn, không cần transaction phức tạp.
- **Trigger tự dừng** đảm bảo vận đơn "Ký nhận CPN"/"Kết thúc" không bao giờ bị lên lịch tracking lại, kể cả khi client quên gọi `finalize_waybills`.
- Index partial `where is_active` giữ scheduler chỉ quét tập đang mở.

## 4. Tăng tốc tracking toàn bộ mã đã lấy

- **Ghi theo lô**: đẩy cả tập waybill vào 1 lần gọi `merge_waybill_rows_v2(site, jsonb[...])` (chunk 500–1000 dòng), không insert từng dòng.
- **Scheduler dựa index partial** `idx_waybills_site_due_active (site_code, next_track_at) where is_active`: mỗi vòng tracking chỉ quét đúng tập đơn **còn active + tới hạn**, bỏ qua toàn bộ đơn đã terminal → giảm mạnh khối lượng quét khi DB lớn.
- **Realtime**: bảng `waybills` đã nằm trong publication `supabase_realtime` → client nhận thay đổi tức thời, không cần poll toàn bảng.
- **Delta pull** `pull_waybill_delta(site, since, limit)` chỉ trả dòng có `updated_at > since` → đồng bộ xuống client cực nhẹ.
- **Dừng tracking sớm**: trigger `waybills_stop_on_final` + `finalize_waybills` loại đơn "Ký nhận CPN"/"Kết thúc" khỏi vòng lặp tracking ngay khi đạt trạng thái cuối → không tốn công theo dõi đơn đã xong.

## 5. Chính sách lưu trữ 30 ngày & dọn dữ liệu (migration `202607150004`)

Quy tắc (chủ dự án chốt 2026-07-15):

| Trạng thái | Xử lý |
|---|---|
| **Ký nhận CPN** | **Xóa ngay** khỏi DB (bất kể tuổi). App gọi `purge_signed_receipts(site)` sau mỗi lần sync; cron dọn nốt hằng ngày. |
| **Kết thúc** (chưa xử lý) | **Giữ vô thời hạn** để xử lý khiếu nại "Trọng tài". Không bị retention 30 ngày đụng tới. |
| **Kết thúc** (đã xử lý) | Đánh dấu `mark_waybill_handled(site, [mã], true)` → cron xóa ở lần chạy kế. |
| Còn lại (đang theo dõi...) | **Retention 30 ngày** theo `updated_at`, quá hạn thì xóa. |

Cơ chế tự động: **pg_cron** job `autojms-retention-daily` chạy `0 18 * * *` (UTC = 01:00 giờ VN) gọi `run_retention_cleanup(30)`. Hàm này KHÔNG cấp quyền cho anon/authenticated (chỉ cron chạy). Trả về JSON đếm số dòng đã purge mỗi loại.

## 6. Kiểm thử

Đã smoke test trên `autojms_database`:

- Sync: finalize dừng đúng, trigger tự dừng khi merge "Kết thúc", mark_left_inventory giữ active cho mã rời kho, **không dòng nào bị xoá ngoài ý muốn**.
- Retention: "Ký nhận CPN" → xóa ngay; "Kết thúc" chưa xử lý (kể cả 90 ngày tuổi) → giữ; "Kết thúc" đã xử lý → xóa; đơn thường >30 ngày → xóa; đơn mới → giữ. `purge_signed_receipts` xóa tức thì đúng. Cron job `autojms-retention-daily` active.
- Dữ liệu test đã dọn sạch.
