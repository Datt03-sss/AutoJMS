# AutoJMS — Kế hoạch Hybrid Local-first + Supabase (đồng bộ realtime)

> Phân tích hiện trạng lưu trữ/xử lý dữ liệu, so sánh với mô hình hybrid, và kế hoạch triển khai cụ thể.
> Mô hình mục tiêu (theo yêu cầu): **local-first → sync lên Supabase → realtime, nhiều máy cùng xử lý; mỗi lần máy nào truy vấn sẽ tự lấy dữ liệu mới nhất (newest-wins).**

---

## 1. Hiện trạng (đã kiểm chứng trong mã nguồn)

| Thành phần | Thực tế |
|---|---|
| Đường dữ liệu **đang chạy** (tabDash) | **100% SQLite local**. `FullStackOperation` → `FullStackDashboardService.LoadSnapshotAsync` → `FullStackWaybillRepository.GetDashboardRowsAsync` (`SELECT … FROM fs_waybills WHERE is_in_current_inventory=1`). DB: `…\AppData\FullStack\journey_history.db` (WAL, busy_timeout=5000). |
| Nguồn dữ liệu | Gọi **thẳng JMS API** (inventory `take_ret_mon_detail_doris2`, tracking `keywordList`, `getOrderDetail`) → ghi SQLite. |
| Đường Supabase (cloud) cho **đơn hàng** | **Đang bị tắt runtime** — 2 chỗ `return;` `[TEMP DISABLE]` trong `Main.cs` (`RunStartupSyncAsync`, `HandleAutoSyncTickAsync`). Code còn (bảng `waybills`, lease, RPC `upsert_new_waybills` / `merge_waybill_tracking_rows`) nhưng không được gọi. |
| Supabase còn dùng cho | **Module/auto-update manifest** (`SupabaseModuleProvider`) và **license/anon-key**. Không dùng cho dữ liệu đơn. |
| Mô hình nhiều máy | Nhiều máy chung 1 tài khoản JMS + 1 bưu cục (`actionSiteCode`/`MiddleCode` từ license), mỗi máy 1 HWID. Tier: BASE = thủ công; ULTRA = có dashboard + sync nền. |

**Hai hệ quả quan trọng của trạng thái local-only hiện tại:**

1. **Mỗi máy là một ốc đảo.** Ghi chú, `is_checked`, task, sao (star), đánh dấu đã xử lý, trạng thái đăng ký kiện vấn đề… chỉ nằm trong SQLite của máy đó. Nhân viên khác **không thấy** thao tác của nhau.
2. **Tải JMS bị nhân lên.** N máy dashboard = N lần kéo full tồn kho từ JMS → tăng rủi ro bị rate-limit/khóa. Cơ chế **lease** cũ của Supabase sinh ra chính là để chỉ 1 máy kéo rồi chia sẻ — nhưng lease không còn tác dụng ở chế độ local-only.

> Ghi chú kỹ thuật: `FullStackOperation._cloudData` **bất chấp cái tên**, thực chất là dữ liệu SQLite (không phải cloud). Đừng nhầm với `Main._cloudData` (thuộc cụm Supabase đã tắt).

---

## 2. So sánh: Local-only (hiện tại) vs Hybrid (local-first + Supabase)

| Tiêu chí | Local-only (hiện tại) | Hybrid local-first + Supabase |
|---|---|---|
| **Cộng tác nhiều máy** | ❌ Không chia sẻ (ốc đảo) | ✅ Note/check/task/done/kiện vấn đề thấy được giữa các máy (realtime) |
| **Đồng nhất số liệu** | ❌ Mỗi máy một phiên bản | ✅ Mọi máy cùng 1 nguồn canonical (Supabase), newest-wins |
| **Tải JMS** | ❌ N× (mỗi máy tự kéo) | ✅ 1× (1 máy giữ lease kéo → chia sẻ), các máy khác lấy từ cloud |
| **Tốc độ đọc / UX** | ✅ Rất nhanh (local) | ✅ Vẫn nhanh — đọc vẫn từ SQLite; cloud chạy nền |
| **Hoạt động offline** | ✅ Luôn chạy | ✅ Vẫn chạy (local-first + outbox), sync lại khi có mạng |
| **Sao lưu / khôi phục** | ❌ Mất nếu hỏng/cài lại máy | ✅ Bản canonical trên cloud |
| **Độ phức tạp** | ✅ Thấp | ⚠️ Cao hơn (2 store, reconcile, RLS) |
| **Chi phí** | ✅ 0 | ⚠️ Supabase (đội nhỏ ~ free/Pro là đủ) |
| **Bảo mật (COD/địa chỉ/SĐT)** | ✅ Chỉ nằm máy nội bộ | ⚠️ Lên cloud → **bắt buộc RLS theo bưu cục + token phạm vi** |
| **Công sức triển khai** | ✅ Đã xong | ⚠️ Trung bình (theo phase) |

