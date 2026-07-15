# Tối ưu tốc độ đồng bộ (lấy danh sách + cập nhật dữ liệu)

Vấn đề: đồng bộ chậm khi số waybill lớn — lấy danh sách phân trang **tuần tự** (100/trang, +250ms/trang),
sau đó tracking gọi **getOrderDetail tuần tự từng đơn** và **lặp lại mỗi chu kỳ** dù thông tin đơn hàng gần như tĩnh.

## Thay đổi đã áp dụng (build pass, không đụng file protected)

### 1. Lấy danh sách song song — `FullStackInventorySyncService.FetchInventoryAsync`
- Trước: vòng `while` tuần tự trang 1 → 2 → ... + `Task.Delay(250ms)` mỗi trang. Với ~4.225 đơn = 43 trang ⇒ ~20–45s chỉ để liệt kê; một trang lỗi làm **hỏng cả sync**.
- Sau:
  - Lấy **trang 1** trước để biết tổng số trang (`pages`/`total`).
  - Lấy **trang 2..N song song** (`Parallel.ForEachAsync`, `PageConcurrency=5`), bỏ delay 250ms.
  - Vẫn giữ **producer/consumer** (`onPageCodes`) — mỗi trang xong đẩy mã sang tracking ngay; dedup an toàn dưới lock.
  - Một trang lỗi ⇒ **log + bỏ qua**, không hỏng cả sync (fallback tuần tự nếu API không trả tổng số trang).
- Kết quả: thời gian liệt kê giảm ~5x (43 trang / 5 luồng), bền hơn với lỗi tạm thời.

### 2. Cache "Thông tin đơn hàng" tĩnh — `DatabaseTracking`
- Trước: `ProcessOrderDetailBatchAsync` gọi `getOrderDetail` **tuần tự từng đơn**, **mỗi chu kỳ** ⇒ với N đơn là N request lặp lại liên tục (nút thắt lớn nhất).
- Sau:
  - `getOrderDetail` (người gửi, địa chỉ, COD, nội dung hàng, trọng lượng...) là **tĩnh theo đơn** → lấy **một lần**, ghi vào cache `_orderDetailDone`, **bỏ qua** ở các chu kỳ sau.
  - Các đơn còn thiếu detail được gọi **song song có giới hạn** (`_orderDetailGate`, `OrderDetailConcurrency=3`) thay vì tuần tự.
  - Hành trình (`keywordList`) — **động** — vẫn cập nhật mỗi chu kỳ như cũ.
- Kết quả: sau chu kỳ đầu, số request order-detail ≈ 0 ⇒ các lần đồng bộ tiếp theo nhanh hơn nhiều; chu kỳ đầu cũng nhanh hơn nhờ song song.

### 3. Ghi DB (đã tối ưu sẵn)
- `UpsertManyWaybillsAsync` đã gộp toàn bộ dòng vào **một RPC** `merge_waybill_tracking_rows` (batch JSONB), và có **fingerprint cache** bỏ qua dòng không đổi ⇒ không cần thay đổi.

## Giới hạn đồng thời (chống khóa IP)
- Lấy trang: tối đa 5 luồng. Order-detail: tối đa 3 luồng (semaphore toàn cục). Tracking batch: giữ nguyên 3.
- Các mức này cân bằng giữa tốc độ và rủi ro bị JMS khóa IP; có thể tinh chỉnh qua hằng số nếu cần.

## Gợi ý nâng cấp tiếp (khi cần)
- Chỉ tracking đơn **đến hạn** (`next_track_at`) theo lịch ưu tiên thay vì toàn bộ mỗi lần (đã có index `idx_waybills_site_due_active`).
- Chunk payload upsert (VD 500–1000 dòng/lần) nếu inventory rất lớn để tránh payload quá to.
- Dùng nút **Export server-side** của Big Data cho full-refresh (1 request thay vì phân trang) — xem `inventory-source-comparison.vi.md`.
