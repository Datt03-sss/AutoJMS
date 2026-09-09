> ⛔ **SUPERSEDED bởi [streaming-v4.5-contract.vi.md](./streaming-v4.5-contract.vi.md)** (2026-09-05). Giữ để tra lịch sử. v4.5 sửa 2 lỗi định danh của bản này: `ReadWriteSiteData` không tồn tại (đúng là `WriteSiteData`), và admin không phải một DeviceCapability.

# Streaming v4.4 — Production Implementation Contract

> **THAY THẾ** `implementation_plan.md` (v4.2), `streaming-v4.2-review.vi.md`, `streaming-v4.3-plan.vi.md`. Hai file kia giữ lại chỉ để tra lịch sử quyết định.
> **Ngày**: 2026-09-05 · **Trạng thái**: sẵn sàng duyệt **P0**.
> **Triết lý (chốt theo phản biện)**: *Server correctness → Client remote/disk → Streaming → Retention → Redis (cuối cùng, nếu thật sự cần).* Không thêm cơ chế mới nếu code hiện tại đã giải quyết.
> **Nguyên tắc số 1**: Tài liệu này **ngắn hơn** v4.3 có chủ đích. Mọi thứ đã đúng trong code đều bị **xoá khỏi backlog**, không mô tả lại.

---

## 0. Phân xử 27 điểm phản biện (adjudication)

### 0.1 Đã đúng sẵn trong code — **XOÁ khỏi backlog** (không cần làm gì)

| Điểm | Kết luận đối chiếu code |
|---|---|
| **#7** `fingerprint_version` chưa tồn tại? | **Đã tồn tại.** `001_core.sql:58` — `fingerprint_version smallint NOT NULL DEFAULT 1` (+ CHECK `>0` dòng 71). Câu `ALTER COLUMN ... SET DEFAULT 1` bạn trích là của **v4.2**, không có trong v4.3. Không cần migration. |
| **#8** `jms_event_policies` PK có phải composite? | **Đã composite.** `001_core.sql:129` — `PRIMARY KEY (reducer_version, scan_type_code)`. `INSERT reducer_version=2` **không** conflict. Không cần `DROP CONSTRAINT`. |
| **#6** (tiền đề) `change_seq` dùng `NEXTVAL`? | **Không có sequence nào.** `grep nextval|CREATE SEQUENCE` → rỗng. `change_seq` là cột trong `site_change_counters`, cấp phát dưới `FOR UPDATE`. *(Nhưng kết luận I-14 của bạn vẫn giữ — xem 0.2.)* |
| **#22** delete-change bị purge quá sớm? | **Đã có retention riêng cho tombstone.** `RetentionRepository.RunOnceAsync(batchSize, tombstoneRetention)`; `DeleteChangesAsync` nhận `tombstoneRetention` riêng (dòng 263, 305, 369), kẹp bởi `Min/MaximumTombstoneRetentionDays`. `pruned_through_seq` cập nhật bằng `GREATEST(...)` (dòng 388-391). |
| **#24** pool hardcode `14/20`? | **Không hardcode.** `PostgresDataSource:45` — `MaxPoolSize = Math.Clamp(options.MaximumPoolSize, 1, 100)`, `MinPoolSize = 0`, statement/idle timeout cấu hình được. Con số `14/20` không có trong v4.3. |
| **#10** PATCH metadata | **Không có trong v4.3.** v4.3 không định nghĩa `PATCH /waybills/{no}`. Nếu sau này thêm → áp dụng nguyên tắc của bạn (xem I-14/§7). |

### 0.2 ⚠️ Đính chính lỗi của **chính mình** trong review v4.2

**Finding B5 ("Terminal Purge không phát `delete` change") là SAI khi nói về code.** Nó đúng với **SQL mà v4.2 đề xuất**, nhưng code hiện tại **đã làm đúng và làm tốt hơn cả v4.3 của mình**:

`RetentionRepository.DeleteProjectionsAsync` (dòng ~135-258) thực hiện trong **một** data-modifying CTE:
```text
SELECT ... FROM site_change_counters WHERE site_id = ANY(...) ORDER BY site_id FOR UPDATE
  → cấp phát change_seq cho từng site
  → INSERT dashboard_changes(..., 'delete', jsonb_build_object('waybill_no', ...))
  → UPDATE site_change_counters SET change_seq = max(allocated)
  → DELETE waybill_projections USING inserted   -- chỉ xoá nơi đã có delete-change
```
Kèm comment nêu đúng lý do lock-ordering: *"Ingest locks the site counter before it touches either the projection or the change feed, so taking that lock first here is what keeps the two paths from acquiring projection and change rows in opposite orders."* Và: *"A site with no counter row cannot be given a sequence, so its projections stay. Deleting them would be the silent disappearance this whole part exists to prevent."*

