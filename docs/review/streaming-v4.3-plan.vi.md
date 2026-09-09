> ⛔ **SUPERSEDED bởi [streaming-v4.4-contract.vi.md](./streaming-v4.4-contract.vi.md)** (2026-09-05). Giữ lại chỉ để tra lịch sử quyết định. Lưu ý: finding **B5** trong bản review v4.2 là SAI về code — xem mục 0.2 của v4.4.

# Kiến trúc Streaming Data Read/Write v4.3
## Pre-Implementation Contract (Brownfield / Delta trên `origin/main`)

> **Đây là bản cải thiện của v4.2**, viết lại theo đúng codebase đang chạy. Nền tảng đối chiếu và lý do từng thay đổi: xem [streaming-v4.2-review.vi.md](./streaming-v4.2-review.vi.md).
> **Nguyên tắc cốt lõi**: hợp đồng này là **delta** trên hệ thống đang hoạt động (ingest/dedupe/projection/change-feed/snapshot/retention/lease đã có), **không** phải mô tả greenfield. Mọi thay đổi phải **cộng thêm (additive)**, an toàn migration, có cờ bật/tắt và đường lùi.
> **Vai trò**: Antigravity soạn Prompt Proposal theo hợp đồng này; Claude Code thực thi từng phase; Owner duyệt **theo phase**.

---

## 0. Thay đổi lớn so với v4.2 (tóm tắt)

| # | v4.2 | v4.3 (đã sửa) | Lý do |
|---|------|---------------|-------|
| 1 | Redis là "auxiliary cache" đã có | **Redis = Phase 3, optional**; tính đúng đắn **không** phụ thuộc Redis | Redis chưa tồn tại trong code (B1) |
| 2 | Advisory lock per-waybill là bất biến serialize chính (I-5) | **Điểm serialize duy nhất = `site_change_counters ... FOR UPDATE` (per-site)**; advisory function chỉ là công cụ tùy chọn | Tránh mất change do cấp phát `change_seq` không theo commit-order (B2) |
| 3 | Không nhắc lease fencing | **Giữ nguyên fencing `X-Leader-Term`** trong hợp đồng ingest | Chống hai leader ghi sau failover (B3) |
| 4 | 14 bước cho "1 observation" | Hợp đồng ingest **bulk all-or-nothing** (đúng thực tế), định nghĩa rõ | Endpoint chính là bulk; test #21 mâu thuẫn (B4) |
| 5 | Purge chỉ xóa projection | **Purge/Reopen phát `dashboard_changes` + tăng `change_seq`** | Client phải nhận tín hiệu xóa/mở lại (B5, H5) |
| 6 | Thêm cột `source_event_at`/`received_at` | **Tái dùng** `event_occurred_at`/`ingested_at` | Tránh 4 cột cho 2 khái niệm (H1, B6) |
| 7 | Hardcode retention 7/90 ngày, worker mới | Retention **qua `retention_policies` + worker hiện có** | Tránh hai cơ chế giẫm chân (H2) |
| 8 | Fingerprint table-driven ngay (fp_version=1) | **Giữ code-driven `EventFingerprintV1`**; table-driven = phase sau, `fp_version ≥ 2` | Tránh dịch ranh giới dedupe (H4) |
| 9 | §9 snapshot như mới | Ghi nhận **đã có**; chỉ đồng bộ tên field | Tránh code lại (M1) |

---

## 1. Bất biến Hệ thống (Core Invariants v4.3)

Giữ các bất biến v4.2 còn đúng, sửa/thêm những cái sau:

