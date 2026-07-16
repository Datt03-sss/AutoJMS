# Phương pháp xử lý tối ưu nhất — đồng bộ tồn kho & tracking FullStackForm

Tài liệu nghiên cứu, tổng hợp và chốt kiến trúc xử lý tối ưu end-to-end cho pipeline:
**lấy danh sách (2 nguồn) → tracking hành trình → ghi database → dashboard**, dưới các ràng buộc thực tế của JMS.

## 1. Ràng buộc chi phối thiết kế

1. **JMS chống khóa IP** — quá nhiều request đồng thời tới `jmsgw` sẽ bị chặn. Đây là ràng buộc số 1.
2. **Phiên/token** — mọi call qua `JmsApiClient` (đính `authToken`, tự refresh 401). Token có thể hết hạn giữa chừng.
3. **Dữ liệu lớn** — mỗi bưu cục ~3.000–4.500 đơn tồn/tháng; tracking là chi phí lặp lại lớn nhất.
4. **Tồn tại "đơn nhảy mã"** — phải union 2 nguồn để không sót (xem `inventory-source-comparison.vi.md`).
5. **Không xóa dữ liệu cũ** — chỉ upsert; dừng theo trạng thái cuối; retention 30 ngày.

## 2. Kiến trúc tối ưu end-to-end

```
             ┌───────────────── GLOBAL JMS GOVERNOR (cap tổng luồng, chống khóa IP) ─────────────────┐
             │                                                                                       │
  ┌──────────┴──────────┐     ┌──────────────────────┐                                              │
  │ Nguồn #1 Big Data    │     │ Nguồn #2 Kiểm kho     │   (chạy SONG SONG, mỗi nguồn phân trang     │
  │ take_ret_mon_..._doris2│   │ opt_stocktaking_ret_ │    song song trang 2..N)                     │
  └──────────┬──────────┘     └──────────┬───────────┘                                              │
             └───────── UNION (dedup theo waybill_no) ─────────┐                                     │
                                                               ▼                                     │
                                     merge_bigdata_detail / ingest_stockcheck_waybills → waybills     │
                                                               │  reconcile_inventory_sources        │
                                                               ▼                                     │
                            SCHEDULER theo next_track_at (chỉ đơn active + tới hạn)                   │
                                                               ▼                                     │
                     Stage 1: route history (keywordList, batch 40) ──► upsert route (ghi TRƯỚC) ────┘
                                                               ▼
                     Stage 2 (bổ sung): tracking events / journey history / risk-SLA
                                                               ▼
                     Order detail: CHỈ khi click/double-click 1 đơn → upsert detail
                                                               ▼
                     Realtime (Supabase) đẩy thay đổi xuống dashboard
```

## 3. Các cơ chế tối ưu

