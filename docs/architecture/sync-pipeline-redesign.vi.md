# Tái thiết kế pipeline đồng bộ FullStackForm (4 điểm)

Theo yêu cầu: (1) lấy danh sách bằng **export**, (2) chỉ tracking **hành trình vận chuyển**,
(3) **order detail on-demand** khi click/double-click, (4) **tuần tự**: route history trước, thông tin bổ sung sau.

## Trạng thái triển khai

| # | Yêu cầu | Trạng thái |
|---|---|---|
| 2 | Sync cycle chỉ kéo hành trình (route history) | ✅ Đã làm |
| 4 | Ghi route history trước, bổ sung sau | ✅ Đã làm |
| 3 | Order detail chạy khi click/double-click, cập nhật DB | ◑ Viewer đã có; cần thêm ghi DB (xem 3.3) |
| 1 | Lấy danh sách bằng export | ◑ Đã map endpoint; cần triển khai client (xem 1.x) |

## 2 & 4 — ĐÃ LÀM (`FullStackTrackingEnrichmentService.EnrichWithResultAsync`)

- **Bỏ Stage 2 (order detail)** khỏi enrichment tự động. Sync nền giờ **chỉ gọi `keywordList`** (hành trình vận chuyển), song song 8 luồng.
- **Ghi route history TRƯỚC** (`UpsertTrackingRowsAsync`) → dashboard phản ánh chuyển động ngay; **sau đó** mới ghi bổ sung (tracking events, journey history, mark enriched) → không gom vào làm chậm.
- An toàn dữ liệu: repository `MergeTracking` dùng `Keep()` — sync route-only gửi detail = "empty" **không xóa** detail đã cache. Detail chỉ được điền khi người dùng mở đơn.
- Ghi chú: path cũ `DatabaseTracking.RunBackgroundTrackingAsync` (Main.cs) vẫn giữ order-detail nhưng đã **cache-once** (chỉ gọi cho đơn mới) từ lần tối ưu trước — không đổi để tránh ảnh hưởng caller khác (PHATLAI...).

## 3 — Order detail on-demand (còn 1 bước)

- Viewer **đã có**: `FullStackOperation.WaybillDetail` — double-click đơn (`TabDash_CellDoubleClick`) → gọi `getOrderDetail` (+ `commonWaybillListByWaybillNos`, `getWaybillsByReverse`) và hiển thị panel phải trên WebView2.
- **Còn thiếu (3.3)**: viewer hiện **chưa ghi lại DB**. Cần: sau khi resolve detail, map sang `WaybillDbModel` (các trường detail) và gọi `_repository.UpsertTrackingRowsAsync` cho **một** đơn đó (repo `Keep()` sẽ chỉ cập nhật detail, giữ nguyên route). Đây là thay đổi UI-wiring, nên làm riêng và test trên máy có JMS session.

## 1 — Lấy danh sách bằng export (đã map endpoint, cần client)

Export là **job bất đồng bộ 3 bước** (khảo sát live 2026-07-16):

1. **Tạo task**: `POST /businessindicator/bigdataReport/pageExcelByTask/take_ret_mon_detail_doris2`
   — cùng payload với list (`actionSiteCode, startDate, endDate, dimension, isFlag, countryId, ...`).
   Trả về ngay; task chạy nền (với ~2.663 đơn hoàn tất gần như tức thì).
2. **Poll trạng thái**: `POST /financialmanagement/gateway/ylspmarkexportserver/arkExportTask/list`
   — trả danh sách task kèm **tiến độ** (`成功`/thành công) và **URL file** khi sẵn sàng. File lưu 7 ngày.
3. **Tải + parse**: tải Excel từ URL trong response, parse cột **Mã vận đơn** (đã có **ClosedXML** trong project).

### Ưu / nhược so với phân trang song song (hiện tại)
- **Export**: 1 file đầy đủ (không cần page) — nhưng thêm độ trễ queue → poll → download → parse XLSX, và **không stream** được mã theo trang (mất producer/consumer khởi động tracking sớm).
- **Phân trang song song** (đã tối ưu): ~43 request/5 luồng (~vài giây), **stream** mã theo từng trang để tracking chạy ngay.

### Đề xuất
- Dùng **export cho full-refresh định kỳ** (1–2 lần/ngày, cần trọn vẹn) và **phân trang song song cho delta thường xuyên** (stream nhanh).
- Cần triển khai `InventoryExportClient`:
  - `CreateExportTaskAsync(params)` → gọi (1), lấy `taskId`.
  - `PollUntilReadyAsync(taskId, timeout)` → gọi (2) tới khi `成功`, lấy `fileUrl`.
  - `DownloadAndParseWaybillsAsync(fileUrl)` → tải + ClosedXML đọc cột mã.
  - Cần **khảo sát 1 lần response schema của `arkExportTask/list`** (tên field taskId/progress/fileUrl) và **header cột Excel** để parse chính xác — chưa lấy được body qua CDP/iframe trong phiên này.

## Việc còn lại (khi được duyệt)
1. Khảo sát response `arkExportTask/list` + header Excel → hiện thực `InventoryExportClient` (#1).
2. Thêm ghi DB cho viewer on-demand (#3.3).