→ **Hệ quả lớn cho kế hoạch**: purge projection + phát delete-change + kỷ luật counter-lock **đã tồn tại**. Việc cần làm **không phải viết purge mới**, mà chỉ **đổi vị từ chọn ứng viên** (thêm `is_terminal`) và **ghi thêm dòng tombstone**. Khối lượng P-Retention giảm mạnh.

**Trạng thái hiện tại của purge**: đang **ngủ** — `DeleteProjectionsAsync` có probe, nếu không có dòng `retention_policies` cho `waybill_projections` thì `return (0,0)`. `003_seed_retention` **không** seed dòng đó (chỉ `waybill_scan_events`, `dashboard_changes`, `audit_logs`). Vị từ hiện tại là `p.updated_at < now() - delete_after` (**theo bất hoạt, không theo terminal**).

→ **Cảnh báo vận hành**: nếu ai đó thêm policy `waybill_projections` **trước khi** có `is_terminal` + tombstone, hệ thống sẽ xoá projection theo *bất hoạt* và **đơn có thể bị hồi sinh** bởi event đến muộn (chưa có tombstone chặn). **Không được seed policy này cho tới hết P-Retention.**

### 0.3 Chấp nhận đưa vào v4.4
`#1` (ghi rõ trade-off serialize) · `#2` (**xung đột horizon — điểm mạnh nhất**) · `#3` (dedupe horizon) · `#5` (test transaction thật) · `#9` (reopen bump version) · `#11` (audit nhất quán) · `#12` (bỏ advisory fn khỏi P1) · `#13`,`#14` (Redis cuối, bỏ dashboard epoch) · `#15`,`#16`,`#17`,`#18` (client cache) · `#19`,`#20`,`#21` (authz/audit) · `#23` (test throughput) · `#25` (ngữ nghĩa response) · `#26` (ghi chú bodyHash) · `#27` (bỏ "optional" mơ hồ) · **I-14**.

### 0.4 Tinh chỉnh (không chấp nhận nguyên văn)

- **#3 — phạm vi thiệt hại nhỏ hơn bạn mô tả.** Sau khi event bị purge, event cũ gửi lại **được accept** (mất bộ nhớ dedupe) → đúng. Nhưng nó **thường không mutate projection**: `ProjectionReducer.IsWinner` so `EventOccurredAt` với slot hiện tại, event ngày 20 thua slot ngày 55 → stale, không đổi version (I-10). Rủi ro thật là: (a) rác audit + `accepted` sai ngữ nghĩa, (b) **nguy hiểm thật sự** khi projection đã bị purge → event cũ **tạo lại projection sống** (hồi sinh). Vì vậy invariant vẫn phải khoá — nhưng lý do chính là **anti-resurrection**, không phải "mutate ngược".
- **#6 → I-14 giữ, nhưng hạ mức**: hiện có **2 writer** của `dashboard_changes` (`IngestRepository:544`, `RetentionRepository:232`) và **cả hai đều đã đúng kỷ luật counter-lock**. Nên I-14 là **refactor chống trôi trong tương lai** (High hygiene), **không phải sửa bug** (không phải Blocker).
- **#27 → sắp xếp lại**: retention của bạn ở P6 (sau client) là **đúng cho phần *huỷ dữ liệu***, nhưng phần ***bảo vệ*** (`is_terminal`, tombstone, anti-resurrection) phải làm **sớm ở P1** vì nó ngăn hồi sinh. Tách "bảo vệ" khỏi "huỷ".

---

## 1. Hợp đồng Dữ liệu: Ba Horizon (thay đổi quan trọng nhất của v4.4)

Khoá bất đẳng thức sau, đây là gốc của mâu thuẫn 90/60:

```text
NORMAL_INGEST_HORIZON  <  EVENT_DEDUPE_RETENTION  <<  TOMBSTONE_RETENTION
        45 ngày                  60 ngày                   ≥ 2 năm
```