### 3.1 Global JMS concurrency governor — ✅ ĐÃ LÀM (`JmsApiClient`)
- Một `SemaphoreSlim` bọc **mọi** JMS POST → tổng request in-flight app-wide ≤ **12**.
- Ý nghĩa: dù list(#1) 5 luồng + list(#2) 5 luồng + tracking 8 luồng + detail 8 luồng cùng chạy, tổng vẫn ≤ 12 → **không chồng luồng gây khóa IP**, mà vẫn đạt throughput tối đa dưới ngưỡng an toàn.
- Đây là **tầng an toàn nền**: tách biệt "độ song song mong muốn của từng service" khỏi "trần an toàn toàn cục". Chỉnh 1 hằng số `MaxConcurrentJmsRequests` để tinh chỉnh.

### 3.2 Lấy danh sách 2 nguồn song song — ✅ ĐÃ LÀM
- Cả `FullStackInventorySyncService` (#1), `InventorySyncService` (#1 legacy), `StockCheckSyncService` (#2): trang 1 lấy tổng số trang → trang 2..N song song 5 luồng, `pageSize=100`, bỏ delay.
- **Tối ưu union**: chạy 2 fetcher **đồng thời** (`Task.WhenAll`), rồi hợp nhất tập mã (dedup `waybill_no`). Governor tự bảo vệ trần luồng.

### 3.3 Route-history-first, order-detail on-demand — ✅ ĐÃ LÀM
- Sync nền **chỉ** kéo hành trình (`keywordList`), ghi route **trước**, bổ sung sau → dashboard cập nhật ngay.
- Order detail (tĩnh) chỉ tải khi mở 1 đơn → giảm N request lặp mỗi chu kỳ về ~0.

### 3.4 Scheduler theo `next_track_at` — ◑ ĐỀ XUẤT (chưa làm)
- Hiện timer 2 phút re-track **toàn bộ** working set. Với đơn lớn, phần lớn không đổi giữa 2 phút.
- Tối ưu: chỉ track đơn **active + tới hạn** (`next_track_at <= now`), dùng index đã có `idx_waybills_site_due_active`.
- **Interval thích ứng** theo trạng thái:
  - Đơn mới / kiện vấn đề: 15–30 phút.
  - Đơn tồn lâu / rủi ro cao: 1–2 giờ.
  - Đơn ổn định: 6–12 giờ.
  - Đơn terminal (Ký nhận CPN/Kết thúc): dừng (đã có).
- Lợi ích: cắt phần lớn request tracking lặp lại; chi phí mỗi chu kỳ ~ tỉ lệ với số đơn *thực sự* cần cập nhật.

### 3.5 Two-tier cadence — ◑ ĐỀ XUẤT
- **Tier nhanh (mỗi 2–5 phút)**: lấy vài trang đầu (sắp theo thời gian) để bắt đơn mới/đổi + track các đơn tới hạn.
- **Tier đầy đủ (1–2 lần/ngày)**: full-refresh cả 2 nguồn (hoặc export) để đối chiếu + `mark_left_inventory`.
- Realtime đẩy thay đổi xuống UI, không cần poll toàn bảng.

### 3.6 Ghi database — ✅ phần lớn ĐÃ LÀM
- Upsert theo lô 1 RPC (`merge_waybill_tracking_rows`/`merge_waybill_rows_v2`) + fingerprint cache bỏ dòng không đổi.
- **Bổ sung đề xuất**: chunk payload **500–1000 dòng/RPC** khi inventory rất lớn (tránh payload quá to / timeout).

### 3.7 Không xóa + dừng đúng + retention — ✅ ĐÃ LÀM
- newest-wins; `finalize_waybills` + trigger dừng terminal; retention 30 ngày pg_cron; giữ "Kết thúc"/stray tới khi `is_handled`.

## 4. Mô hình chi phí (ước tính, 1 bưu cục ~4.000 đơn)

| Giai đoạn | Trước | Sau (tối ưu) |
|---|---|---|
| Lấy danh sách | 40–214 request **tuần tự** + delay | ~43 request **//5** (~vài giây), ×2 nguồn song song dưới trần governor |
| Order detail | ~4.000 request **mỗi** chu kỳ | ~0 (on-demand) |
| Tracking route | toàn bộ mỗi 2 phút (~106 batch) | chỉ đơn tới hạn (giảm mạnh nhờ interval thích ứng) |
| Ghi DB | 1 RPC (đã tốt) | 1 RPC/chunk + skip fingerprint |
| Trần đồng thời | rời rạc, dễ chồng → khóa IP | **≤ 12 toàn cục** (an toàn + nhanh) |

## 5. Lộ trình còn lại (ưu tiên)

1. **Wire union 2 nguồn**: orchestrator chạy `FetchInventory(#1)` + `FetchStockCheckWaybills(#2)` song song → dedup → `merge_bigdata_detail` / `ingest_stockcheck_waybills` → `reconcile_inventory_sources`. (Cần chốt payload #2 — xem `inventory-source-comparison.vi.md` 5c.)
2. **Scheduler `next_track_at`** + interval thích ứng (3.4).
3. **Two-tier cadence** (3.5).
4. **Chunk upsert** cho inventory rất lớn (3.6).
5. **Ghi DB cho order-detail on-demand** (viewer → upsert 1 đơn).

## 6. Rủi ro & tinh chỉnh
- **Trần governor (12)**: nếu JMS cho phép cao hơn → tăng để nhanh hơn; nếu vẫn bị nhắc IP → giảm. Là 1 hằng số duy nhất.
- **Interval thích ứng**: cần đo thực tế tần suất đổi trạng thái để chỉnh ngưỡng.
- **Payload nguồn #2**: xác minh 1 lần qua log (mục 5c tài liệu so sánh).