| # | Invariant | Định nghĩa |
|---|-----------|-----------|
| **I-1** | Client Isolation | Client không kết nối trực tiếp Postgres; mọi đọc/ghi qua HTTPS/WSS DataHub API. *(giữ)* |
| **I-2 (sửa)** | **Correctness không phụ thuộc Redis** | Redis (nếu có) chỉ giảm tải. Redis lỗi → bypass 100% Postgres, **kết quả không đổi**. Phase 1–2 chạy **không cần Redis**. |
| **I-3** | SignalR is Advisory | Doorbell có thể trễ/trùng/mất; **Safety Poll 10s** là chốt chặn đúng đắn. *(giữ)* |
| **I-4** | PostgreSQL = Single Source of Truth | *(giữ)* |
| **I-5 (sửa)** | **Điểm serialize duy nhất per-site** | Mọi transaction mutate projection/`change_seq` của một site **serialize trên `site_change_counters` (FOR UPDATE)**. Điều này (a) cấp phát `change_seq` đơn điệu **theo commit-order**, (b) chống hai first-observation cùng tạo projection. `get_waybill_advisory_lock_key` là **công cụ tùy chọn** cho phase concurrency tương lai, **không** dùng trong luồng ingest Phase 1. |
| **I-6** | Monotonic Gap-Tolerant Cursor | `change_seq` tăng đơn điệu, cho phép gap; client chỉ quan tâm `> clientCursor`. Nhờ I-5, **không có seq nào bị commit sau khi seq lớn hơn đã hiển thị** → reader không mất change. *(làm rõ)* |
| **I-7** | Atomic Multi-Site Cursor | `cursor.json` cập nhật sau khi ghi cache thành công; atomic (`tmp → fsync → rename`), cách ly per-`siteId`. *(giữ, thêm fsync)* |
| **I-8 (mở rộng)** | **Server-side Anti-Resurrection + Change-on-Delete** | Server chặn hồi sinh đơn terminal/tombstoned. **Mọi thao tác xóa/mở-lại projection phải phát `dashboard_changes` (`delete`/`upsert`) và tăng `change_seq`.** |
| **I-9** | Disk-First Client Cache | RAM chỉ giữ dòng đang hiển thị; dashboard/detail/history nằm trên đĩa `%LOCALAPPDATA%\AutoJMS\cache\`. *(giữ — Phase 3)* |
| **I-10** | No No-Op State Mutations | Event bị stale/duplicate/terminal-locked: không tăng version, không ghi `dashboard_changes`, không tăng `change_seq`. *(giữ)* |
| **I-11** | No Sync File I/O on UI Thread | *(giữ — Phase 3)* |
| **I-12 (mới)** | **Giữ Lease Fencing** | Bulk ingest bắt buộc `X-Leader-Term`; kiểm `site_fetch_leases` bằng `clock_timestamp()`. Không phase nào được bỏ. |
| **I-13 (mới)** | **Additive & Reversible Migration** | Mỗi migration chỉ thêm (cột có default an toàn / bảng mới); index trên bảng có dữ liệu thật dùng `CREATE INDEX CONCURRENTLY` (`_notx`). Mỗi phase có cờ bật/tắt để lùi. |

---

## 2. Lộ trình theo Phase (Rollout)

> Duyệt **từng phase**. Mỗi phase kết thúc bằng gate: `dotnet build -c Release` pass + `eng/harness/verify.ps1` pass + acceptance test của phase pass.

**Phase 0 — Chốt quyết định (Owner)**
Chốt OD-1..OD-5 (xem §7). Đặc biệt: danh sách **terminal scan codes** và **fingerprint fields** phải đối chiếu payload JMS thật.

**Phase 1 — Server core (KHÔNG cần Redis)**
1. Migration 007 (event metadata), 008 (advisory fn), 009 (terminal + tombstone + index).
2. Ingest delta: thêm **tombstone check** + **anti-resurrection** vào reducer; **giữ** counter-lock + fencing + idempotency hiện có.
3. Retention 2-phase qua `retention_policies` + worker hiện có; **terminal purge phát `delete` change**.
4. Endpoint `reopen` (Option A) + `historical-replay` (admin) — **phát change** khi reopen.
5. Acceptance tests server-side (§6).
→ *Gate. Đây là phần đem lại phần lớn giá trị đúng đắn.*

**Phase 2 — (Tùy chọn) Cache Redis**
Chỉ làm nếu đo được nhu cầu. Provision Redis (VPS + `render.yaml`), circuit-breaker 250ms, Detail Pointer Pattern (§5), cân nhắc **bỏ** Dashboard Epoch (M4). Outbox `cache_invalidation_outbox` (đã tạo ở 009) mới được kích hoạt.
→ *Gate.*

**Phase 3 — Client disk-first + multi-site cursor**
`cursor.json` đa site (sửa defect cursor), cache đĩa async (I-9/I-11), hard-cut SQLite với gate "drain outbox → 0" (CC-9). Cần Owner cho phép nếu chạm Protected Files (`Main.cs`).
→ *Gate.*

---

## 3. Migrations đã sửa (đúng schema thật)

> Đánh số **007–009** (tiếp sau `006` hiện có — không đụng migration cũ). Tất cả `IF NOT EXISTS`, idempotent, ghi `schema_migrations`.

### Migration 007 — Event metadata (additive, không backfill)
```sql
-- Tái dùng event_occurred_at (= source event time) và ingested_at (= received time).
-- KHÔNG thêm cột trùng khái niệm. Mọi cột mới có default an toàn nên INSERT hiện tại
-- của IngestRepository.InsertEventAsync tiếp tục chạy không đổi.
ALTER TABLE waybill_scan_events
    ADD COLUMN IF NOT EXISTS event_kind            text,
    ADD COLUMN IF NOT EXISTS reducer_version       smallint NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS normalizer_version    smallint NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS source_schema_version text     NOT NULL DEFAULT 'v2';