| Tham số | Giá trị chốt | Nguồn cấu hình | Ghi chú |
|---|---|---|---|
| Normal ingest horizon | **45 ngày** | app config (guardrail) | `source_event_at ≥ received_at − 45d`; cũ hơn → **reject**, đi admin workflow |
| Future skew | **5 phút** | app config | giữ như v4.2 |
| Event / dedupe retention | **60 ngày** | `retention_policies('waybill_scan_events')` | **giữ nguyên seed hiện có — không đổi gì** |
| Terminal projection purge | **90 ngày** | `retention_policies('waybill_projections')` | **chỉ seed sau khi xong P-Retention** |
| `dashboard_changes` (upsert) | 14 ngày | `retention_policies` | đã có |
| `dashboard_changes` (delete) | `tombstoneRetention` | `DataHubRuntimeOptions` | đã có, tách riêng |
| `waybill_tombstones` | **≥ 2 năm** | policy | phải sống lâu hơn **mọi** đường replay |

**Bất biến**: `NORMAL_INGEST_HORIZON < EVENT_DEDUPE_RETENTION`. Server phải **fail-fast khi khởi động** nếu cấu hình vi phạm — không để lệch âm thầm.

---

## 2. Invariants (bản cuối)

Giữ I-1, I-3, I-4, I-6, I-7, I-9, I-10, I-11 như v4.3. Sửa/thêm:

| # | Invariant |
|---|---|
| **I-2** | **Correctness không phụ thuộc Redis.** Baseline production **không có Redis**. |
| **I-5** | **Điểm serialize duy nhất per-site = `site_change_counters FOR UPDATE`.** Mọi path chạm projection **hoặc** change feed phải lấy khoá này **trước** (đúng như `IngestRepository` và `RetentionRepository` đang làm). Đây là **trade-off có chủ ý** — xem §3. |
| **I-8** | **Anti-Resurrection + Change-on-Delete.** Không projection nào biến mất/hồi sinh mà client không được báo. Xoá → `dashboard_changes('delete')`; mở lại → `('upsert')`; cả hai bump `change_seq` **và** `projection.version`. |
| **I-12** | **Giữ Lease Fencing** (`X-Leader-Term`) cho bulk ingest. |
| **I-13** | **Additive & Reversible Migration**; index trên bảng có dữ liệu dùng `CONCURRENTLY (_notx)`. |
| **I-14** (mới) | **Single Change Writer.** Mọi dòng `dashboard_changes` phải sinh qua **một** `IChangeSequenceAllocator` duy nhất. Không endpoint/repository/retention/admin nào được tự viết logic cấp seq. *(Hiện 2 writer đều đúng — refactor để chống trôi.)* |
| **I-15** (mới) | **Client merge phải idempotent & version-guarded.** Áp cùng một batch nhiều lần **phải** cho cùng kết quả; `if incoming.version <= cached.version → ignore`. |
| **I-16** (mới) | **Audit identity từ token.** `operatorId`/actor luôn lấy từ principal đã xác thực, **không bao giờ** từ body/query client. |
| **I-17** (mới) | **Horizon ladder** (§1) — fail-fast khi cấu hình vi phạm. |

---

## 3. Kiến trúc baseline (không Redis)

```text
                 INTERNET
                    │  HTTPS / WSS
                    ▼
          ┌────────────────────┐
          │      VPS-API       │  REST · SignalR(doorbell) · Ingest
          │                    │  Reducer · Health/Metrics
          └─────────┬──────────┘
                    │ WireGuard
                    ▼
          ┌────────────────────┐
          │      VPS-DB        │  PostgreSQL
          │  Events · Projections · Change Feed
          │  Retention · Tombstone · Idempotency
          └────────────────────┘
Client: UI RAM = chỉ rows đang hiển thị │ %LOCALAPPDATA% = Disk Cache
```

