# Kế hoạch database cho FullStackOperation

Cập nhật 2026-08-23. Viết lại toàn bộ theo kiến trúc API hiện tại.

Database phục vụ các tab của cửa sổ FullStackOperation (**tabDash**, **tabThoiHieu**, và các tab
mở rộng). Nó là PostgreSQL trong Docker trên VPS, **chỉ** API `AutoJMS.DataHub.Api` truy cập được:
không publish port ra host, không có client nào nối trực tiếp.

Nguyên tắc đang áp dụng:

- Tenancy theo `site_id` (uuid, FK về `sites`), do **device token** quyết định — không phải do
  client tự khai. Không có RLS, không có policy, không có role `anon`/`authenticated`: việc chặn
  chéo site nằm ở endpoint, nơi có thể xác thực, rate-limit và ghi audit.
- Ghi qua endpoint REST, không qua SQL function. Toàn bộ database chỉ có một function duy nhất là
  `create_datahub_site(...)` — helper cấp phát site cho `scripts/provision-site.ps1`, client không
  gọi được.
- Event log append-only + projection newest-wins. Idempotent theo `event_fingerprint`.
- Realtime là **doorbell**: hub SignalR chỉ báo "có thay đổi", client vẫn pull `/changes` theo
  `change_seq`. Mất hub thì thoái hoá về polling, không mất dữ liệu.
- Retention theo `retention_policies`, chạy bởi background service trong API.

## 1. Hiện trạng — 12 bảng đã dựng

`backend/datahub/migrations/001_core.sql` … `005_change_retention_floor.sql` (forward-only, mỗi
file tự ghi marker `schema_migrations` trong transaction của chính nó).

| Bảng | Dùng cho |
|---|---|
| `sites` | Một dòng mỗi bưu cục; `site_code` là thứ enrollment đối chiếu |
| `devices` | Máy đã enroll; `status` ∈ `active` / `revoked` / `disabled`; unique `(site_id, name)` |
| `waybill_scan_events` | Event log append-only từ `/jms/ingest` |
| `waybill_projections` | Bảng trạng thái hiện tại — nguồn chính của tabDash |
| `dashboard_changes` | Change feed cho `/changes` |
| `site_change_counters` | `change_seq` tăng đơn điệu theo site |
| `site_fetch_leases` | Chọn một máy leader đi fetch JMS |
| `idempotency_records` | Đỡ header `Idempotency-Key` khi ingest |
| `audit_logs` | Enrollment và hành động admin |
| `jms_event_policies` | Map `scan_type_code` → `event_kind` |
| `retention_policies` | Cửa sổ retention từng bảng |
| `schema_migrations` | Marker migration đã áp |

### `waybill_scan_events` — timeline thao tác bưu cục

Nguồn: `podTracking/queryCssWork` phía JMS, đẩy lên qua `POST /api/v1/sites/{siteId}/jms/ingest`.

Cột chính: `site_id`, `waybill_no`, `event_fingerprint`, `fingerprint_version`,
`event_occurred_at`, `ingested_at`, `scan_type_code`, `scan_type_name`, `status`, `network_code`,
`operator_code`, `package_number`, `task_code`, `payload jsonb`.

Chống trùng bằng `UNIQUE (site_id, event_fingerprint)` — không dùng `scan_seq` (số thứ tự JMS
không ổn định giữa các lần fetch). Index đọc: `(site_id, waybill_no, event_occurred_at)`.

### `waybill_projections` — trạng thái hiện tại

Khoá chính `(site_id, waybill_no)`. Ba nhóm slot, mỗi nhóm giữ event mới nhất thuộc loại đó:

| Nhóm slot | Cột | Ý nghĩa |
|---|---|---|
| `state_*` | `state_code/name/event_at/fingerprint/event_id/kind/status/payload` | Trạng thái vận đơn |
| `last_activity_*` | cùng bộ hậu tố | Thao tác cuối cùng |
| `inventory_*` | cùng bộ hậu tố | Tín hiệu kiểm/tồn kho |

Cộng `payload`, `reducer_version`, `version`, `updated_at`.

`*_event_id` là tham chiếu hydrate **không có FK**: retention xoá event không được phép làm một
dòng dashboard hoá không đọc/không xoá được.

Reducer nằm trong API, không trong SQL. `jms_event_policies` quyết định một `scan_type_code` rơi
vào slot nào (`state_transition` / `activity` / `inventory` / `communication`).

### `dashboard_changes` — change feed

`(site_id, change_seq)` PK, `entity_type`, `entity_key`, `operation` ∈ `upsert`/`delete`/`resync`,
`change_at`, `body jsonb`. Client đọc `GET /changes?after={cursor}&limit=` và tự giữ cursor.

## 2. Ánh xạ dữ liệu theo tab

### tabDash — Dashboard đơn tồn realtime

