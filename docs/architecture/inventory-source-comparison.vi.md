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
- ⚠️ Payload của `opt_stocktaking_ret_detail` **không lấy được body** (iframe cross-origin) — client `StockCheckSyncService` dùng payload suy từ nguồn #1 cùng họ; cần xác minh log lần chạy đầu (xem code + mục 5b).

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

### 5.1 Hợp nhất 2 nguồn vào cùng bảng `waybills` (không xóa, chỉ upsert)
- Cả 2 nguồn đẩy qua cùng RPC upsert newest-wins đã có (`merge_waybill_rows_v2`).
- Thêm cờ nguồn phát hiện để biết đơn đến từ đâu:
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

### 5.5 Điều kiện loại khỏi database (tận dụng cơ chế đã có)
- Đơn thường: theo policy đã triển khai — "Ký nhận CPN" xóa ngay, "Kết thúc" giữ tới khi `is_handled`, còn lại retention 30 ngày.
- **Đơn nhảy mã (`suspected_stray`)**: **chỉ loại khi** (a) xuất hiện **hành trình mới hơn** (last_seen dịch chuyển sang bưu cục/thao tác khác) **hoặc** (b) được đánh dấu **`is_handled`** (Đã xử lý). Trước đó **giữ nguyên** để theo dõi.

## 5b. ĐÃ TRIỂN KHAI (migration `202607150005_dual_source_union`)

Cột mới trên `waybills`: `seen_in_bigdata`, `seen_in_stockcheck`, `bigdata_first/last_seen_at`,
`stockcheck_first/last_seen_at`, `suspected_stray`; index `idx_waybills_suspected_stray`.

RPC:
- `merge_bigdata_detail(site, jsonb)` — đẩy chi tiết Nguồn #1 + tự đóng dấu `seen_in_bigdata`.
- `ingest_bigdata_waybills(site, text[])` — đóng dấu Nguồn #1 (dùng khi chỉ có danh sách mã).
- `ingest_stockcheck_waybills(site, text[])` — nạp Nguồn #2 (insert-if-missing + đóng dấu).
- `reconcile_inventory_sources(site)` — đặt `suspected_stray = seen_in_stockcheck AND NOT seen_in_bigdata`; tự xoá cờ khi Big Data xác nhận sau.
- `run_retention_cleanup` cập nhật: **không xoá** đơn `suspected_stray` chưa `is_handled` (giữ tới khi có hành trình mới đẩy vào Big Data hoặc đánh dấu Đã xử lý).

Luồng đồng bộ mỗi chu kỳ: `merge_bigdata_detail(...)` (Nguồn #1) → `ingest_stockcheck_waybills(...)` (Nguồn #2) → `reconcile_inventory_sources(site)`.

Đã smoke test: đơn chỉ có ở kiểm kho → `suspected_stray=true`; retention giữ đơn stray chưa xử lý dù 45 ngày; Big Data xác nhận sau → cờ tự xoá; đơn thường >30 ngày vẫn bị xoá. (Kèm fix `202607150006`: bật lại RLS cho `waybills`.)

## 5c. Fetcher nguồn #2 — ĐÃ TRIỂN KHAI (`Services/StockCheckSyncService.cs`)

- `FetchStockCheckWaybillsAsync()` — lấy toàn bộ danh sách "Số đơn tồn" của ngày hôm nay bằng **phân trang song song** (giống nguồn #1): trang 1 lấy tổng số trang → trang 2..N song song `PageConcurrency=5`, `pageSize=100`, cửa sổ `today 00:00:00 → today 23:59:59`.
- Endpoint: `opt_stocktaking_ret_detail`; parser dung sai (records ở `data.records/list/rows`; mã ở `billcode/waybillNo/...`) để bền với khác biệt field.
- **Cần kiểm chứng 1 lần**: vì không lấy được body payload, nếu log `[StockCheckSync] page=1 records=0` trong khi console hiện Số đơn tồn > 0 → điều chỉnh `BuildPayload` (VD bỏ/đổi `dimension`/`isFlag`, hoặc thêm field lọc kiểm kho). Sau khi đúng, gọi kết quả qua `ingest_stockcheck_waybills` + `reconcile_inventory_sources` (đã có ở DB) để hợp nhất union.

## 6. Việc cần làm tiếp
1. Khảo sát live Nguồn #2 (Thống kê kiểm kho): xác nhận cột export, endpoint, ý nghĩa "Số đơn tồn", có mã vận đơn trong file export không.
2. Xác nhận đầy đủ các lựa chọn "Phạm vi lựa chọn" của Nguồn #1 và hành vi từng lựa chọn (đặc biệt "Thời gian hàng đến bưu cục" — có thể vá điểm mù nhảy mã ngay trong Nguồn #1).
3. Chốt các cột/flag mới (`seen_in_*`, `suspected_stray`) → migration khi chủ dự án duyệt.

> Lưu ý: thêm cột/flag vào `waybills` thuộc vùng schema migration (protected) — sẽ triển khai khi có yêu cầu cụ thể.
