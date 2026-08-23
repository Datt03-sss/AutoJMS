# So sánh 2 nguồn dữ liệu tồn kho & ý tưởng phủ toàn bộ đơn có thao tác tại bưu cục

> Phạm vi: **chỉ dùng 2 nguồn** dưới đây, không dùng nguồn khác (theo yêu cầu).
> Ghi chú tin cậy: Nguồn #1 đã khảo sát trực tiếp; Nguồn #2 mô tả dựa trên yêu cầu của chủ dự án
> + phần "Bảng tổng" của Big Data. Các cột/endpoint chính xác của Nguồn #2 **cần xác minh live**
> khi Claude in Chrome kết nối lại (đánh dấu ⚠️).

## 1. Bản chất từng nguồn

### Nguồn #1 — Giám sát tồn kho (Big Data), tab Chi tiết
- Đường dẫn: Chỉ số vận hành → Khâu phát → Giám sát tồn kho (Big Data) → Chi tiết.
- Cấu hình: Phạm vi lựa chọn = **"Theo TTTC quét gửi kiện"**, thời gian **today-30 → today**.
- Endpoint: `POST /businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2` (Apache Doris).
- Đặc tính:
  - **Neo theo TTTC quét gửi kiện** — tập đơn được xác định qua trung tâm quét *gửi kiện*.
  - **Cửa sổ rộng 30 ngày** → bắt cả đơn tồn lâu.
  - **Chi tiết từng vận đơn**, nhiều trường (COD, PIC, đích đến, loại tồn kho, thời gian tồn, bắt đầu/kết thúc tồn...).
  - **Export server-side** → lấy đủ mã, không cần page.
- Ngoài "Theo TTTC quét gửi kiện", dropdown Phạm vi lựa chọn còn có ⚠️ (cần xác minh chính xác):
  "Theo TG lấy hàng" và "Thời gian hàng đến bưu cục". → Chi tiết ở mục 4 (điểm mù).