- Snapshot lần đầu: `GET /api/v1/sites/{siteId}/projections/snapshot?limit=`.
- Sau đó chỉ delta: `GET /changes?after={change_seq}`, được doorbell `/hubs/site` đánh thức.
- Cột UI lấy từ `waybill_projections`: mã vận đơn ← `waybill_no`; trạng thái hiện tại ←
  `state_name`; thao tác cuối ← `last_activity_name`; thời gian thao tác ← `last_activity_at`;
  NV xử lý cuối ← `operator_code` trong `last_activity_payload`; cập nhật lúc ← `updated_at`.
- Không cần view SQL: endpoint snapshot đã trả đúng hình dạng client cần.

### tabThoiHieu — Giám sát SLA/thời hiệu

SLA hiện **tính ở client** từ `state_event_at` / `inventory_event_at` / `updated_at`. Chưa có cột
`sla_status`, `sla_deadline`, `days_in_inventory`, `age_hours` trong schema.

Nếu muốn đẩy phép tính về server thì làm trong reducer của API (C#) và thêm cột vào
`waybill_projections` bằng một migration mới — **không** thêm SQL function, vì reducer phải là một
đường ghi duy nhất.

### Tab tương lai

| Tab dự kiến | Nguồn hiện có | Còn thiếu |
|---|---|---|
| Kiện vấn đề | event có `scan_type_code` thuộc nhóm problem | endpoint truy vấn riêng |
| Kiểm kho | slot `inventory_*` | endpoint riêng nếu cần trạng thái đã-kiểm |
| Điều phối | — | bảng + endpoint `tasks` |
| Trọng tài | — | bảng + endpoint `notes` |
| Chat/Zalo | `ZaloChatService` phía client | bảng `chat_messages` nếu cần lưu |

> **Khoảng trống đã biết (P1-1).** Các bảng và endpoint `notes` / `checks` / `tasks` chưa tồn tại.
> Bản kế hoạch cũ liệt kê `order_notes`, `order_checks`, `dispatch_tasks` như "đã có" — không đúng
> với schema hiện tại. Tab nào cần chúng thì phải mở một migration + endpoint mới.

## 3. Chuẩn hoá & quy ước

- **Mọi bảng dữ liệu site**: có `site_id uuid REFERENCES sites(id)` và cột thời gian cập nhật.
- **Idempotent**: bảng append-only dùng fingerprint + `ON CONFLICT DO NOTHING`; ingest thêm một
  lớp `Idempotency-Key` ở `idempotency_records`.
- **Newest-wins** cho projection: chỉ ghi khi event mới hơn slot đang giữ.
- **Realtime**: bảng mới không cần "publication" gì cả — muốn client thấy thay đổi thì API ghi một
  dòng `dashboard_changes` và bắn doorbell.
- **Retention**: thêm dòng vào `retention_policies`; background service trong API dọn theo lô
  (`DATAHUB_RETENTION_BATCH_SIZE`, `DATAHUB_RETENTION_INTERVAL_SECONDS`). `005_change_retention_floor.sql`
  đặt sàn để không xoá mất cursor client đang dùng.
- **Không** thêm function client gọi được, không `CREATE POLICY`, không `GRANT` cho role mới.

## 4. Lộ trình migration khi mở tab mới

Đặt tên tiếp số: `006_*.sql`, `007_*.sql`. Mỗi file:

1. Idempotent (`CREATE TABLE IF NOT EXISTS`, `ADD COLUMN IF NOT EXISTS`).
2. Tự ghi marker `schema_migrations` **trong** transaction của nó — migration chạy xong mà không
   ghi marker bị `apply-migrations.sh` coi là thất bại.
3. Không sửa file migration đã áp. Sai thì thêm file mới.
4. Áp bằng `./bin/apply-migrations.sh --env-file .env.production` (hoặc `apply-migrations.ps1
   -DatabaseUrl`), rồi `EXPLAIN ANALYZE` truy vấn nóng và chạy `./bin/smoke-test.sh --env-file`.

## 5. Sơ đồ quan hệ (rút gọn)

```
sites (id uuid PK, site_code)
   ├─1..n─ devices                (máy đã enroll)
   ├─1..n─ waybill_scan_events    (event log append-only)
   ├─1..n─ waybill_projections    (trạng thái hiện tại, PK site_id+waybill_no)
   ├─1..n─ dashboard_changes      (change feed theo change_seq)
   ├─0..1─ site_change_counters   (bộ đếm change_seq)
   └─0..1─ site_fetch_leases      (leader fetch JMS)
```

`waybill_projections` không có FK về `waybill_scan_events` — cố ý, xem ghi chú ở mục 1.

> Lưu ý: schema migration thuộc vùng Protected Files trong `CLAUDE.md` — chỉ triển khai khi chủ dự
> án yêu cầu cụ thể cho từng migration.
