# AutoJMS DataHub — Thiết kế Fetcher (INDEX — nội dung cũ đã gỡ)

> **Trạng thái: v3 — thân hợp đồng cũ (v2) ĐÃ GỠ để tránh triển khai nhầm.** Bản v2 trước đây chứa
> các mô hình **đã bị thay thế** (pg_cron `next_run_at` làm scheduler, desktop gọi JMS trực tiếp,
> fallback per-scope lease, token "vault đơn", "reducer sẵn sàng cutover"). Toàn bộ đã bị loại khỏi
> tài liệu này.
>
> **Đây chỉ còn là INDEX.** Hợp đồng kiến trúc hiện hành nằm ở các tài liệu nguồn sự thật dưới đây —
> đọc và triển khai theo chúng, KHÔNG theo trí nhớ về bản v2.

## Nguồn sự thật (đọc theo thứ tự)

1. **`datahub-master-plan.md` (Draft v3 — BLOCKED BY P0)** — mục tiêu, 18 quyết định đã chốt, lộ trình
   P0→P5, blast radius, điểm cần owner duyệt.
2. **`datahub-p0-contract.md` (P0-A..P0-E)** — hợp đồng + threat model phải đóng trước P1/P2:
   - **P0-A**: identity (1 claim `site_code` top-level), RLS hardening (revoke `PUBLIC`+anon), phân
     tầng ghi (Edge boundary + private RPC), đường đọc desktop.
   - **P0-B**: event schema v2, fingerprint theo event-type, snapshot vs transition, projection owner,
     per-event ACK.
   - **P0-C**: cursor keyset + lưu độc lập + test pagination.
   - **P0-D**: site `fetch_leader` + 5 bảng token + `leader_fencing_token`/CAS + drain partition-safe.
   - **P0-E**: spike token-binding, site-wide rate limit, provisioning (Management API), key rotation,
     Windows Service lifecycle.
3. **`datahub-token-pool-plan.md` (v2)** — chi tiết token pool 5 bảng, active pointer theo candidate,
   two-strike, single-session gắn leader.
4. **`event-sourcing-lite.md`** — nền event log + defect đã biết (fingerprint/cursor) + hợp đồng đúng.
5. **`migration/hybrid-supabase-sync-plan.md`** — tài liệu lịch sử/transition (nền đã triển khai một
   phần, chưa đạt hợp đồng bảo mật/cursor mới).

## Tóm tắt kiến trúc (1 dòng mỗi ý — chi tiết ở nguồn trên)

- JMS fetch: **.NET Worker (Windows Service) trong LAN bưu cục**; mỗi bưu cục 1 Supabase project.
- Lịch fetch: **Worker sở hữu** HOT/WARM/COLD; pg_cron chỉ maintenance/stale/health (**không** scheduler).
- Token: **LOCAL trên máy sở hữu** (DPAPI `tokens.dat`), Supabase chỉ giữ **binding metadata/`token_fp`** (KHÔNG ciphertext); một **active binding** gắn `fetch_leader`.
- Desktop: **chỉ ĐỌC** (direct keyset SELECT dưới RLS) + **GHI metadata qua Edge** (session/heartbeat/
  contribute). **Token relay qua Named Pipe (LOCAL, cùng máy)** — KHÔNG qua Edge; binding do Worker/gateway publish.
- Contributor: ngoại lệ bounded (permit site-wide), không qua leader.
- Đồng bộ: transition = event append-only; **projection order `source_event_at → received_at → seq`**
  (KHÔNG `event_time` v1); snapshot detail = snapshot store CAS revision **hoặc** single-writer + FIFO
  `(leader_term, snapshot_seq)` (KHÔNG "upsert last-write");
  projection = **một writer duy nhất** (append+project cùng RPC/transaction).
- Bảo mật: RLS theo `site_code` + **entitlement mirror**, revoke EXECUTE khỏi PUBLIC; private RPC gọi
  qua **`worker_gateway`/`datahub_edge`** (DB role trong gateway/Edge, **KHÔNG** phát DB password cho máy);
  Worker LAN chỉ có `WorkerAccessToken`. Token JMS **LOCAL** (DPAPI), không lên cloud.