**Kết luận:** với nhu cầu *nhiều máy + cần chia sẻ + đồng nhất*, **hybrid là lựa chọn đúng**. Nhưng phải làm theo hướng **local-first** (SQLite vẫn là nguồn đọc chính, nhanh, offline) — Supabase chỉ là **lớp đồng bộ/chia sẻ realtime**, không phải nguồn đọc chính. Tránh mô hình "cloud-primary" (chậm, phụ thuộc mạng) mà bản Supabase cũ từng đi theo.

---

## 3. Giải pháp đề xuất — kiến trúc Local-first Hybrid

### 3.1 Nguyên tắc cốt lõi
1. **SQLite là nguồn đọc chính (authoritative read).** Dashboard đọc từ SQLite → giữ nguyên toàn bộ tối ưu đã làm (producer/consumer, cache-once, WAL). Đọc **không bao giờ chờ mạng**.
2. **Ghi local trước, sync sau (outbox).** Mọi thao tác ghi vào SQLite ngay (UX tức thì) → đẩy lên Supabase nền. Mất mạng vẫn ghi được, có mạng thì flush.
3. **Supabase = bản canonical chia sẻ theo bưu cục.** Tenant = `site_code` (= `MiddleCode`/`actionSiteCode`).
4. **Realtime + delta-pull.** Máy khác đổi dữ liệu → Supabase Realtime đẩy về → merge vào SQLite → refresh UI (`RunStreamRefreshAsync`). Đồng thời **mỗi lần refresh/truy vấn**, máy kéo delta (`updated_at > cursor`) để chắc chắn có bản mới nhất.
5. **Newest-wins.** Xung đột giải theo `updated_at` mới nhất. Ghi chú (notes) là **append-only** (không xung đột). Check/done/star/task = last-write-wins theo `updated_at` + actor.

### 3.2 Luồng dữ liệu (tóm tắt)
```
Kéo tồn kho (chỉ MÁY GIỮ LEASE):
   JMS API ──► SQLite local ──► push bản enriched lên Supabase.waybills (site-scoped)

Máy khác (không giữ lease):
   Supabase (realtime + delta-pull) ──► merge vào SQLite ──► refresh dashboard
   (KHÔNG gọi lại JMS trừ khi lease trống/hỏng → fallback tự kéo)

Thao tác nhân viên (note/check/task/done/issue):
   UI ──► SQLite ngay ──► outbox ──► Supabase ──► realtime ──► các máy khác merge
```

### 3.3 Dữ liệu nào đồng bộ (2 nhóm)
- **Nhóm A — Đơn + tracking (chia sẻ, ít xung đột):** các cột lõi của `fs_waybills` (mã, trạng thái/thao tác, thời gian, tồn kho, COD, địa chỉ, `ma_doan_*`…). Do máy giữ lease kéo & ghi → newest-wins theo `updated_at`. Đây là phần cắt được tải JMS.
- **Nhóm B — Cộng tác/workflow (giá trị chia sẻ cao nhất):** `fs_order_notes` (append-only), `fs_order_checks`/`is_checked`, `fs_dispatch_tasks`, star, done-mark, trạng thái đăng ký kiện vấn đề, `fs_order_state_history`. Ai cũng phải thấy realtime.

### 3.4 Tenancy & bảo mật (bắt buộc)
- Thêm `site_code` vào mọi bảng cloud; bật **RLS**: mỗi license chỉ đọc/ghi được `site_code` của mình. Dữ liệu chứa COD/SĐT/địa chỉ → **không** để anon key đọc toàn bộ.
- Nâng cấp từ "1 anon key chung" → **token có phạm vi theo bưu cục** (JWT có claim `site_code`, cấp bởi license server). Đây là điểm hardening quan trọng.

### 3.5 Tận dụng lại code sẵn có
- **Lease** (`try_acquire/refresh/release/complete_inventory_lease`) — dùng lại nguyên vẹn để quyết định "máy nào kéo JMS".
- **`SupabaseDbService`** (client + RPC) đã dựng sẵn, chỉ đang tắt → bật lại có kiểm soát (chỉ ULTRA, có cờ cấu hình).
- **`merge_waybill_tracking_rows` / `upsert_new_waybills`** — tái sử dụng cho push Nhóm A (bổ sung `site_code` + `updated_at`).
- **`fs_sync_state`** (key/value) — lưu cursor delta-pull (`last_pulled_waybills_at`, …) mà **không cần đổi schema**.

---

## 4. Kế hoạch triển khai theo phase

> Quy ước: mỗi phase có tiêu chí "Done" rõ ràng; luôn build + verify (theo CLAUDE.md) trước khi commit. **Không** đổi schema DB / cấu hình Supabase production nếu chưa có owner duyệt (xem §6).

### Phase 0 — Thiết kế & phê duyệt (không rủi ro code)
- Chốt tenant = `MiddleCode`/`actionSiteCode`.
- Thiết kế schema Supabase: `waybills` (+`site_code`,`updated_at`,trigger), bảng workflow (`order_notes`,`order_checks`,`dispatch_tasks`), **RLS theo site_code**, **publication realtime**.
- Chốt hợp đồng sync: bảng nào, delta theo `updated_at`, newest-wins, notes append-only.
- **Done:** tài liệu schema + policy RLS được owner duyệt.

