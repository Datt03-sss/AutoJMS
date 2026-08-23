# AutoJMS DataHub — Thiết kế Fetcher (INDEX)

Cập nhật 2026-08-23.

> **Lịch sử.** Bản v2 của tài liệu này chứa một hợp đồng đã bị thay thế (pg_cron `next_run_at` làm
> scheduler, desktop gọi JMS trực tiếp, fallback per-scope lease, token "vault đơn"). Bản v3 gỡ phần
> thân và biến file thành INDEX, nhưng lại trỏ sang **ba tài liệu không tồn tại**
> (`datahub-master-plan.md`, `datahub-p0-contract.md`, `migration/hybrid-datahub-sync-plan.md`) và mô tả
> một kiến trúc **chưa từng được dựng**: Worker Windows Service, Edge boundary, private RPC, RLS theo
> `site_code`, DB role `worker_gateway`/`datahub_edge`, một DataHub project cho mỗi bưu cục.
>
> Cái đã dựng thật là **một API ASP.NET Core trên VPS** với PostgreSQL riêng trong mạng nội bộ. Không có
> Worker service, không có Edge function, không có RPC client gọi được, không có RLS. Dưới đây là index
> trỏ sang các tài liệu **có thật**.

## Nguồn sự thật (đọc theo thứ tự)

1. **[`architecture/datahub-backend-design.vi.md`](./architecture/datahub-backend-design.vi.md)** —
   hợp đồng as-built: endpoint, xác thực, hạ tầng, biến môi trường.
2. **[`architecture/datahub-backend-diagrams.md`](./architecture/datahub-backend-diagrams.md)** — sơ đồ.
3. **[`api/datahub-api-endpoints.vi.md`](./api/datahub-api-endpoints.vi.md)** — chi tiết từng endpoint.
4. **[`architecture/fullstack-database-plan.vi.md`](./architecture/fullstack-database-plan.vi.md)** —
   12 bảng, ánh xạ theo tab, quy ước migration.
5. **[`event-sourcing-lite.md`](./event-sourcing-lite.md)** — event log local + hợp đồng fingerprint.
6. **[`datahub-deployment-options.md`](./datahub-deployment-options.md)** — vì sao chọn VPS hub (số liệu
   đo từ code vẫn đúng).
7. **[`../backend/datahub/openapi/datahub-v1.yaml`](../backend/datahub/openapi/datahub-v1.yaml)** —
   hợp đồng máy đọc được.

## Tóm tắt kiến trúc as-built (1 dòng mỗi ý)

- **Ai fetch JMS:** chính process UI của AutoJMS (ULTRA), không có Windows Service riêng. Vì thế token
  JMS không rời máy mà cũng không cần Named Pipe.
- **Chống trùng:** một leader mỗi site qua bảng `site_fetch_leases` +
  `POST /api/v1/sites/{siteId}/lease/{acquire,renew,release}`. Máy tắt ⇒ lease hết hạn ⇒ máy khác đoạt.
- **Lịch fetch:** timer phía client (30 phút, 8h–23h30). Không có scheduler trong database — job nền duy
  nhất trong API là retention (`BackgroundService` đọc `retention_policies`).
- **Đường ghi:** `POST /jms/ingest` (theo lô, có `Idempotency-Key`) và `POST /jms/observations` (lẻ, khi
  user mở đơn). Handler ingest là **writer duy nhất**: append event + cập nhật projection + ghi
  `dashboard_changes` trong một transaction.
- **Đường đọc:** `GET /projections/snapshot` lần đầu, rồi `GET /changes?after={change_seq}`. Doorbell
  SignalR `/hubs/site` chỉ đánh thức client sớm hơn; mất hub thì thoái hoá về polling.
- **Fingerprint:** API tính lại khi nhận, dedupe bằng `UNIQUE (site_id, event_fingerprint)`. Client
  không được tin cậy để đặt fingerprint.
- **Bảo mật:** ba credential không thay thế nhau — access token (Render, 60 phút), license assertion
  (Render, 300 giây, chỉ dùng cho `/api/v1/devices/enroll`), device token (DataHub, 24 giờ, dùng cho mọi
  `/api/v1/sites/...` và hub). Phạm vi site lấy từ device token, client không tự khai. PostgreSQL không
  publish port ra host. Token JMS chỉ ở local.