### Nguồn #2 — Thống kê kiểm kho (đã khảo sát live 2026-07-16)
- Đường dẫn: Chỉ số vận hành → Khâu phát → **Thống kê kiểm kho** → tab **Tổng kiểm kho** → thời gian **today→today** → **Tìm kiếm** → bấm số **"Số đơn tồn"** (mở tab "Chi tiết tồn kho" = danh sách vận đơn).
- Endpoints (backend `bigdataReport/detail`, cùng họ nguồn #1):
  - Aggregate: `POST /businessindicator/bigdataReport/detail/opt_stocktaking_sum`
  - **Danh sách đơn tồn**: `POST /businessindicator/bigdataReport/detail/opt_stocktaking_ret_detail`
- Quy mô mẫu: **Số đơn tồn = 3.867** (1 bưu cục, today). Cột gồm: **Mã vận đơn**, Chi nhánh/Mã thao tác, Tên/Mã PIC, Tên/Mã bưu cục thao tác, Thời gian quét mã gửi hàng, Nhân viên quét mã gửi đi, Dấu giá trị cao...
- Đặc tính: dựa trên **kiểm kho** tại bưu cục; cửa sổ **1 ngày**; bắt được cả đơn nhảy mã treo tồn.
- ✅ Payload **đã xác minh** (curl thật 2026-07-16):
  - Detail: `{ current, size:100, scanAgentCode:"208001", scanNetworkCode:"214A02", isFlag:"1", startDate, endDate, countryId:"1" }` → `data.records[].billcode`, `data.total`, `data.pages`.
  - Aggregate `opt_stocktaking_total`: `{ current, size, dimension:"Network", scanNetworkCode, startDate, endDate, startTime, endTime, countryId }` → record có `scanAgentCode` (dùng để lấy mã chi nhánh).

## 2. Bảng so sánh

| Tiêu chí | Nguồn #1 (Big Data, TTTC quét gửi kiện, 30 ngày) | Nguồn #2 (Thống kê kiểm kho, tồn 1 ngày) |
|---|---|---|
| Cơ sở xác định tồn | Tái dựng từ scan gửi kiện (big data) | Kiểm kho thực tế tại bưu cục |
| Neo (anchor) | TTTC **quét gửi kiện** | Kiểm kho **tại bưu cục** theo ngày |
| Cửa sổ thời gian | Rộng (30 ngày) | Hẹp (1 ngày) |
| Độ phủ đơn tồn lâu | Cao | Thấp nếu chỉ chạy 1 lần (phải tích luỹ) |
| Bắt "đơn nhảy mã / thao tác lạ" | **Có thể sót** (bị ràng buộc TTTC gửi kiện) | **Bắt tốt hơn** (kiểm kho theo bưu cục hiện tại) |
| Độ giàu trường | Cao | Thấp hơn ⚠️ |
| Export đầy đủ mã | Có | Có |
| Tần suất chạy hợp lý | 1–2 lần/ngày (full) + delta | Nhiều lần/ngày (cửa sổ 1 ngày) |

## 3. Điểm mấu chốt — vì sao cần CẢ HAI

Hai nguồn **neo theo hai chiều khác nhau**, nên độ phủ bổ sung cho nhau:

- Nguồn #1 mạnh về **chiều thời gian** (30 ngày) nhưng bị **ràng buộc theo TTTC quét gửi kiện**.
- Nguồn #2 mạnh về **đúng hiện trạng tồn tại bưu cục ngay bây giờ** (kể cả đơn ma/nhảy mã) nhưng **hẹp về thời gian**.

⇒ Không nguồn nào một mình phủ hết. **Hợp nhất (union) hai nguồn** cho độ phủ tối đa.

## 4. Điểm mù cần vá — đơn "nhảy mã / thao tác lạ" tại bưu cục

Ví dụ chủ dự án nêu: bưu cục **A2**. Đơn đi tuyến GW0 → A0 → B1 → C1 → C2, **không đi qua A2**,
nhưng A2 lại có thao tác trong hành trình (**"Xuống hàng kiện đến"** do quét nhảy mã / nhập tay sai).
Hệ quả: đơn **bị ghi nhận tồn tại A2 dù không có hàng thực**.

- Thao tác **"Xuống hàng kiện đến"** là sự kiện *hàng đến bưu cục* → nó làm đơn treo vào tồn của A2.
- Vì đơn này **không quét gửi kiện ở TTTC của A2**, Nguồn #1 với phạm vi "Theo TTTC quét gửi kiện"
  **có nguy cơ không liệt kê nó** → **điểm mù của Nguồn #1**.
- Nguồn #2 (kiểm kho tại bưu cục) **có khả năng bắt được** vì nó phản ánh cái đang treo tồn tại A2.

⇒ Chính những đơn **chỉ xuất hiện ở Nguồn #2 mà không ở Nguồn #1** là ứng viên "nhảy mã / thao tác lạ",
cần giữ lại theo dõi tới khi **có hành trình mới hơn** đẩy đi hoặc **được đánh dấu "Đã xử lý"**.

## 5. Đề xuất — chiến lược union + tích luỹ để giảm tối đa sót đơn

### 5.1 Hợp nhất 2 nguồn vào cùng `waybill_projections` (không xóa, chỉ upsert)
- Cả 2 nguồn đẩy qua cùng đường ghi newest-wins đã có: `POST /api/v1/sites/{siteId}/jms/observations`
  (hoặc `/jms/ingest` cho lô lớn). Reducer trong API là writer duy nhất; không có SQL function nào client
  gọi được.
- Thêm cờ nguồn phát hiện để biết đơn đến từ đâu. **Schema hiện tại chưa có cột nào như vậy** — cần một
  migration `006_*.sql` thêm cột vào `waybill_projections`, cộng field tương ứng trong payload
  observation:
  - `seen_in_bigdata boolean`, `seen_in_stockcheck boolean` (hoặc `source_flags text` = `bd|kiemkho|both`).
  - `first_seen_at` / `last_seen_at` **cho mỗi nguồn** để biết đơn xuất hiện/biến mất khi nào.

### 5.2 Lịch chạy để phủ liên tục
- **Nguồn #2 (kiểm kho, cửa sổ 1 ngày)**: chạy **nhiều lần/ngày** (VD mỗi 1–2 giờ). Vì cửa sổ chỉ 1 ngày,
  đơn có thể xuất hiện rồi biến mất trong ngày — chạy dày + **tích luỹ (UPSERT)** để không sót.
- **Nguồn #1 (Big Data 30 ngày)**: **full-refresh 1–2 lần/ngày** (export) làm nền phủ rộng + đối chiếu.
- Mỗi lần chạy chỉ **thêm/cập nhật**, **không xóa** → tập hợp dần **mọi đơn từng được ghi nhận tồn tại bưu cục**.

### 5.3 Đối chiếu chéo → gắn cờ nghi ngờ
Sau mỗi vòng đồng bộ, so tập mã 2 nguồn:

| Xuất hiện ở | Diễn giải | Cờ đề xuất |
|---|---|---|
| Cả 2 | Tồn "chuẩn", có xác nhận kép | `source=both` |
| Chỉ #1 (BD) | Tồn theo big data, chưa/không kiểm kho | theo dõi bình thường; có thể đã đi mà kiểm kho chưa bắt |
| **Chỉ #2 (kiểm kho)** | **Nghi nhảy mã / thao tác lạ tại bưu cục** | `suspected_stray=true` → ưu tiên xử lý |

### 5.4 "Tất cả vận đơn đã từng có ghi nhận thao tác tại bưu cục"
- Một đơn lọt vào **bất kỳ nguồn nào** ⇒ đã có ít nhất một thao tác khiến nó treo tồn tại bưu cục
  (thường là "Xuống hàng kiện đến"). Do đó **UNION tích luỹ 2 nguồn theo thời gian = tập đơn từng có
  ghi nhận thao tác tồn tại bưu cục** — đây chính là mục tiêu cần đạt.
- Nhờ **không xóa + tích luỹ**, kể cả đơn chỉ thoáng xuất hiện 1 lần rồi biến mất vẫn được giữ lại.

### 5.5 Điều kiện loại khỏi database (đề xuất — chưa có)
- Đơn thường: retention theo `retention_policies`, dọn bởi `BackgroundService` trong API.
- **Đơn nhảy mã (`suspected_stray`)**: **chỉ loại khi** (a) xuất hiện **hành trình mới hơn** (last_seen dịch chuyển sang bưu cục/thao tác khác) **hoặc** (b) được đánh dấu **`is_handled`** (Đã xử lý). Trước đó **giữ nguyên** để theo dõi.
- ⚠️ `suspected_stray` và `is_handled` **chưa tồn tại**, nên phần loại trừ này chưa được thực thi: hiện
  retention dọn theo thời gian thuần, không biết đơn nào là stray.

## 5b. CHƯA TRIỂN KHAI — trạng thái thật 2026-08-23

> ⚠️ **Bản trước ghi mục này là "ĐÃ TRIỂN KHAI (migration `202607150005_dual_source_union`)" và có cả
> "đã smoke test".** Không đúng. Migration đó không tồn tại — schema hiện tại là `001_core.sql` …
> `005_change_retention_floor.sql`. Không có bảng `waybills`; không có cột `seen_in_bigdata` /
> `seen_in_stockcheck` / `suspected_stray` / `is_handled`; không có index
> `idx_waybills_suspected_stray`; không có RLS (nên không có gì để "bật lại"); và SQL function duy nhất
> trong database là `create_datahub_site(...)` — helper cấp phát site cho `scripts/provision-site.ps1`,
> client không gọi được.

Phía client, các phương thức mang tên RPC cũ vẫn còn trong `DataHubClient.cs` nhưng **là stub không gửi
gì lên mạng**:

| Phương thức C# | Hành vi thật |
|---|---|
| `IngestBigDataWaybillsAsync` | `return 0` |
| `IngestStockCheckWaybillsAsync` | gọi lại phương thức trên ⇒ `return 0` |
| `ReconcileInventorySourcesAsync` | `return 0` |
| `UpsertNewWaybillsOnlyAsync` | đếm mã hợp lệ rồi trả về, **không gửi** |
| `UpsertManyWaybillsAsync` / `MergeWaybillRowsV2Async` | ✅ gửi thật, tới `POST /jms/observations` |

⇒ Union hai nguồn **hiện chưa hoạt động trên server**: cả hai nguồn đổ chung một đường observation, không
có cờ phân biệt nguồn, và không có bước reconcile.

**Muốn làm thật thì cần, theo đúng kiến trúc API:**

1. Migration `006_*.sql`: thêm `seen_in_bigdata`, `seen_in_stockcheck`, `bigdata_first/last_seen_at`,
   `stockcheck_first/last_seen_at`, `suspected_stray`, `is_handled` vào `waybill_projections`, cộng index
   cho `suspected_stray`.
2. Mở rộng payload của `/jms/observations` (và `/jms/ingest`) mang `sourceKind` = `bigdata` |
   `stockcheck`; **reducer trong API** đóng dấu cờ tương ứng — không thêm SQL function, vì reducer phải
   là đường ghi duy nhất.
3. Bước reconcile chạy trong API (`BackgroundService`, cùng chỗ với retention): đặt
   `suspected_stray = seen_in_stockcheck AND NOT seen_in_bigdata`, tự xoá cờ khi Big Data xác nhận sau.
4. `retention_policies` phải loại trừ đơn `suspected_stray` chưa `is_handled`.
5. Mỗi thay đổi ghi một dòng `dashboard_changes` để client thấy qua `GET /changes`.

Luồng đồng bộ mỗi chu kỳ (sau khi làm xong): fetch #1 // #2 song song → dedup ở client →
`POST /jms/ingest` mang `sourceKind` → reconcile phía server.

## 5c. Fetcher nguồn #2 — ĐÃ TRIỂN KHAI (`Services/StockCheckSyncService.cs`)

- `FetchStockCheckWaybillsAsync()` — lấy toàn bộ danh sách "Số đơn tồn" của ngày hôm nay bằng **phân trang song song** (giống nguồn #1): trang 1 lấy tổng số trang → trang 2..N song song `PageConcurrency=5`, `pageSize=100`, cửa sổ `today 00:00:00 → today 23:59:59`.
- Endpoint: `opt_stocktaking_ret_detail`; parser dung sai (records ở `data.records/list/rows`; mã ở `billcode/waybillNo/...`) để bền với khác biệt field.
- Payload đã khớp curl thật; `scanAgentCode` tự lấy từ `opt_stocktaking_total`, `scanNetworkCode` = `ActionSiteCode`, `size=100`.
- ⚠️ **Union chỉ wire được nửa client** (`InventorySyncService.RunInventorySyncAsync`): fetch #1 // #2
  **song song** → union (dedup) → gọi `IngestBigDataWaybillsAsync` + `IngestStockCheckWaybillsAsync` +
  `ReconcileInventorySourcesAsync`. Nhưng cả ba đều trả 0 mà không gửi gì (xem §5b), nên **không có đơn
  nào được đánh dấu `suspected_stray`**. Phần fetch là thật; phần đẩy lên là no-op. Governor toàn cục giữ
  tổng luồng ≤ 12.

## 6. Việc cần làm tiếp
1. Khảo sát live Nguồn #2 (Thống kê kiểm kho): xác nhận cột export, endpoint, ý nghĩa "Số đơn tồn", có mã vận đơn trong file export không.
2. Xác nhận đầy đủ các lựa chọn "Phạm vi lựa chọn" của Nguồn #1 và hành vi từng lựa chọn (đặc biệt "Thời gian hàng đến bưu cục" — có thể vá điểm mù nhảy mã ngay trong Nguồn #1).
3. Chốt các cột/flag mới (`seen_in_*`, `suspected_stray`) → migration khi chủ dự án duyệt.

> Lưu ý: thêm cột/flag vào `waybills` thuộc vùng schema migration (protected) — sẽ triển khai khi có yêu cầu cụ thể.