INSERT INTO schema_migrations (version) VALUES ('007_event_metadata')
ON CONFLICT (version) DO NOTHING;
```
*Ghi chú: nếu về sau muốn đổi tên cho khớp thuật ngữ (`event_occurred_at → source_event_at`), làm bằng migration rename riêng + compat view, KHÔNG duy trì hai cột song song.*

### Migration 008 — Hàm advisory 64-bit (đúng tên tham số)
```sql
CREATE OR REPLACE FUNCTION get_waybill_advisory_lock_key(p_site_id uuid, p_canonical_waybill text)
RETURNS bigint
LANGUAGE sql IMMUTABLE STRICT AS $$
    SELECT ('x' || substr(md5(p_site_id::text || ':' || p_canonical_waybill), 1, 16))::bit(64)::bigint;
$$;

INSERT INTO schema_migrations (version) VALUES ('008_advisory_lock_fn')
ON CONFLICT (version) DO NOTHING;
```
*Dùng `p_canonical_waybill` (đúng) — sửa lỗi định danh `p_canonicalWaybill` ở §2.1 của v4.2. Hàm này phục vụ retention/reopen ở phase concurrency tương lai; luồng ingest Phase 1 **không** gọi nó (I-5).*

### Migration 009 — Terminal marker + Tombstone + Outbox (optional) + Index
```sql
ALTER TABLE waybill_projections
    ADD COLUMN IF NOT EXISTS is_terminal         boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS terminal_at         timestamptz,
    ADD COLUMN IF NOT EXISTS terminal_state_code integer,
    ADD COLUMN IF NOT EXISTS last_change_seq     bigint;

CREATE TABLE IF NOT EXISTS waybill_tombstones (
    site_id             uuid NOT NULL REFERENCES sites(id),
    waybill_no          text NOT NULL,
    terminal_state_code integer NOT NULL,
    terminal_at         timestamptz NOT NULL,
    purged_at           timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (site_id, waybill_no)
);

