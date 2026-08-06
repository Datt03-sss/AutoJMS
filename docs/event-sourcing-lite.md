# AutoJMS — Event-Sourcing-Lite + Operational Data Store (ODS)

> ⚠️ **Tài liệu này có 2 phần tách bạch — đừng trộn:**
> - **Current v1 (đã code, mặc định TẮT) = defects, không phải hợp đồng đích.** Code hiện `fs_events`
>   append + `FoldProjectionAsync` sort theo `event_time`; emitter **vẫn phát `OrderDetailObserved`
>   như event** (`FullStackEventPipeline`); **chưa có snapshot store**; fingerprint/cursor có defect.
> - **Target v2 (hợp đồng đích) = `datahub-p0-contract.md`.** Transition append-only + **OrderDetail
>   là snapshot store riêng** (không phải event); projection **một writer/RPC** ordering theo nhóm
>   field + `source_event_at→received_at→seq`; rebuild = **events + snapshot store**.
> Không Kafka/ClickHouse (đích Phase 4 nếu volume chứng minh cần).

## 1. Vì sao event, không phải "order row" (định hướng Target v2)

Đồng bộ row theo `updated_at` (write-time) là **lossy** và có thể **lùi trạng thái**: một máy ghi
muộn (updated_at mới) một quan sát cũ (event_time cũ) sẽ đè lên trạng thái mới hơn. Event-sourcing
giải quyết bằng cách:

- **Append-only + dedupe theo fingerprint** — không xung đột, không mất lịch sử.
- **Projection theo thứ tự nghiệp vụ** (`source_event_at→received_at→seq`, Target v2), không phải
  `updated_at`. (⚠️ Current v1 code còn sort `event_time` — sẽ viết lại.)
- **Client cũng là nguồn** — thao tác sinh observed event/đóng góp; nhưng **không** quyết định
  inventory membership (chỉ leader phát `InventoryLeft`).

## 2. Current v1 — ĐÃ CODE (mặc định TẮT) = defects, KHÔNG phải hợp đồng đích

Cờ `EventPipelineEnabled` trong `AutoJMS.json` (mặc định `false`). Trạng thái thực của code:
- `fs_events` append + `FoldProjectionAsync` sort theo `event_time` (mô hình cũ).
- Emitter **vẫn phát `OrderDetailObserved` như event** (`FullStackEventPipeline`) — **chưa** có snapshot
  store; Target v2 sẽ chuyển detail sang snapshot store.
- Fingerprint gồm `eventTime=now` cho detail/inventory/workflow → **không dedupe**; cursor `MAX(remote_seq)`
  có thể kẹt. → tất cả là **defect**, sửa khi làm P0.5/Target v2. (Emitter tắt nên chưa tác động sản xuất.)

### Event envelope
`FullStack/Events/FullStackEvent.cs` — `event_id, waybill_no, event_type, event_time, source,
source_client, fingerprint, payload, observed_at, schema_version, seq`.

**Transition event (append-only)** — 6 loại: `TrackingObserved, InventorySeen, InventoryLeft,
ManualNoteAdded, CheckUpdated, DispatchTaskUpdated`.
> ⚠️ **`OrderDetail` KHÔNG phải transition event** — là **snapshot mutable** lưu ở **snapshot store
> riêng** (`waybill_detail_snapshot` + history), CAS theo revision, KHÔNG nằm trong `waybill_events`,
> KHÔNG dedupe-bằng-nội-dung. (Bản trước liệt kê `OrderDetailObserved` là event — **đã bỏ**.) Chi tiết
> P0-B / P0-contract.

### Fingerprint (chống trùng cross-machine)
> ⚠️ **DEFECT hiện tại (sửa ở P0-B).** `EventFingerprint.Compute` luôn đưa `eventTime` vào hash,
> nhưng `FullStackEventPipeline` dùng `DateTime.UtcNow` làm eventTime cho `OrderDetailObserved`,
> `InventorySeen/Left` và workflow ⇒ **mỗi lần fetch cùng dữ liệu tạo fingerprint mới**, không dedupe
> như câu dưới tuyên bố. (Emitters đang tắt nên chưa có tác động sản xuất.)