### Phase 1 — Chia sẻ đơn + tracking qua lease (đọc là chính)
- Bật lại `SupabaseDbService` (có cờ cấu hình, chỉ ULTRA).
- Máy **giữ lease**: sau khi `SyncInventoryAndRefreshTrackingAsync` ghi SQLite → push rows (Nhóm A, có `site_code`) lên Supabase.
- Máy **không giữ lease**: khi refresh → delta-pull (`updated_at > cursor`) từ Supabase vào SQLite thay vì gọi JMS; nếu lease trống/hỏng → fallback tự kéo JMS.
- Realtime subscribe `waybills` (lọc theo `site_code`) → upsert SQLite → `RunStreamRefreshAsync`.
- **Done:** 3 máy cùng bưu cục thấy cùng danh sách; JMS chỉ bị gọi bởi 1 máy; đo được tải JMS giảm ~N×.

### Phase 2 — Đồng bộ cộng tác/workflow (điểm ăn tiền)
- Mirror `order_notes`/`order_checks`/`dispatch_tasks`/star/done/issue-status lên Supabase (site-scoped; notes append-only; còn lại newest-wins).
- Ghi local ngay → outbox → push → realtime → máy khác merge.
- **Done:** máy A thêm ghi chú / đánh dấu đã xử lý → máy B thấy trong ~1–2s mà không cần bấm đồng bộ.

### Phase 3 — Chống mất mạng & reconcile
- **Outbox** trong SQLite (bản ghi chờ sync + cờ trạng thái); flush khi có mạng lại; newest-wins khi remote mới hơn (đơn) / merge (workflow).
- Delta-pull cursor + initial full-sync cho máy mới cài.
- Heartbeat lease; chế độ degraded (offline → local-only + banner "chưa đồng bộ").
- **Done:** rút mạng vẫn làm việc; cắm lại tự đồng bộ đủ 2 chiều, không mất/không trùng.

### Phase 4 — Hardening & dọn dẹp
- RLS chặt theo `site_code`; thay anon key chung bằng **token có phạm vi**; rà soát PII (COD/SĐT).
- Quan trắc: số bản pending, xung đột, độ trễ sync.
- Gỡ/nhập lại cụm legacy chết (`DatabaseTracking`, `InventorySyncService`, `Main._cloudData`) để tránh nhầm lẫn.
- **Done:** không rò rỉ chéo bưu cục; log sync sạch; không còn code cloud "chết".

---

## 5. Rủi ro & giảm thiểu

| Rủi ro | Giảm thiểu |
|---|---|
| Sai lệch/đồng bộ lỗi (2 store) | Local-first (SQLite luôn là nguồn đọc); quy tắc newest-wins đơn giản; notes append-only; làm theo phase, có verify |
| Phụ thuộc mạng | Local-first + outbox → offline vẫn chạy; sync khi có mạng |
| Rò rỉ dữ liệu chéo bưu cục | RLS theo `site_code` + token phạm vi (Phase 4 nhưng thiết kế từ Phase 0) |
| Tải/ chi phí Supabase | Chỉ đẩy delta; đội nhỏ → free/Pro đủ; realtime lọc theo site |
| Đổi schema / cấu hình production | Là **file protected** → cần owner duyệt trước (xem §6) |
| Xung đột ghi đồng thời | `updated_at` newest-wins + actor; workflow tách bảng để giảm đụng độ |

---

## 6. Ràng buộc từ CLAUDE.md (cần owner duyệt riêng)
Các thay đổi sau **không** được tự ý làm nếu chưa có yêu cầu cụ thể của owner:
- **Database schema migrations** (thêm `site_code`/bảng workflow ở cả Supabase và SQLite outbox).
- **Supabase production config** (URL/anon key/RLS/publication).
- Các file protected khác giữ nguyên (Main.cs, Licensing, Velopack, release scripts).

> Khuyến nghị: Phase 0 chốt thiết kế + owner duyệt schema/RLS; từ Phase 1 mới đụng code service (không protected: `FullStack/*`, `SupabaseDbService.cs`).

---

## 7. Tóm tắt khuyến nghị
Giữ nguyên **local-first SQLite** làm lõi đọc nhanh (đã tối ưu), thêm **Supabase làm lớp đồng bộ realtime chia sẻ theo bưu cục**: 1 máy giữ lease kéo JMS → chia sẻ cloud (cắt tải JMS + đồng nhất số liệu), workflow ghi local rồi sync để nhiều máy cùng xử lý, newest-wins theo `updated_at`, realtime + delta-pull để "máy nào truy vấn cũng có bản mới nhất". Triển khai theo 5 phase, ưu tiên Phase 1 (chia sẻ đơn + giảm tải JMS) và Phase 2 (cộng tác) vì đúng 2 mục tiêu bạn chọn.
