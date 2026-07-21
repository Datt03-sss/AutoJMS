# AutoJMS — Event-Sourcing-Lite + Operational Data Store (ODS)

> Mở rộng của `hybrid-supabase-sync-plan.md`. Chuẩn hóa dữ liệu thành **event** append-only,
> projection (`fs_waybills`) được **dựng lại từ event theo `event_time`**. Không Kafka/ClickHouse
> — tận dụng SQLite + Supabase/Postgres hiện có. Kafka/ClickHouse chỉ là đích Phase 4 khi volume
> chứng minh cần.

## 1. Vì sao event, không phải "order row"

Đồng bộ row theo `updated_at` (write-time) là **lossy** và có thể **lùi trạng thái**: một máy ghi
muộn (updated_at mới) một quan sát cũ (event_time cũ) sẽ đè lên trạng thái mới hơn. Event-sourcing
giải quyết bằng cách:

- **Append-only + dedupe theo fingerprint** — không xung đột, không mất lịch sử.
- **Projection fold theo `event_time`** (observation-time), không phải `updated_at`. Đây là điều
  làm invariant "máy A 07:01 vs máy B 07:00 → không lùi" luôn đúng.
- **Client cũng là nguồn** — mở đơn/thao tác sinh observed event, đóng góp vào kho chung; nhưng
  **không** quyết định inventory membership (chỉ leader full-sync phát `InventoryLeft`).

## 2. Đã triển khai (scope A + B, additive, mặc định TẮT)

Cờ `EventPipelineEnabled` trong `AutoJMS.json` (mặc định `false`) — bật mới chạy, nên **không đổi
hành vi dashboard** cho tới khi cutover scope C.

### Event envelope
`FullStack/Events/FullStackEvent.cs` — `event_id, waybill_no, event_type, event_time, source,
source_client, fingerprint, payload, observed_at, schema_version, seq`.

7 event type: `TrackingObserved, InventorySeen, InventoryLeft, OrderDetailObserved,
ManualNoteAdded, CheckUpdated, DispatchTaskUpdated`.

### Fingerprint (chống trùng cross-machine)
`FullStack/Events/EventFingerprint.cs` — `SHA256(waybill_no | event_type | event_time |
payload-ngữ-nghĩa)`. **Loại bỏ** `observed_at / source_client / event_id` để 2 máy quan sát cùng
trạng thái JMS ra cùng hash → dedupe thật.

### Local event log + projection fold
`FullStack/Events/FullStackEventLog.cs` — append/dedupe vào `fs_events` (SQLite, schema V3),
`FoldProjectionAsync` dựng latest state theo `event_time ASC` (sẵn sàng cho cutover scope C).

### Emitters (shadow)
`FullStack/Events/FullStackEventPipeline.cs` — façade no-op khi tắt cờ. Gắn vào:
- `FullStackTrackingEnrichmentService`: `TrackingObserved` + `OrderDetailObserved` sau mỗi enrich.
- `FullStackWorkflowService`: `ManualNoteAdded / CheckUpdated / DispatchTaskUpdated`.

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

## 3. Chưa làm (scope C — cutover, cần test kỹ)

Chuyển projection `fs_waybills` sang **derive-from-events** làm nguồn chính: sửa
`merge_waybill_rows_v2` (Supabase) + đường đọc dashboard để ưu tiên `FoldProjectionAsync`. Rủi ro
cao vì đổi luồng đang chạy → làm sau khi scope A/B chạy shadow ổn định và đối chiếu khớp.

## 4. Đánh giá roadmap Kafka/ClickHouse (kế hoạch 6 tuần)

Đúng hướng cho logistics đa chi nhánh hàng triệu event/ngày, **sai thời điểm** cho AutoJMS:

- ClickHouse là OLAP (update/delete kém) — **không** phải ODS realtime; ODS đúng là Postgres.
- SignalR/Kafka cần server luôn chạy → phải tách `AutoJMS.DataHub` trước; đó là quyết định
  **trách nhiệm vận hành** (client không nên giữ token JMS), không phải volume.
- "Chỉ gửi Delta/Aggregated qua mạng" đã đạt bằng delta-pull cursor + realtime doorbell hiện có.

Ngưỡng tách DataHub nên ưu tiên trách nhiệm (đa site, bỏ token ở client) hơn con số event/ngày.

## 5. Test cases

- Follower mở đơn → `TrackingObserved` ghi local, push cloud, máy khác thấy trong vài giây.
- 2 máy fetch cùng trạng thái → cùng fingerprint → chỉ 1 event (dedupe).
- Máy A event 07:01, máy B 07:00 → fold theo event_time không lùi trạng thái.
- Leader full-sync phát hiện đơn rời tồn → `InventoryLeft`; follower không tự đảo membership.
- Mất mạng → event vào outbox, cắm lại flush; seq cursor đảm bảo không trùng/không sót.