-- Outbox chỉ dùng ở Phase 2 (Redis). Tạo sẵn schema là additive, không bật ở Phase 1.
CREATE TABLE IF NOT EXISTS cache_invalidation_outbox (
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id         uuid NOT NULL REFERENCES sites(id),
    cache_type      text NOT NULL,           -- 'PROJECTION_POINTER' | 'DASHBOARD_EPOCH'
    waybill_no      text NULL,
    target_version  bigint NOT NULL,
    attempt_count   integer NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    processed_at    timestamptz NULL,
    last_error      text NULL,
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_cache_invalidation_unprocessed
    ON cache_invalidation_outbox (next_attempt_at) WHERE processed_at IS NULL;

INSERT INTO schema_migrations (version) VALUES ('009_terminal_tombstone_outbox')
ON CONFLICT (version) DO NOTHING;
```
**Index cho terminal retention — chạy TÁCH (`_notx`) với `CONCURRENTLY` trên DB có dữ liệu thật** (theo đúng cảnh báo ở `006`):
```sql
-- File 009_terminal_indexes_notx.sql (không transaction)
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_waybill_projections_terminal_retention
    ON waybill_projections (site_id, terminal_at) WHERE is_terminal = true;
```
*Terminal retention days (mặc định 90) đặt trong `retention_policies` (xem §Retention), **không** hardcode cột per-row.*

---

## 4. Hợp đồng Ingest v4.3 (delta trên `IngestRepository.IngestAsync`)

> Đây là **bổ sung** vào luồng hiện có, **không** viết lại. Các bước có dấu **[MỚI]** là phần cần thêm; các bước **[GIỮ]** phải bảo toàn.

**Ngoài transaction (tiền kiểm):**
1. **[GIỮ]** Auth device token (siteId match, capability `ReadWriteSiteData`).
2. **[GIỮ]** Chuẩn hóa `CanonicalWaybillNo = waybillNo.Trim().ToUpperInvariant()` cho từng item.
3. **[MỚI]** Bắt `received_at = clock_timestamp()` **tại ingress** (gán ở app trước `BEGIN`), lưu immutable — không trôi theo thời gian chờ lock (§L2 review).
4. **[MỚI]** Timestamp guardrail mỗi item: `source_event_at` là UTC; `≤ received_at + 5 phút`; `≥ received_at − 90 ngày`. Vi phạm → item lỗi cứng.
5. **[MỚI]** Resolve & pin policy versions (`reducer_version`, `fingerprint_version`, `normalizer_version`) — nguồn: `JmsEventPolicyCatalog`/bảng policy (định nghĩa nguồn "active version" nếu bật per-request; xem M7).
6. **[GIỮ]** `bodyHash = SHA256(toàn bộ request)`.

**Trong transaction (Postgres):**
7. **[GIỮ]** `BEGIN`; `SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='30s'`.
8. **[GIỮ/I-12]** Nếu bulk: kiểm fence (`X-Leader-Term`) — trước idempotency, khi replay, và **trước commit**.
9. **[GIỮ]** Idempotency: xóa record hết hạn → đọc `FOR UPDATE` → (trùng key + trùng hash) replay; (trùng key + khác hash) **409 IDEMPOTENCY_KEY_REUSED**; (đang xử lý) **409 IDEMPOTENCY_IN_PROGRESS**; nếu chưa có → reserve (`status_code=0`).
10. **[GIỮ/I-5]** `SELECT change_seq FROM site_change_counters WHERE site_id=@site FOR UPDATE` — **điểm serialize per-site + cấp phát seq**.
11. **Vòng lặp từng item (bulk):**
    a. **[MỚI/I-8]** Tombstone check: `SELECT 1 FROM waybill_tombstones WHERE site_id=@site AND waybill_no=@canonical`. Nếu có → **skip mutation**, đếm `terminal_locked++` (KHÔNG rollback cả batch — hành vi giống duplicate).
    b. **[GIỮ]** `INSERT INTO waybill_scan_events (... đủ cột như code hiện tại ...) ON CONFLICT (site_id,event_fingerprint) DO NOTHING RETURNING id`. Null → `duplicates++`, continue.
    c. **[GIỮ]** Đọc projection `FOR UPDATE`; **[MỚI]** nếu `is_terminal = true` và event không phải `admin_reopen` → **anti-resurrection**: skip mutation, `terminal_locked++` (event vẫn lưu phục vụ audit).
    d. **[GIỮ]** `reducer.Reduce(current, event, policies)`; gom thay đổi theo waybill.
12. **[GIỮ]** Với mỗi waybill đổi: `sequence = checked(sequence+1)`; upsert projection; `INSERT dashboard_changes(...,'upsert', body)`; cập nhật `last_change_seq`. **[MỚI Phase 2]** nếu bật Redis: `INSERT cache_invalidation_outbox`.
13. **[GIỮ]** `UPDATE site_change_counters SET change_seq=@sequence`; ghi idempotency response; audit; **[I-12]** fence re-check; `COMMIT`.

**Hậu commit (best-effort):**
14. **[GIỮ]** Bắn SignalR doorbell `{ siteId, changeSeq }`. **[MỚI Phase 2]** kích outbox worker.

**Response schema (mở rộng nhẹ):** `{ accepted, duplicates, terminalLocked, changed, firstSeq, lastSeq, replayed }`.

**Quyết định ranh giới transaction (chốt B4):** giữ **all-or-nothing** — lỗi cứng (scanTime sai, guardrail) → rollback cả batch, `422`; các skip nghiệp vụ (duplicate/terminal-locked) không rollback. → **Sửa test #21** thành "1 item lỗi schema → 422, không commit phần nào" (không phải partial 99/100).

---

## 5. Retention 2-Phase (qua `retention_policies` + worker hiện có)

**Phase 1 — Event Purge**: đã có (`RetentionHostedService`, clock `event_occurred_at`, mặc định 60 ngày). **Không tạo worker mới, không hardcode 7 ngày.** Nếu Owner muốn 7 ngày → sửa **giá trị trong `retention_policies`** (OD-2).

**Phase 2 — Terminal Projection Purge (MỚI)**: thêm task cho worker, **mỗi waybill một transaction ngắn**, **phát `delete` change** (sửa B5):
```sql
BEGIN;
  -- Serialize với ingest cùng site: đây là nơi cấp phát change_seq (I-5).
  PERFORM 1 FROM site_change_counters WHERE site_id = @siteId FOR UPDATE;

  IF EXISTS (
      SELECT 1 FROM waybill_projections
       WHERE site_id=@siteId AND waybill_no=@waybill
         AND is_terminal = true
         AND terminal_at <= now() - @terminal_retention   -- từ retention_policies (mặc định 90d)
  ) THEN
      INSERT INTO waybill_tombstones (site_id, waybill_no, terminal_state_code, terminal_at)
      SELECT site_id, waybill_no, terminal_state_code, terminal_at
        FROM waybill_projections WHERE site_id=@siteId AND waybill_no=@waybill
      ON CONFLICT (site_id, waybill_no) DO NOTHING;

      -- Cấp phát seq và PHÁT delete để client bỏ dòng.
      UPDATE site_change_counters SET change_seq = change_seq + 1
       WHERE site_id=@siteId RETURNING change_seq \gset
      INSERT INTO dashboard_changes (site_id, change_seq, entity_type, entity_key, operation, body)
      VALUES (@siteId, :change_seq, 'waybill_projection', @waybill, 'delete', '{}'::jsonb);

      DELETE FROM waybill_projections WHERE site_id=@siteId AND waybill_no=@waybill;
  END IF;
COMMIT;
```
*Đơn Active hoặc terminal chưa đủ hạn: không đụng. Nhờ counter-lock, purge không cần advisory lock. `dashboard_changes(delete)` sẽ tự bị dọn theo retention 14 ngày sau khi client đã kịp đồng bộ.*

---

## 6. Admin Reopen & Historical Replay

**Reopen (Option A)** — `POST /api/v1/sites/{siteId}/waybills/{waybillNo}/reopen`, quyền `datahub_admin`, có `reason` + `adminUserId`:
- Chỉ khi projection còn sống (< hạn 90 ngày). Đã purge → **410 GONE** (`PROJECTION_ALREADY_PURGED`).
- Trong transaction (serialize qua counter-lock): xóa tombstone nếu có; `is_terminal=false`; sinh event `admin_reopen`; **[MỚI/I-8]** cấp seq + `INSERT dashboard_changes('upsert')` để client thấy đơn mở lại (sửa H5).

**Historical Replay** — `POST /api/v1/admin/sites/{siteId}/historical-replay` (token admin): log > 90 ngày bị chặn ở endpoint ingest thường (guardrail). Nếu waybill có tombstone → replay **không** tạo projection sống, trừ khi `force_rebuild_tombstoned=true` kèm lý do audit.

---

## 7. Redis (Phase 2 — tùy chọn)

Chỉ triển khai khi đo được nhu cầu tải. Yêu cầu **bổ sung** so với v4.2:
- **Provision thật**: thêm Redis service vào `render.yaml`, cấu hình `maxmemory` + `maxmemory-policy=allkeys-lru`, TLS, giám sát.
- **Detail Pointer Pattern** (§5 v4.2) giữ nguyên logic Lua `max(current,target)`, **nhưng** đặt **TTL dài cho pointer key** (`projver:*`) để tránh tăng bộ nhớ vô hạn (M3).
- **Dashboard Epoch**: **cân nhắc bỏ** — ở site bận `epoch` đổi mỗi `change_seq` khiến hit-ratio ≈ 0 (M4). Nếu vẫn làm, đo hit-ratio trước/sau.
- **Circuit breaker 250ms** + bypass Postgres (I-2). Outbox `cache_invalidation_outbox` (đã có schema ở 009) mới được worker xử lý ở phase này.

---

## 8. Client (Phase 3 — tùy chọn, tách riêng)

- **`cursor.json` đa site** tại `%LOCALAPPDATA%\AutoJMS\cache\` — thay cursor `MAX(remote_seq)` (sửa defect kẹt cursor). Cập nhật per-site: `tmp → Flush(true)/fsync → File.Move(overwrite:true)` **cùng volume NTFS** (L1).
- **Disk-first cache** (I-9) + async I/O (I-11): quota `details/` 20MB/100 files LRU, `history/` 10MB LRU, `dashboard/` xóa > 24h. Inherit ACL Windows user; DPAPI theo OD-4.
- **Hard-cut SQLite** (CC-9): gate "freeze write → drain outbox về 0 → mới ngắt SQLite". Cần Owner cho phép nếu chạm `Main.cs`/`Main.Designer.cs` (Protected Files).

---

## 9. Owner Decisions (đề xuất — chờ Owner chốt ở Phase 0)

| # | Hạng mục | Đề xuất v4.3 |
|---|----------|--------------|
| **OD-1** | Terminal scan codes | **Đối chiếu JMS thật trước khi seed.** Hiện `002` chỉ phân loại 98/110. Lưu terminal codes dạng **policy versioned trong bảng**, không hardcode reducer. |
| **OD-2** | Terminal retention days | Giữ event **60 ngày** (như hiện tại) + terminal projection **90 ngày**, **cả hai qua `retention_policies`**. Nếu muốn event 7 ngày → chỉ đổi giá trị policy. |
| **OD-3** | Reopen sau purge | **Option A** + phải có đường Historical Rebuild cho đơn đã purge (410 không là ngõ cụt). |
| **OD-4** | DPAPI cho disk cache | Có PII (tên/địa chỉ/SĐT) → **bật DPAPI per-user**; chỉ mã đơn + trạng thái → ACL mặc định đủ. |
| **OD-5** | Fingerprint fields | **Giữ code-driven `EventFingerprintV1`** tới khi có payload JMS thật; sau đó cutover `fingerprint_version = 2` (không tái dùng số 1). |

---

## 10. Acceptance Tests (cập nhật)

Giữ bộ test v4.2 **trừ #21 (sửa)** và **bổ sung 6 test** khớp các blocker/high:

- **#21 (sửa)**: Bulk có 1 item lỗi schema → **422, không commit phần nào** (all-or-nothing). *(Nếu Owner chọn partial-success thì đổi contract + response schema trước.)*
- **T-A Purge-emits-delete**: purge đơn terminal → client nhận `dashboard_changes(delete)` và xóa dòng khỏi dashboard.
- **T-B Reopen-emits-change**: reopen → client nhận `upsert`, đơn xuất hiện lại.
- **T-C Fence-under-ingest**: leader mất lease giữa batch → `409 LEADER_FENCED`, không commit.
- **T-D change_seq monotonic under concurrency**: nhiều ingest song song cùng site → reader không bao giờ nhảy qua một `change_seq` chưa commit (không mất change).
- **T-E Fingerprint-version cutover**: đổi công thức fingerprint (v1→v2) không phá dedupe của event `v1` cũ.
- **T-F Retention-not-conflicting**: chỉ một cơ chế retention chạy; giá trị đến từ `retention_policies` (không có worker hardcode song song).

---

## 11. Bảng "Claude Code MUST NOT Interpret Differently" (khóa cứng — bản v4.3)

| # | Quy định | Tuyệt đối |
|---|----------|-----------|
| **CC-1** | Advisory fn | Nếu dùng, bắt buộc `get_waybill_advisory_lock_key` (tham số `p_canonical_waybill`, 64-bit). Không `hashtext` 32-bit. |
| **CC-2 (sửa)** | Serialize | **Điểm serialize per-site = `site_change_counters FOR UPDATE`.** Không thay bằng advisory lock per-waybill trong ingest Phase 1. |
| **CC-3 (sửa)** | Fencing | **Giữ nguyên** kiểm `X-Leader-Term` cho bulk ingest. Không được lược bỏ. |
| **CC-4 (sửa)** | Timestamps | **Tái dùng** `event_occurred_at`/`ingested_at`. Không thêm cột `source_event_at`/`received_at` trùng khái niệm. |
| **CC-5 (sửa)** | Change-on-Delete | Purge/Reopen **phải** phát `dashboard_changes` + tăng `change_seq` trong cùng transaction. |
| **CC-6** | Snapshot | Giữ `RepeatableRead` + `LIMIT maxRows+1` **đã có** (`ChangeRepository`); chỉ đồng bộ tên field, **không** code lại. |
| **CC-7 (sửa)** | Retention | Terminal purge qua `retention_policies` + worker hiện có; không hardcode 7/90 ngày trong code. |
| **CC-8 (sửa)** | Redis | Không đưa Redis vào đường tính đúng đắn (Phase 1–2). Fingerprint giữ code-driven tới khi Owner chốt OD-5. |
| **CC-9** | Client cache | `%LOCALAPPDATA%`, async I/O, drain SQLite outbox về 0 trước hard-cut. (Phase 3) |
| **CC-10 (mới)** | Migration | Additive + `CONCURRENTLY (_notx)` cho index trên bảng có dữ liệu; mỗi phase có cờ bật/tắt để lùi. |

---

*Hết. Đề nghị Owner duyệt Phase 0 (chốt OD) rồi Phase 1 (server core) trước; Phase 2 (Redis) và Phase 3 (client) duyệt riêng khi đủ điều kiện.*