**Công thức đúng — theo từng event-type, TÍNH LẠI PHÍA SERVER, kèm `fingerprint_version`** (không tin
fingerprint desktop gửi):
- **Tracking:** `waybill | type | JMS event_time (source_event_at) | canonical(payload)`.
- **Inventory:** `waybill | type | run_id | membership` (leader).
- **Workflow:** `waybill | type | client action id ổn định`.
- **OrderDetail:** KHÔNG có fingerprint transition — là **snapshot store** (`waybill_detail_snapshot`
  + history), CAS theo revision: `>` apply, `=` no-op-nếu-canonical-giống-else-**conflict**, `<` stale.
  **Bỏ `observation_epoch`.** Rebuild `waybills` = **events + snapshot store** (không chỉ event log).

Loại `observed_at thô / source_client / event_id` khỏi mọi công thức. Edge/private RPC tính fingerprint
chuẩn khi nhận. Chi tiết + field-mapping (per-nhóm) + snapshot CAS/ACK + replay/backfill:
`datahub-p0-contract.md` P0-B.

### Local event log + projection fold
`FullStack/Events/FullStackEventLog.cs` — append/dedupe vào `fs_events` (SQLite, schema V3),
`FoldProjectionAsync` dựng latest state.
> ⚠️ **KHÔNG "sẵn sàng cutover".** `FoldProjectionAsync` hiện: (1) chỉ dựng **tập nhỏ** trường (thiếu
> field-mapping đầy đủ — P0-B), (2) sort theo **`event_time`** (mô hình cũ) chứ chưa theo hợp đồng
> `source_event_at → received_at → seq`, (3) chưa tách snapshot vs transition. Reducer phải **viết lại**
> theo P0-B trước khi cutover; đừng coi bản hiện tại là dùng được cho projection chính thức.

### Emitters (Current v1 — reality)
`FullStack/Events/FullStackEventPipeline.cs` — façade no-op khi tắt cờ. **Hiện code:**
- `FullStackTrackingEnrichmentService`: phát `TrackingObserved` **và `OrderDetailObserved` (như event)**
  — **CHƯA có snapshot store**.
- `FullStackWorkflowService`: `ManualNoteAdded / CheckUpdated / DispatchTaskUpdated`.
> **Target v2:** `OrderDetailObserved` **bỏ khỏi event**, chuyển sang **snapshot store** (CAS revision +
> FIFO seq). Emitter sẽ viết lại: tracking→transition event; detail→snapshot writer RPC.

### Remote event store (Supabase)
`backend/supabase/migrations/202607110002_event_store.sql`:
- Bảng `waybill_events` — `seq bigint identity` (thứ tự server-assigned, chống lệch đồng hồ),
  `unique(site_code, fingerprint)` (dedupe), RLS theo `jwt_site_code()`, realtime publication.
- RPC `append_waybill_events(site, events)` — append/dedupe, trả số dòng mới.
- RPC `pull_events_delta(site, since_seq, limit)` — delta theo **seq cursor** (không phụ thuộc
  đồng hồ client).

### Sync wiring
`SupabaseDbService.Hybrid.cs`: `AppendWaybillEventsAsync / PullEventsDeltaAsync` + realtime
`waybill_events`. `FullStackCloudSyncService`: outbox thêm kind `EVENT` (push observation lên
cloud), `PullEventsAsync` kéo delta theo seq → merge vào `fs_events` local.