**Trade-off được tuyên bố (theo #1)**:
> *Phase 1 deliberately uses per-site serialization for correctness and simplicity.*
> Toàn bộ writer của một site xếp hàng trên `site_change_counters`. Với 2–5 client/site đây là đánh đổi chấp nhận được và **không** được quảng cáo là kiến trúc scale vô hạn.
> Giới hạn khoá: `max bulk = 200`, `statement_timeout = 30s`, `lock_timeout = 5s`.
> Chỉ khi P8 đo được nghẽn mới cân nhắc `per-waybill lock + commit-safe change watermark`. **Không tối ưu sớm.**

---

## 4. ĐÃ CÓ — đừng code lại (Brownfield Freeze)

Danh sách này quan trọng ngang backlog: nó chặn việc viết lại thứ đã đúng.

| Hạng mục | Vị trí | Trạng thái |
|---|---|---|
| Ingest 1-txn, bulk ≤200, all-or-nothing | `IngestRepository.IngestAsync` | ✅ |
| Idempotency đầy đủ (reserve `status_code=0`, replay, `KEY_REUSED`, `IN_PROGRESS`) | `IngestRepository` | ✅ |
| Lease fencing 3 chốt | `CheckFenceAsync` | ✅ |
| Dedupe `UNIQUE(site_id,event_fingerprint)` + `ON CONFLICT DO NOTHING RETURNING id` | `001_core` + `InsertEventAsync` | ✅ |
| Cấp phát `change_seq` dưới `FOR UPDATE` | `ReadCounterAsync/UpdateCounterAsync` | ✅ |
| Snapshot `RepeatableRead` + `LIMIT maxRows+1` + `truncated` + `asOfChangeSeq` | `ChangeRepository.ReadSnapshotAsync` | ✅ |
| Delta feed + `pruned_through_seq` + `RESYNC_REQUIRED` | `ChangeRepository` + `ChangeCursorWindow` | ✅ |
| **Projection purge + delete-change + counter-lock (CTE nguyên tử)** | `RetentionRepository.DeleteProjectionsAsync` | ✅ *(đang ngủ)* |
| Retention cấu hình được + tombstone retention riêng | `retention_policies` + `RunOnceAsync` | ✅ |
| `fingerprint_version`, PK policy composite | `001_core:58,129` | ✅ |
| Client xử lý `operation='delete'`, last-op-wins trong trang | `DataHubClient` (`DeleteOperation`) | ✅ |
| Authz theo token vs siteId URL | `TenantAuthorizationEvaluator` | ✅ |
| Pool/timeout cấu hình được | `PostgresDataSource` | ✅ |

---

## 5. Delta thật sự cần làm

**Server**
1. `is_terminal`, `terminal_at`, `terminal_state_code`, `last_change_seq` trên `waybill_projections`; bảng `waybill_tombstones`.
2. **Anti-resurrection trong ingest**: check tombstone + terminal → skip mutation, đếm `terminalLocked`.
3. **Đổi vị từ purge**: `DeleteProjectionsAsync` dùng `is_terminal = true AND terminal_at < now() - delete_after` (clock `terminal_at`) thay cho `updated_at`; **ghi thêm dòng `waybill_tombstones` trong cùng CTE**.
4. **Horizon guardrail 45 ngày** + fail-fast cấu hình (I-17).
5. **`IChangeSequenceAllocator`** — gom 2 writer hiện có (I-14).
6. **Reopen** (Option A): bump `version`, phát `upsert`, xoá tombstone, audit từ principal.
7. **Historical replay** = workflow admin riêng (§7), không phải boolean flag.

**Client** — xem §8.

**KHÔNG làm**: Redis (P7 nếu cần) · dashboard epoch (**bỏ hẳn**) · advisory lock function (**bỏ khỏi P1**, #12) · viết lại snapshot/purge/idempotency.

---

## 6. Migrations (bản cuối)

### 007 — Event metadata (additive, không backfill)
```sql
-- Tái dùng event_occurred_at (= source event time) và ingested_at (= received time).
-- KHÔNG thêm cột trùng khái niệm. KHÔNG đụng fingerprint_version (đã có DEFAULT 1 ở 001_core:58).
ALTER TABLE waybill_scan_events
    ADD COLUMN IF NOT EXISTS event_kind            text,
    ADD COLUMN IF NOT EXISTS reducer_version       smallint NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS normalizer_version    smallint NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS source_schema_version text     NOT NULL DEFAULT 'v2';

INSERT INTO schema_migrations (version) VALUES ('007_event_metadata')
ON CONFLICT (version) DO NOTHING;
```

### 008 — Terminal marker + Tombstone
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

INSERT INTO schema_migrations (version) VALUES ('008_terminal_tombstone')
ON CONFLICT (version) DO NOTHING;
```
*(Hàm `get_waybill_advisory_lock_key` **bị loại khỏi P1** — dead code, theo #12. Chỉ thêm khi vào phase concurrency.)*

### 009 — Index (chạy TÁCH, `_notx`, `CONCURRENTLY`)
```sql
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_waybill_projections_terminal_retention
    ON waybill_projections (site_id, terminal_at) WHERE is_terminal = true;
```

**Cổng migration (P0)**: trước khi chạy, verify bằng `information_schema.columns` rằng 4 cột ở 007 và 4 cột ở 008 **chưa** tồn tại; nếu đã có thì bỏ qua, không nhân đôi (nguyên tắc brownfield của bạn — giữ, chỉ bỏ 2 mục đã chứng minh là dư).

---

## 7. Phân pha (thay cho "optional" mơ hồ, theo #27)

**BẮT BUỘC**

| Phase | Nội dung | Gate |
|---|---|---|
| **P0** Contract & Brownfield Freeze | Chốt OD (§10) + verify schema thật + khoá danh sách §4 | Owner ký |
| **P1** Server Correctness (bảo vệ) | Migration 007/008/009 · anti-resurrection · horizon guardrail 45d + fail-fast · `IChangeSequenceAllocator` · reopen | Build Release + test P1 |
| **P2** Server Read Contract | `waybills`/`detail`/`history` + ETag. **Dùng lại** `changes`/`snapshot` đã có | Build + test |
| **P3** Client Remote Read + Disk Cache | §8. Đo UI responsiveness, disk latency, working set | Đo được, không regress UI |
| **P4** Observation Write Cutover | JMS → Normalizer → POST → server reducer. Shadow mode. Chỉ tắt LocalDb khi `outbox = 0` | outbox = 0 |
| **P5** SignalR | doorbell wake-up, **không payload**; Safety Poll 10s là chốt đúng đắn | test mất kết nối |
| **P6** Retention (huỷ dữ liệu) | Đổi vị từ purge sang `is_terminal`/`terminal_at` + ghi tombstone; **chỉ khi đó mới seed policy `waybill_projections` (90d)** | test retention × ingest/reopen/replay |
| **P7** Load / Chaos / Production Gate | 2/5/10 client · 50 request đồng thời · SignalR off · API/DB restart · client crash · disk full · mất mạng | đạt SLO |

**TUỲ CHỌN (chỉ khi P7 chứng minh cần)**

| R1 | Redis **Detail Pointer** cache (chỉ detail) — nếu PG CPU / query p95 cho thấy cần |
| R2 | ~~Dashboard epoch cache~~ — **bỏ hẳn** (#14): dashboard đổi liên tục, nhiều filter/pagination → hit-ratio ≈ 0, độ phức tạp cao |

*Lưu ý thứ tự*: purge (P6) đặt **sau** client cutover (P3/P4) vì nó huỷ dữ liệu — dù client hiện **đã** xử lý `delete`, vẫn nên xác nhận đường đi thực tế trước khi bật.

---

## 8. Hợp đồng Client Cache

- **Khoá cache phải scope theo site** (#18 — lỗi thật): `details/{siteId}/{canonicalWaybillNo}.json`, `history/{siteId}/...`, `dashboard/{siteId}/{queryHash}/...`. Hai site cùng mã vận đơn **không được** đè nhau. Khi đổi site: phân lập cả dashboard page cache, detail cache, và **SignalR subscription**, không chỉ cursor.
- **Merge idempotent + version-guarded** (I-15, #16/#17): crash giữa "ghi page" và "ghi cursor" sẽ khiến batch cũ được áp lại → bắt buộc `if incoming.version <= cached.version → ignore`. Safety Poll + SignalR vốn có thể đưa cùng một change nhiều lần.
- **Page cache là thứ dùng rồi bỏ** (#15): tối đa **3–5 trang gần nhất / query**, TTL **5–15 phút**. Không biến nó thành database offline.
- Cursor: `cursor.json` đa site, `tmp → Flush(true)/fsync → File.Move(overwrite:true)` cùng volume NTFS.
- Quota: `details/` 20MB · `history/` 10MB (LRU). Async I/O (I-11). DPAPI theo OD-4.

---

## 9. Bảo mật & Audit

- **#19** Authz theo từng route: `siteId` trong URL **không bao giờ** là nguồn quyền; quyền đến từ claims/capabilities của token. `reopen`, `historical-replay` yêu cầu `datahub_admin` (khác `ReadWriteSiteData`). *(Đường hiện có đã đúng — giữ khi thêm route mới.)*
- **#21 / I-16** `operatorId` = principal đã xác thực. Client chỉ gửi `reason`.
- **#20** `force_rebuild_tombstoned` **không** là boolean flag. Tách thành workflow `HISTORICAL_REBUILD`: admin role · reason · source archive · rebuild job id · audit · **dry-run bắt buộc**. Tuyệt đối không tạo lại row sống bằng normal ingest.
- **#26** Ghi chú vào contract: *Idempotency request hash is transport-level identity, not business canonicalization.* Không cần sửa code.

---

## 10. Ngữ nghĩa Response bulk (#25)

Với 100 item → 80 accepted / 10 duplicate / 10 terminal_locked:
```json
{ "accepted": 80, "duplicates": 10, "terminalLocked": 10,
  "changed": <số waybill đổi>, "firstSeq": ..., "lastSeq": ... }
```
`firstSeq`/`lastSeq` phản ánh **số waybill thay đổi**, **không** phải số event. Số `dashboard_changes` sinh ra = `changed`, không phải `accepted`.

**Audit nhất quán (#11)** — chốt: **insert event TRƯỚC, kiểm tombstone/terminal SAU**. Cả hai đường (tombstoned và terminal) đều **lưu event** rồi **skip mutation**, để hành vi forensic đồng nhất. Lỗi cứng (scanTime/horizon) vẫn rollback cả batch → 422.

---

## 11. Acceptance Tests — bổ sung

Giữ bộ test v4.3, thêm:

- **T-G Horizon ladder**: event 50 ngày tuổi → **reject** ở normal ingest; qua admin replay → chấp nhận. Cấu hình `ingest_horizon ≥ event_retention` → **app fail-fast khi start**.
- **T-H Dedupe sau purge**: event bị purge rồi gửi lại → không tạo lại projection (tombstone chặn); ghi nhận là anomaly, không mutate.
- **T-I Single change writer**: mọi path (ingest/purge/reopen) sinh change đều đi qua allocator; không path nào tự `UPDATE site_change_counters`.
- **T-J Snapshot vs ingest (transaction thật, không unit test)** (#5): mở snapshot txn → ingest commit → snapshot **không** thấy state sau `asOfChangeSeq`.
- **T-K Site-wide serialization throughput** (#23): 50 request đồng thời, cùng site, khác waybill → đo lock wait, txn p95, throughput. Ghi số vào contract, tune pool từ workload (#24).
- **T-L Client cache cross-site**: hai site cùng mã vận đơn → không đè cache.
- **T-M Client replay idempotent**: crash giữa page-write và cursor-write → áp lại batch cũ, kết quả không đổi.
- **T-N Reopen version bump**: reopen → `version++`, ETag cũ không còn hợp lệ, client nhận upsert.

---

## 12. Owner Decisions (bản cuối)

| # | Chốt |
|---|---|
| **OD-1** Terminal scan codes | **Vẫn chờ đối chiếu JMS thật.** Seed hiện chỉ có 98 (inventory), 110 (state_transition). Lưu dạng policy versioned trong bảng (PK đã composite — thêm `reducer_version=2` không conflict). |
| **OD-2** Horizons | **Normal ingest 45d · event/dedupe 60d (giữ nguyên) · terminal purge 90d · tombstone ≥2 năm.** |
| **OD-3** Reopen | Option A + workflow `HISTORICAL_REBUILD` riêng cho đơn đã purge. |
| **OD-4** DPAPI | Có PII (tên/địa chỉ/SĐT) → bật DPAPI per-user; chỉ mã đơn + trạng thái → ACL đủ. |
| **OD-5** Fingerprint | **Giữ code-driven `EventFingerprintV1`.** Không đưa `fingerprint_policies` vào P1. Khi có payload JMS thật → cutover `fingerprint_version = 2`. |

---

## 13. Ba việc phải khoá trước P1 (theo đề xuất của bạn, đã hiệu chỉnh)

1. ✅ **Xung đột horizon 90/60** → giải bằng ladder §1 + fail-fast (I-17). *Đây là điểm đúng và giá trị nhất của bản phản biện.*
2. ⚠️ **Verify schema** → nguyên tắc giữ; nhưng **`fingerprint_version` và PK policy đã đúng sẵn** (0.1), nên hạng mục này rút gọn còn: verify 8 cột mới của 007/008 chưa tồn tại.
3. ✅ **`IChangeSequenceAllocator`** → giữ, nhưng là **refactor hygiene** (2 writer hiện tại đều đã đúng), không phải sửa bug.

**Và một việc thứ tư mà cả hai bản trước đều bỏ sót**: **không seed `retention_policies('waybill_projections')` cho tới hết P6.** Nếu seed sớm, purge theo `updated_at` sẽ xoá projection còn sống khi chưa có tombstone → đơn có thể bị hồi sinh. Đây là rủi ro vận hành cụ thể, không phải lý thuyết.