> ⚠️ **DEFECT cursor (sửa ở P0-C).** `GetMaxRemoteSeqAsync` lấy `MAX(remote_seq)` từ event đã insert;
> nhưng khi fingerprint đã tồn tại, `INSERT OR IGNORE` **không gắn `remote_seq`** ⇒ cursor không tiến
> → kéo lại cùng trang mãi. Row delta `updated_at > cursor + LIMIT` cũng **bỏ sót** khi nhiều dòng
> trùng timestamp vượt limit. **Hợp đồng cursor đúng (CHỐT ở P0-C — thay thế bản cũ):** khoá cursor là
> **`change_seq` server (đơn điệu, cấp dưới per-site lock)**, tuple **`(change_seq, stable_id)`** cho CẢ
> event lẫn row — **KHÔNG** dùng `(updated_at, stable_id)` nữa (commit muộn mang `now()` cũ → sót). Apply
> delta bằng **UPSERT theo `canonical_event_id`** (KHÔNG `INSERT OR IGNORE` — nếu không promotion/rehash
> mất). Lưu cursor độc lập trong `fs_sync_state`, checkpoint page-max trong cùng transaction với apply.

## 3. Quyền sở hữu projection — CHỐT một writer duy nhất (P0-B)

`waybills` phải có **đúng một writer**. **CHỐT (a):** append event + cập nhật projection trong **cùng
một RPC/transaction** nguyên tử — RPC đó là writer duy nhất; cả Worker lẫn Edge-contributor gọi chung.
**Gỡ** đường Worker vừa `merge_waybill_rows_v2` vừa append event ở hai thao tác rời. (Loại (b)
projector-process riêng — thêm moving part, không chọn.)

Scope C (derive-from-events, thứ tự `source_event_at → received_at → seq`, viết lại reducer + writer
RPC + đường đọc dashboard) **chưa làm** — chỉ mở sau khi P0-B chốt field-mapping + ACK và scope A/B
chạy shadow đối chiếu khớp.

## 4. Đánh giá roadmap Kafka/ClickHouse (kế hoạch 6 tuần)

Đúng hướng cho logistics đa chi nhánh hàng triệu event/ngày, **sai thời điểm** cho AutoJMS:

- ClickHouse là OLAP (update/delete kém) — **không** phải ODS realtime; ODS đúng là Postgres.
- SignalR/Kafka cần server luôn chạy → phải tách `AutoJMS.DataHub` trước; đó là quyết định
  **trách nhiệm vận hành** (client không nên giữ token JMS), không phải volume.
- "Chỉ gửi Delta/Aggregated qua mạng" đã đạt bằng delta-pull cursor + realtime doorbell hiện có.

Ngưỡng tách DataHub nên ưu tiên trách nhiệm (đa site, bỏ token ở client) hơn con số event/ngày.

## 5. Acceptance tests (Target v2 — theo hợp đồng P0, KHÔNG theo ordering v1)

- Follower mở đơn → `TrackingObserved` push qua Edge (fingerprint server-recompute), máy khác thấy vài giây.
- 2 máy quan sát cùng trạng thái → **cùng fingerprint theo `source_event_at` (không `now`)** → 1 event.
- **Projection order `source_event_at → received_at → seq`** (KHÔNG `event_time` v1): A 07:01 vs B 07:00 → không lùi.
- OrderDetail A→B→A → **snapshot store CAS revision / FIFO `(leader_term, snapshot_seq)`**; retry A muộn KHÔNG đè B.
- Leader phát `InventoryLeft`; contributor **không** đảo membership; contributor timestamp tương lai bị clamp.
- Cursor: **`change_seq` server làm khoá DUY NHẤT (event + row)**; writer serialize/site (gap rollback OK,
  không chờ contiguous). **Promotion/rehash BUMP `change_seq`** → client offline pull `change_seq>cursor`
  KHÔNG bỏ sót promotion (identity `seq` không đổi khi UPDATE). Test "seq thấp commit sau seq cao" 0 sót.
- Rebuild `waybills` = **events + snapshot store** (OrderDetail ở snapshot, không event log); replay tôn trọng
  `authority_class` (Worker thắng contributor) + snapshot FIFO `(leader_term, snapshot_seq)`.
- Mất mạng → outbox flush lại; ACK trả **`request_event_id`+`canonical_event_id`**; `fingerprint_version`
  đổi → reconcile theo `server_seq` nhỏ nhất, không nhân đôi.
