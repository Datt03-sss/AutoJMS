> ⛔ **SUPERSEDED bởi [streaming-v4.6-contract.vi.md](./streaming-v4.6-contract.vi.md)** (2026-09-05). v4.6 sửa 4 phát biểu sai của bản này: (1) `ingested_at` là transaction start time từ DB default, không phải ingress time, và giống hệt nhau cho mọi item trong một bulk; (2) delete-retention 90d KHÔNG cho client 90 ngày delta-resync; (3) version guard bất khả thi cho delete (delete-change không mang version); (4) tuyên bố RAM 50-100 object chưa được chứng minh.

# Streaming v4.5 — Pre-Implementation Contract

> **THAY THẾ** v4.2 / v4.3 / v4.4. Các bản trước chỉ giữ để tra lịch sử quyết định.
> **Ngày**: 2026-09-05 · **Trạng thái**: sẵn sàng duyệt **P0**.
> **Thứ tự ưu tiên khi có xung đột**: `brownfield safety > correctness > rollback > đơn giản > performance`.
> **Mục tiêu**: đủ chặt để Claude Code triển khai **không tự suy diễn semantics**. Mọi chỗ chưa chốt đều được đánh dấu `⛔ PENDING OD-x` và **cấm** implement.
> **Ràng buộc tài liệu này**: không chứa application code; không tự chọn business policy; Redis **không** phải dependency; không viết lại snapshot / idempotency / change-feed / retention đã đúng trên `origin/main`; mọi thay đổi phải **additive** và **brownfield-safe**.

---

## §-1. Quy ước dấu hiệu (đọc trước tiên)

| Dấu | Ý nghĩa | Hành động của Claude Code |
|---|---|---|
| **⛔** | Mục bị **chặn** hoặc **chưa được Owner chốt** | **CẤM implement.** Dừng và báo cáo. |
| **✅** | Đã tồn tại và đúng trong `origin/main` | **CẤM viết lại.** Chỉ được viết test xác minh. |
| **PENDING OD-x** | Chờ quyết định của Owner số x (§21) | Không được tự chọn giá trị/hành vi thay Owner. |

> Bất kỳ dòng nào mang **⛔** đều là ranh giới cứng, bất kể diễn đạt kèm theo là "PENDING", "Chờ Owner", "BLOCKED", hay "CẤM". Không có ngoại lệ ngầm.

---

## §0. Bảng thay đổi v4.4 → v4.5

| # | Hạng mục | v4.4 | v4.5 | Loại |
|---|---|---|---|---|
| 1 | Đường ghi multi-client | Nói chung "bulk fenced" | **Tách tường minh 2 đường**: interactive (không leader) vs bulk (leader-fenced). **Đã xác minh code có sẵn** | Làm rõ + verify |
| 2 | Capability name | Ghi `ReadWriteSiteData` (theo v4.2) | **Sửa: `DeviceCapability.WriteSiteData`** — `ReadWriteSiteData` **không tồn tại** | 🔴 Sửa lỗi định danh |
| 3 | Admin authz | Ngụ ý `datahub_admin` là capability | **Sửa: admin KHÔNG phải DeviceCapability**; cả 3 role đều giữ cả 2 capability → admin dùng admin/operator token middleware | 🔴 Sửa lỗi mô hình |
| 4 | Admin Reopen | Chỉ nói bump version + phát change | **Bắt buộc `Idempotency-Key` + retry-safe semantics đầy đủ** | Bổ sung |
| 5 | Terminal lifecycle | Chỉ có "set is_terminal" | **State machine đầy đủ**, gồm reset metadata khi reopen và terminal-lại | Bổ sung |
| 6 | Terminal retention clock | Ngầm dùng `terminal_at` | **Tách thành OD-6**: A=`source_event_at` vs B=received time. **Cấm implementation tự chọn** | 🔴 Nâng thành OD |
| 7 | Dedupe guarantee | "dedupe theo fingerprint" | **Định nghĩa có thời hạn**: guarantee chỉ tồn tại khi event mang fingerprint còn được retain | Làm rõ |
| 8 | Historical replay | Có endpoint + workflow | **Tách Contract vs Implementation**; cấm implement khi chưa có archive source (OD-7) | 🔴 Hoãn |
| 9 | `IChangeSequenceAllocator` | Refactor ở P1 | **Hoãn**: chỉ wrapper không đổi execution order; refactor sâu sau khi test pass | Hạ rủi ro |
| 10 | Backup/restore | Không có gate | **P0 gate bắt buộc**: backup → verify restore → smoke test trên DB restored → mới migrate. Fail → `STOP RELEASE` | 🔴 Bổ sung |
| 11 | Infrastructure | Không kiểm | **P0 gate**: firewall/WireGuard/TLS VerifyFull phải PASS trước cutover | 🔴 Bổ sung |
| 12 | Migration preflight | "verify 8 cột chưa tồn tại" | **Matrix 5 trạng thái** (exists/missing/type/nullability/default); partial-state → `STOP + REPORT`, cấm auto-skip | 🔴 Nâng cấp |
| 13 | Terminal policy | Đề xuất code chờ OD-1 | **Fail-closed tường minh**: chưa có policy → `is_terminal=false`, không tombstone, không purge | Làm chặt |
| 14 | Normalizer version | Chỉ thêm cột | **Lifecycle đầy đủ**: khoá `=1`, nơi chọn, nơi lưu, cách debug | Bổ sung |
| 15 | Dashboard cache | Nhiều page + LRU | **Đơn giản hoá: chỉ current page**, disposable, debounce 300–500ms | Đơn giản hoá |
| 16 | Site switching | Chỉ nói scope key | **Thủ tục 6 bước** + test contamination | Bổ sung |
| 17 | Disk full | Không đề cập | **Hành vi bắt buộc**: cursor không tiến, fallback remote, không crash | 🔴 Bổ sung |
| 18 | SLO | P95 < 5s chung | **Tách healthy vs degraded**; cấm tuyên bố P95<5s ở degraded | Sửa |
| 19 | Rollback | Rải rác | **Mục riêng, tiêu chí tường minh theo phase** | Bổ sung |
| 20 | Phase plan | P0–P7 + R1 | **Sắp lại theo yêu cầu**: P0 gồm backup + infra + baseline throughput, không đổi code | Sắp lại |

---

## §1. Quyết định KHOÁ (không được thay đổi trong v4.5)

1. **PostgreSQL là Source of Truth** duy nhất.
2. **Client không bao giờ kết nối trực tiếp PostgreSQL.**
3. **Redis KHÔNG nằm trong correctness path** — và không phải dependency của bất kỳ phase bắt buộc nào.
4. **SignalR chỉ là advisory doorbell** — không mang payload nghiệp vụ.
5. **Safety Poll (10s) là correctness backstop** — mất SignalR không được mất dữ liệu.
6. **Client Disk-First** — RAM chỉ giữ rows đang hiển thị.
7. **`site_change_counters ... FOR UPDATE` là serialize point** hiện tại, **giữ nguyên**.
8. **Idempotency hiện có phải được giữ** nguyên hành vi.
9. **Lease fencing hiện có phải được giữ** cho bulk ingest.
10. **Snapshot / change-feed / retention hiện có phải được REUSE**, không viết lại.
11. **Terminal anti-resurrection enforce phía server.**
12. **KHÔNG chuyển sang per-waybill concurrency** ở giai đoạn đầu.

---

## §2. Hai đường ghi (Multi-Writer vs Leader-Fenced Bulk)

> ✅ **ĐÃ TỒN TẠI TRONG CODE — KHÔNG ĐƯỢC CODE LẠI.** Bằng chứng: `IngestEndpoints.cs` map hai route vào cùng `HandleAsync` với cờ `requireFence` khác nhau.

### 2.1 Interactive Observation (multi-client)
```
POST /api/v1/sites/{siteId}/jms/observations
```
| Thuộc tính | Giá trị (đã xác minh trong code) |
|---|---|
| Xác thực | device bearer token (`context.GetDeviceIdentity()`) |
| Capability | **`DeviceCapability.WriteSiteData`** |
| Leader term | **KHÔNG yêu cầu** — `requireFence: false`, `X-Leader-Term` không được đọc |
| Retry-safe | `Idempotency-Key` bắt buộc, độ dài **8–128** |
| Dedupe | server-side fingerprint (`EventFingerprintV1`) + `UNIQUE(site_id, event_fingerprint)` |

### 2.2 Bulk / Scheduled Ingest (leader-fenced)
```
POST /api/v1/sites/{siteId}/jms/ingest
```
| Thuộc tính | Giá trị |
|---|---|
| Capability | **`DeviceCapability.WriteSiteData`** (giống trên) |
| Leader term | **BẮT BUỘC** `X-Leader-Term` ≥ 1; thiếu/sai → `409 LEADER_FENCED` |
| Fencing | kiểm `site_fetch_leases` 3 lần: trước idempotency, khi replay, **trước commit**; dùng `clock_timestamp()` |
| Mục đích | chỉ dành cho scheduled/leader synchronization path |

### 2.3 ⛔ Ràng buộc bắt buộc
- **CẤM** áp `X-Leader-Term` lên `/observations`.
- **CẤM** dùng định danh `ReadWriteSiteData` — enum `DeviceCapability` chỉ có `ReadSiteData` và `WriteSiteData` (`AuthContracts.cs:41-48`).
- **CẤM** coi `datahub_admin` là một `DeviceCapability`. Comment trong code nêu rõ: *"All three named roles currently hold both capabilities"* → capability **không** phân biệt được admin. Admin endpoints phải đi qua **admin/operator token middleware** riêng (như `manifestAdmin`), không qua device token.

---

## §3. Terminal Lifecycle (state machine khoá cứng)

> ⛔ Toàn bộ §3 **fail-closed** cho tới khi Owner ký **OD-1**. Xem §3.4.

### 3.1 Normal terminal event
```
is_terminal          = true
terminal_at          = <incoming source_event_at>     (nếu OD-6 = A)
terminal_state_code  = <incoming terminal scan code>
projection.version   ++
change_seq           ++            → dashboard_changes(operation='upsert')
```

### 3.2 Admin Reopen
```
is_terminal          = false
terminal_at          = NULL
terminal_state_code  = NULL
projection.version   ++
change_seq           ++            → dashboard_changes(operation='upsert')
waybill_tombstones   → xoá dòng (nếu có)
```

### 3.3 Terminal lại sau Reopen
Terminal metadata được **tạo lại hoàn toàn từ event terminal mới**.
**CẤM** giữ lại `terminal_at` cũ. Lịch sử cũ nằm ở event store + audit log, **không** ở projection.

### 3.4 ⛔ Fail-closed khi chưa có policy (OD-1)
Cho tới khi Owner duyệt danh sách terminal scan code:
- **Không** scan type nào được mặc định là terminal.
- `is_terminal` giữ `false`.
- **Không** tạo tombstone.
- **Không** chạy terminal projection purge.
- **CẤM** tự seed `9001` / `9002` / `9004` hoặc bất kỳ code nào chưa chứng minh bằng payload JMS thật.

*Ghi chú schema*: `jms_event_policies` hiện có `event_kind ∈ {state_transition, activity, inventory, communication}` — **không có** khái niệm terminal. Cách biểu diễn terminal (thêm cột `is_terminal` vào bảng policy, hay bảng riêng) là **quyết định thiết kế thuộc P1**, chỉ chốt sau khi OD-1 xác định tập code.

---

## §4. ⛔ OD-6 — Terminal retention tính từ mốc nào

**Phải chọn một, Owner ký. CẤM implementation tự chọn.**

| Phương án | Định nghĩa | Hệ quả |
|---|---|---|
| **A. `source_event_at`** | tuổi terminal tính từ thời điểm nghiệp vụ | Late terminal observation (ví dụ đến muộn 95 ngày) **có thể đủ tuổi purge ngay khi vừa được nhận** → đơn xuất hiện rồi biến mất gần như tức thì. **Bắt buộc** có acceptance test `T-TERM-LATE`. |
| **B. Server observed/received time** | tuổi tính từ lúc server ghi nhận terminal | Không có hiện tượng purge-ngay; nhưng "tuổi" lệch khỏi thời gian nghiệp vụ, và một đơn terminal từ lâu vẫn sống thêm đủ retention sau khi được nhận. |

Nếu chọn **A** → bắt buộc test late-terminal. Nếu muốn tránh hành vi đó → chọn **B**.
Cột lưu vẫn là `terminal_at`; OD-6 chỉ quyết định **giá trị nào được ghi vào đó**.

---

## §5. Horizon Ladder & giới hạn của Dedupe Guarantee

```
NORMAL_INGEST_HORIZON  <  EVENT_RETENTION (= DEDUPE RETENTION)  <<  TOMBSTONE RETENTION
        45d                            60d                              >= 2 năm
```

### 5.1 Định nghĩa chính xác của dedupe guarantee
> **Dedupe guarantee chỉ tồn tại trong khoảng thời gian mà event mang fingerprint còn được retained.**

Sau khi event bị purge:
- duplicate **có thể** được insert lại → đây **không còn** là vi phạm guarantee;
- nếu event stale → **không mutate projection** (newest-wins, I-10);
- nếu tombstone tồn tại → **không resurrect**;
- **CẤM** tiếp tục mô tả hệ thống là "dedupe vĩnh viễn".

### 5.2 Bất biến cấu hình
`NORMAL_INGEST_HORIZON < EVENT_RETENTION`. Server **fail-fast khi khởi động** nếu vi phạm.

### 5.3 Bảng tham số
| Tham số | Giá trị | Nguồn | Ghi chú |
|---|---|---|---|
| Normal ingest horizon | **45d** ⛔ OD-2 | app config | cũ hơn → reject |
| Future skew | 5 phút | app config | |
| Event / dedupe retention | **60d** | `retention_policies('waybill_scan_events')` | **giữ nguyên seed hiện có** |
| Terminal projection purge | **90d** ⛔ OD-2 | `retention_policies('waybill_projections')` | **chỉ seed sau P6** |
| `dashboard_changes` thường | 14d | `retention_policies` | đã có |
| `dashboard_changes` delete | **90d** (min 30 / max 365) | `DataHubRuntimeOptions.TombstoneRetention` | đã có, tách riêng |
| `waybill_tombstones` | ≥ 2 năm ⛔ OD-2 | policy | phải sống lâu hơn mọi đường replay |

---

## §6. Admin Reopen — bắt buộc retry-safe

```
POST /api/v1/sites/{siteId}/waybills/{waybillNo}/reopen
Header: Idempotency-Key (bắt buộc, 8–128)
Body:   { "reason": "<bắt buộc>" }
```

| Tình huống | Hành vi bắt buộc |
|---|---|
| same key + same body | **replay exact previous response** (status + body) |
| same key + different body | `409 IDEMPOTENCY_KEY_REUSED` |
| duplicate retry | **KHÔNG** bump `projection.version` lần hai |
| duplicate retry | **KHÔNG** tạo thêm dòng `dashboard_changes` |
| duplicate retry | **KHÔNG** tạo thêm logical reopen event ngoài bản ghi đã dedupe |
| projection đã purge | `410 GONE` (`PROJECTION_ALREADY_PURGED`) |
| audit | ghi **principal thật từ authentication context** |

### 6.1 ⛔ Audit identity
`operatorId` / actor **luôn** lấy từ principal đã xác thực. Client **chỉ** được gửi `reason`.
**CẤM** nhận `operatorId` từ body/query/header do client tự khai.

### 6.2 Authz
Reopen là admin operation → **admin/operator token**, không phải device token capability (xem §2.3).

---

## §7. ⛔ Historical Replay — Contract-only, chưa implement

Event retention chỉ **60 ngày**. Do đó **CẤM** giả định replay > 60/90 ngày lấy được dữ liệu từ PostgreSQL hiện tại.

| Phần | Trạng thái |
|---|---|
| **HistoricalReplayContract** | Được định nghĩa trong tài liệu này (semantics, authz, audit, anti-resurrection) |
| **HistoricalReplayImplementation** | ⛔ **BLOCKED** cho tới khi OD-7 xác định archive source |

**CẤM tạo endpoint `replay` khi chưa có nguồn dữ liệu thật.** Nguồn hợp lệ phải là một trong: object storage · backup/archive · JMS export · nguồn lịch sử khác được Owner xác nhận.

Khi implement (sau OD-7), bắt buộc: admin role · `reason` · source archive id · rebuild job id · audit · **dry-run bắt buộc** · retry-safe bằng job identity riêng. **CẤM** `force_rebuild_tombstoned` dạng boolean flag; phải là workflow `HISTORICAL_REBUILD` có phê duyệt.

---

## §8. Change Sequence — đóng băng, không refactor sớm

`IngestRepository` và `RetentionRepository` **đã** dùng counter-lock đúng. `IChangeSequenceAllocator` là **hygiene**, không phải bug.

Trong v4.5:
- **Giữ nguyên transaction behavior hiện tại.**
- Chỉ được introduce wrapper/abstraction **nếu wrapper không thay đổi execution order**.
- **CẤM** thay đổi allocation semantics.
- **CẤM** thay đổi `site_change_counters` (schema hoặc cách khoá).
- Chỉ xem xét refactor sâu **sau khi** acceptance tests pass.

---

## §9. Normalizer Version Lifecycle

| Câu hỏi | Chốt |
|---|---|
| Giá trị đầu tiên | **`normalizer_version = 1`** cho implementation đầu tiên |
| Khi nào bump | **Chỉ khi semantic output contract thay đổi** (cùng input JMS → output chuẩn hoá khác) |
| Khi nào KHÔNG bump | **CẤM** bump chỉ vì app version tăng, refactor, đổi tên biến, hay sửa bug không đổi output |
| Nơi chọn | Tại tầng ingest, **resolve & pin một lần cho cả request**, trước khi vào transaction |
| Nơi lưu | Cột `normalizer_version` trên `waybill_scan_events` (thêm ở migration 007) |
| Cách debug event cũ | Đọc `normalizer_version` của event → áp đúng bản normalizer tương ứng khi diễn giải/replay; **không** giả định event cũ được chuẩn hoá bằng bản hiện tại |

---

## §10. Migration Safety

### 10.1 Preflight matrix (bắt buộc, thay cho kiểm nhị phân)
Với **mỗi** cột/bảng dự kiến thêm, preflight phải phân loại vào **5 trạng thái**:

| Trạng thái | Hành động |
|---|---|
| `missing` | ✅ được phép áp migration |
| `exists` (đúng type + nullability + default) | ✅ skip cột đó, ghi log |
| `unexpected type` | ⛔ **STOP + REPORT** |
| `unexpected nullability` | ⛔ **STOP + REPORT** |
| `unexpected default` | ⛔ **STOP + REPORT** |

> ⛔ **CẤM** hành vi "một cột đã tồn tại → skip toàn bộ migration". Partial state phải được báo cáo chi tiết per-column, không auto-resolve.

Nguồn kiểm: `information_schema.columns` (`data_type`, `is_nullable`, `column_default`) và `information_schema.tables`.

### 10.2 Quy tắc
- Mọi migration **additive** và **rollback-safe** (không drop cột/bảng, không đổi type, không sửa dữ liệu cũ).
- Index trên bảng production dùng **`CREATE INDEX CONCURRENTLY`** trong **migration riêng, ngoài transaction** (đặt tên `*_notx.sql`, theo đúng tiền lệ cảnh báo ở `006`).
- Mỗi migration ghi `schema_migrations` với `ON CONFLICT DO NOTHING`.
- **CẤM** đụng `fingerprint_version` (đã tồn tại `001_core.sql:58`, `NOT NULL DEFAULT 1`).
- **CẤM** đụng PK của `jms_event_policies` (đã composite `001_core.sql:129`).

### 10.3 Phạm vi schema delta (chỉ ngần này)
| Bảng | Cột/đối tượng thêm | Ghi chú |
|---|---|---|
| `waybill_scan_events` | `event_kind`, `reducer_version`, `normalizer_version`, `source_schema_version` | tất cả có default an toàn |
| `waybill_projections` | `is_terminal`, `terminal_at`, `terminal_state_code`, `last_change_seq` | `is_terminal NOT NULL DEFAULT false` |
| bảng mới | `waybill_tombstones` | PK `(site_id, waybill_no)` |
| index (`_notx`) | `ix_waybill_projections_terminal_retention` partial `WHERE is_terminal = true` | `CONCURRENTLY` |

**Tái dùng** `event_occurred_at` (source time) và `ingested_at` (received time) — **CẤM** thêm cột trùng khái niệm.
**CẤM** đưa `fingerprint_policies` và `get_waybill_advisory_lock_key` vào các phase bắt buộc.

---

## §11. Infrastructure Gate (P0 — trước mọi API cutover)

Phải xác minh và ghi kết quả bằng chứng:

| Kiểm tra | Kỳ vọng |
|---|---|
| `Internet → VPS-DB:5432` | **BLOCKED** |
| `VPS-API (WireGuard) → VPS-DB:5432` | **ALLOWED** |
| TLS mode | **VerifyFull = PASS** |
| Certificate hostname | khớp đúng hostname được kết nối |
| `API → PostgreSQL` connection | **PASS** |

Ràng buộc:
- PostgreSQL container **không được** publish trực tiếp ra Internet.
- Nếu cần Docker bind, dùng dạng bind vào địa chỉ nội bộ, ví dụ `10.0.0.2:5432:5432`.
- **Firewall vẫn là lớp kiểm soát độc lập** — Docker bind không thay thế firewall.

Bất kỳ mục nào FAIL → ⛔ **STOP**, không cutover.
*Tham chiếu tài liệu vận hành đã có*: `backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md`, `DEPLOY_EXECUTION_CHECKLIST.vi.md`, `bootstrap-vps.sh`.

---

## §12. Backup / Restore Gate (P0 — bắt buộc, không thương lượng)

Vì PostgreSQL là single source of truth, **trước mọi migration production**:

1. **Backup DB hiện tại** → `backend/datahub/scripts/backup-postgres.ps1` *(đã tồn tại)*.
2. **Verify restore được** → `backend/datahub/scripts/restore-postgres.ps1` vào một instance staging/tạm *(đã tồn tại)*.
3. **Chạy representative API smoke test trên DB đã restore** → `backend/datahub/scripts/smoke-test.sh` *(đã tồn tại — 10 bước: provision site → license assertion → enroll device → acquire lease → ingest behind fence → replay cùng Idempotency-Key → read change feed → read snapshot → 5 negative case → release lease)*.
4. **Chỉ sau khi cả 3 bước PASS** mới được chạy migration.

> ⛔ Nếu backup hoặc restore FAIL → **STOP RELEASE**.
> ⛔ **CẤM** "migration trước, backup sau" trong mọi hoàn cảnh.

### 12.1 RPO / RTO
Ghi **giá trị thật hiện tại**, không đặt mục tiêu vượt khả năng backup thực tế:

| Chỉ số | Giá trị | Trạng thái |
|---|---|---|
| RPO (mất dữ liệu tối đa) | = khoảng cách giữa 2 lần backup thủ công | ⛔ **PENDING OD-8** — Owner ghi tần suất backup thật |
| RTO (thời gian phục hồi) | = thời gian đo được của `restore-postgres.ps1` + smoke test | ⛔ **PENDING** — phải **đo** ở P0, không ước lượng |

Không được tuyên bố RPO/RTO tốt hơn số đo được ở P0.

---

## §13. Retention Safety

### 13.1 Event purge
Theo `retention_policies` **hiện có**. Không viết worker mới.

### 13.2 Terminal projection purge
Điều kiện **bắt buộc đủ cả hai**:
```
is_terminal = true
AND terminal_at <= <retention cutoff>      (mốc theo OD-6)
```
Trong **cùng một transaction**:
```
tạo waybill_tombstones
+ cấp change_seq (qua counter-lock)
+ INSERT dashboard_changes(operation='delete')
+ DELETE waybill_projections
```

### 13.3 ⛔ Ràng buộc cứng
- **CẤM** seed `retention_policies('waybill_projections')` trước khi terminal + tombstone protection hoàn tất (hết P6).
- **Active projection KHÔNG BAO GIỜ** được purge theo luật inactivity-only.
- ⚠️ **Cảnh báo brownfield**: vị từ purge **hiện tại** trong `RetentionRepository.DeleteProjectionsAsync` là `p.updated_at < now() - delete_after` — tức **theo bất hoạt, không theo terminal**. Hiện nó **đang ngủ** vì không có policy row. Seed sớm = xoá projection còn sống khi chưa có tombstone → **đơn có thể bị hồi sinh**. Đây là rủi ro kích hoạt bằng **một dòng SQL**.

---

## §14. Client — Disk Cache

Giữ **Disk-First**. Đơn giản hoá dashboard cache:

- **Chỉ cache current page/query.**
- Page cache là **disposable** — không phải database offline.
- **KHÔNG** duy trì nhiều page nhất quán bằng delta.
- Delta ảnh hưởng current page → **mark stale** → **debounce 300–500ms** → refetch current page.
- Page khác **chỉ** tải lại khi user điều hướng tới.

### 14.1 Đường dẫn bắt buộc scope theo site
```
%LOCALAPPDATA%\AutoJMS\cache\{siteId}\dashboard\{queryHash}\{pageCursorHash}.json
%LOCALAPPDATA%\AutoJMS\cache\{siteId}\details\{canonicalWaybillNo}.json
%LOCALAPPDATA%\AutoJMS\cache\{siteId}\history\{canonicalWaybillNo}.json
%LOCALAPPDATA%\AutoJMS\cache\cursor.json          (đa site, tách nhánh theo siteId)
```
> ⛔ **KHÔNG file cache nào được bỏ `siteId`.** Hai site có cùng mã vận đơn **không được** đè nhau.

---

## §15. Site Switching

Khi đổi Site, thực hiện **đúng thứ tự**:

1. **Stop** xử lý update của site cũ.
2. **Unsubscribe** SignalR group của site cũ.
3. **Subscribe** group của site mới.
4. **Load cursor** của site mới.
5. **Load / invalidate** current dashboard query.
6. **Never apply** change của site cũ vào cache site mới.

---

## §16. Client Cursor Correctness

### 16.1 Invariant thứ tự
```
persist cache  →  persist cursor (atomic)  →  update UI
```
Hoặc thứ tự tương đương, miễn giữ được:
> **`cursor` KHÔNG BAO GIỜ được tiến quá trạng thái đĩa đã persist thành công.**

### 16.2 Idempotent merge (bắt buộc)
Áp cùng một change batch hai lần phải cho kết quả giống hệt:
```
if incoming.version <= cached.version → ignore
```
Lý do: SignalR + Safety Poll có thể đưa cùng một change nhiều lần; crash giữa "ghi page" và "ghi cursor" sẽ khiến batch cũ được áp lại.

### 16.3 Atomic cursor write
```
tmp  →  flush(true)  →  atomic replace
```
Cùng volume NTFS. **Không** synchronous disk I/O trên UI thread.

### 16.4 Hành vi khi disk full
| Bước | Bắt buộc |
|---|---|
| cache write | fails |
| cursor | **không tiến** |
| UI | fallback sang remote |
| app | **không được crash** |

---

## §17. SLO — tách healthy và degraded

| Chế độ | Cam kết |
|---|---|
| **Healthy** (SignalR hoạt động) | E2E propagation **P95 < 5s** |
| **Degraded** (SignalR không khả dụng) | **Correctness được đảm bảo**; Safety Poll = 10s; freshness bound **≤ 10s** (hoặc bound đã định nghĩa) |

> ⛔ **CẤM** tuyên bố P95 < 5s trong degraded mode.

---

## §18. Đo throughput của per-site lock (P1 baseline + P7 gate)

Vì per-site counter lock là **deliberate bottleneck**, phải đo — không phỏng đoán:

**Kịch bản**: 10 và 50 concurrent request · **khác waybill** · **cùng site**.

**Metrics bắt buộc**: `lock_wait_p95` · `ingest_p95` · `commit_p95` · `throughput` · `queue depth` (nếu quan sát được).

**Quy tắc quyết định**: nếu workload thật (2–5 client) đạt target → **giữ kiến trúc**.
> ⛔ **CẤM** thêm per-waybill concurrency chỉ để "tối ưu cho đẹp".

---

## §19. Phase Plan & Gates

| Phase | Nội dung | ⛔ Gate để qua phase |
|---|---|---|
| **P0** Brownfield Freeze + Backup + OD | schema inventory (preflight matrix §10.1) · backup + verify restore + smoke test trên DB restored (§12) · network prerequisites (§11) · OD-1..OD-8 · **baseline throughput** (§18) · **không đổi code** | Backup+restore+smoke PASS · infra PASS · Owner ký toàn bộ OD · có số baseline |
| **P1** Server Safety | terminal columns · tombstones · horizon guardrail + fail-fast · terminal guard (anti-resurrection) · terminal lifecycle · **thay đổi ingest tối thiểu cần thiết**. **Không Redis. Không client. Không rewrite snapshot/change-feed.** | Build Release PASS · test P1 PASS · throughput không xấu hơn baseline |
| **P2** Server Read | `/waybills` · `/detail` · `/history` · ETag · **reuse** `/changes` và snapshot hiện có | Build PASS · test P2 PASS · ETag đúng semantics |
| **P3** Client Remote Read + Disk Cache | `%LOCALAPPDATA%` · current-page cache · detail/history cache · multi-site cursor · site-scoped SignalR · async disk I/O | UI không regress · test cache/cursor/disk-full PASS |
| **P4** Multi-client Observation Write Cutover | interactive observation (**no leader fencing**) · bulk scheduled ingest (**leader-fenced**) · JMS Normalizer · server reducer · **drain legacy outbox trước hard cut** | `outbox = 0` · shadow-mode đối chiếu khớp |
| **P5** SignalR | advisory doorbell · Safety Poll 10s · **no payload over SignalR** | test mất kết nối PASS |
| **P6** Retention | activate terminal projection retention policy · tombstones · delete changes · batch purge · historical replay **vẫn contract-only** trừ khi có archive source | test retention × ingest/reopen/replay PASS |
| **P7** Production Validation | 50 concurrent · 2/5/10 client · crash · network loss · SignalR loss · disk full · DB restart · cache replay · retention race · site switching | toàn bộ PASS · đạt SLO §17 |
| **R1** *(Optional)* Redis | **Chỉ khi** đo ở P7 cho thấy PostgreSQL cần tăng tốc. Bắt đầu **chỉ với Detail Pointer**. ⛔ **CẤM** implement Dashboard Epoch | có số đo chứng minh |

---

## §20. Rollback Criteria (tường minh)

### 20.1 Nguyên tắc chung
Mọi migration là **additive** → rollback mặc định là **tắt code path mới**, **không** drop schema. Cột thừa không gây hại; drop cột mới là thao tác nguy hiểm hơn giữ lại.

### 20.2 Theo phase

| Phase | Điều kiện kích hoạt rollback | Hành động rollback |
|---|---|---|
| **P0** | backup fail · restore fail · smoke test trên DB restored fail · bất kỳ mục infra FAIL | **STOP RELEASE**. Không có gì để rollback vì chưa đổi gì. |
| **P1** | ingest error rate tăng · throughput xấu hơn baseline đáng kể · anti-resurrection chặn nhầm đơn hợp lệ · fail-fast horizon chặn traffic hợp lệ | Tắt terminal guard qua config flag → hành vi trở lại như trước. **Giữ nguyên cột đã thêm** (vô hại vì `is_terminal DEFAULT false`). |
| **P2** | endpoint đọc mới trả sai · ETag gây stale ở client | Ngừng route mới. `/changes` + snapshot cũ **không đổi** nên client cũ vẫn chạy. |
| **P3** | UI đơ/giật · cache corrupt · cursor tiến sai · disk full gây crash | Tắt disk-cache path → client đọc thẳng remote. Xoá thư mục cache là thao tác an toàn. |
| **P4** | shadow-mode lệch dữ liệu · outbox không drain về 0 · observation bị từ chối hàng loạt | **Không hard-cut**. Giữ LocalDb, quay lại đường ghi cũ. Hard-cut chỉ một chiều **sau khi** `outbox = 0`. |
| **P5** | doorbell gây bão request · SignalR làm treo client | Tắt SignalR → Safety Poll 10s vẫn đảm bảo correctness (đây chính là lý do Safety Poll là backstop). |
| **P6** | purge xoá nhầm đơn active · client mất dòng không giải thích được · delete-change không tới client | **Xoá `retention_policies('waybill_projections')` ngay** → purge trở lại trạng thái ngủ. Tombstone đã tạo **giữ nguyên** (an toàn, chỉ chặn resurrect). Dữ liệu đã xoá phục hồi từ backup §12. |
| **R1** | Redis timeout/lỗi ảnh hưởng độ trễ | Tắt Redis → bypass 100% PostgreSQL. Vì Redis không nằm trong correctness path, đây là rollback không mất dữ liệu. |

### 20.3 ⛔ Điểm không thể rollback bằng config
- **Terminal projection purge (P6)** là thao tác **huỷ dữ liệu**. Sau khi chạy, chỉ backup mới khôi phục được. → Đây là lý do §12 là gate cứng và P6 nằm sau P3/P4.
- **Hard-cut LocalDb (P4)** một chiều. → gate `outbox = 0`.

---

## §21. Owner Decisions

| # | Hạng mục | Đề xuất / Lựa chọn | Trạng thái |
|---|---|---|---|
| **OD-1** | Danh sách terminal scan code | **Chưa có cơ sở** — seed hiện chỉ phân loại `98` (inventory), `110` (state_transition). Phải đối chiếu payload JMS thật. Cho tới khi ký: fail-closed (§3.4) | ⛔ **PENDING OD-1** |
| **OD-2** | Giá trị horizon | ingest **45d** · event/dedupe **60d** (giữ nguyên) · terminal purge **90d** · tombstone **≥2 năm** | ⛔ **PENDING OD-2** |
| **OD-3** | Reopen sau purge | **Option A** (chỉ reopen khi projection còn sống) + workflow `HISTORICAL_REBUILD` riêng | ⛔ **PENDING OD-3** |
| **OD-4** | DPAPI cho disk cache | Có PII (tên/địa chỉ/SĐT) → bật DPAPI per-user; chỉ mã đơn + trạng thái → ACL Windows đủ | ⛔ **PENDING OD-4** |
| **OD-5** | Fingerprint policy | **Giữ code-driven `EventFingerprintV1`**. Không đưa `fingerprint_policies` vào phase bắt buộc. Khi có payload thật → cutover `fingerprint_version = 2` | ⛔ **PENDING OD-5** |
| **OD-6** | Mốc tính terminal retention | **A** = `source_event_at` (cần test late-terminal) · **B** = server received time (tránh purge-ngay) | ⛔ **PENDING OD-6** — CẤM tự chọn |
| **OD-7** | Archive source cho Historical Replay | object storage · backup/archive · JMS export · nguồn khác. **Không có → không implement** | ⛔ **PENDING OD-7** |
| **OD-8** | Tần suất backup (RPO) | Owner ghi tần suất backup thật đang chạy; RTO **đo** ở P0 | ⛔ **PENDING OD-8** |

---

## §22. ĐÃ CÓ TRONG CODE — KHÔNG ĐƯỢC CODE LẠI

| Hạng mục | Vị trí | Ghi chú |
|---|---|---|
| **Tách interactive vs leader-fenced** | `IngestEndpoints.cs` (`requireFence: false` / `true`) | ✅ Yêu cầu #2 **đã thoả** — chỉ cần test xác minh |
| Ingest 1-transaction, bulk ≤ 200, all-or-nothing | `IngestRepository.IngestAsync` | ✅ |
| Idempotency đầy đủ (reserve `status_code=0`, replay, `KEY_REUSED`, `IN_PROGRESS`) | `IngestRepository` | ✅ |
| Lease fencing 3 chốt, dùng `clock_timestamp()` | `IngestRepository.CheckFenceAsync` | ✅ |
| Dedupe `UNIQUE(site_id,event_fingerprint)` + `ON CONFLICT DO NOTHING RETURNING id` | `001_core` + `InsertEventAsync` | ✅ |
| Cấp phát `change_seq` dưới `FOR UPDATE` | `ReadCounterAsync` / `UpdateCounterAsync` | ✅ |
| Snapshot `RepeatableRead` + `LIMIT maxRows+1` + `truncated` + `asOfChangeSeq` | `ChangeRepository.ReadSnapshotAsync` | ✅ |
| Delta feed + `pruned_through_seq` + `RESYNC_REQUIRED` | `ChangeRepository` + `ChangeCursorWindow` | ✅ |
| **Projection purge + delete-change + counter-lock (CTE nguyên tử)** | `RetentionRepository.DeleteProjectionsAsync` | ✅ **đang ngủ** — chỉ cần đổi vị từ sang `is_terminal` |
| Retention cấu hình được + tombstone retention riêng (90d, 30–365) | `retention_policies` + `DataHubRuntimeOptions` | ✅ |
| `fingerprint_version` (`NOT NULL DEFAULT 1`) | `001_core.sql:58` | ✅ **cấm đụng** |
| PK composite `jms_event_policies` | `001_core.sql:129` | ✅ **cấm đụng** |
| Client xử lý `operation='delete'`, last-op-wins trong trang | `DataHubClient` (`DeleteOperation`) | ✅ |
| Authz: token là nguồn quyền, `siteId` URL chỉ để đối chiếu | `TenantAuthorizationEvaluator` | ✅ |
| Pool / statement timeout / idle timeout cấu hình được | `PostgresDataSource`, `DataHubRuntimeOptions` | ✅ |
| Backup / Restore / Smoke test / Apply-migrations | `backend/datahub/scripts/*` | ✅ **dùng lại cho P0 gate** |

---

## §23. Acceptance Test Matrix

`T` = loại: **I**=integration (DB thật), **U**=unit, **M**=manual/ops, **L**=load/chaos.

### 23.1 Multi-writer & fencing (§2)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-W1 | P1 | non-leader client gửi `/observations` | **200 OK**, event được ghi | I |
| AT-W2 | P1 | non-leader client gọi `/ingest` (không `X-Leader-Term`) | **409 LEADER_FENCED** | I |
| AT-W3 | P1 | stale leader (term cũ) commit `/ingest` | **409 LEADER_FENCED**, không commit | I |
| AT-W4 | P1 | device thiếu `WriteSiteData` | **403** | I |
| AT-W5 | P1 | `/observations` không có `Idempotency-Key` | **400** | I |

### 23.2 Terminal lifecycle (§3)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-T1 | P1 | terminal event → projection | `is_terminal=true`, `terminal_at`/`terminal_state_code` set, `version++`, 1 change | I |
| AT-T2 | P1 | event thường đến sau terminal | bị chặn, không mutate, đếm `terminalLocked` | I |
| AT-T3 | P1 | **Chuỗi**: terminal → reopen → old event → new terminal event | metadata terminal **tạo lại từ event mới**; `terminal_at` cũ **không** còn; retention tính theo `terminal_at` **mới** | I |
| AT-T4 | P1 | ⛔ OD-1 chưa ký | không code nào là terminal; `is_terminal=false`; không tombstone; không purge | I |
| AT-TERM-LATE | P6 | *(chỉ khi OD-6 = A)* terminal observation đến muộn hơn cutoff | hành vi purge-ngay được quan sát và **được chấp nhận có ý thức** | I |

### 23.3 Reopen retry-safe (§6)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-R1 | P1 | reopen same key + same body ×2 | replay **exact** response; `version` bump **đúng 1 lần**; **1** dòng `dashboard_changes` | I |
| AT-R2 | P1 | reopen same key + different body | **409 IDEMPOTENCY_KEY_REUSED** | I |
| AT-R3 | P1 | reopen đơn đã purge | **410 GONE** | I |
| AT-R4 | P1 | audit của reopen | actor = principal đã xác thực, **không** lấy từ body | I |
| AT-R5 | P1 | reopen bằng device token thường | **403** (phải admin token) | I |

### 23.4 Horizon & dedupe (§5)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-H1 | P1 | event 50 ngày tuổi vào `/observations` | **reject** (ngoài 45d) | I |
| AT-H2 | P1 | cấu hình `ingest_horizon ≥ event_retention` | app **fail-fast khi start** | U |
| AT-H3 | P6 | event đã bị purge, gửi lại | được insert lại; **stale → không mutate**; tombstone → **không resurrect**; không coi là vi phạm dedupe | I |
| AT-H4 | P1 | event `source_event_at` > now + 5 phút | **reject** (future skew) | I |

### 23.5 Change sequence (§8)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-C1 | P1 | ingest change + retention delete change + reopen change | cả ba cấp `change_seq` đúng, đơn điệu, không trùng, không nhảy qua seq chưa commit | I |
| AT-C2 | P1 | snapshot txn mở → ingest commit → đọc snapshot | snapshot **không** thấy state sau `asOfChangeSeq` (**transaction thật, không unit test**) | I |
| AT-C3 | P1 | client cursor cũ hơn `pruned_through_seq` | **409 RESYNC_REQUIRED** | I |

### 23.6 Retention (§13)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-RT1 | P6 | projection active (không terminal), lâu không đổi | **KHÔNG BAO GIỜ** bị purge | I |
| AT-RT2 | P6 | terminal đủ tuổi | tombstone + `dashboard_changes('delete')` + xoá projection, **cùng transaction** | I |
| AT-RT3 | P6 | retention chạy song song ingest cùng waybill | không data mồ côi, không deadlock | I |
| AT-RT4 | P6 | retention vs reopen đồng thời | kết quả nhất quán, không xoá đơn vừa reopen | I |
| AT-RT5 | P0/P6 | policy `waybill_projections` chưa seed | purge **không chạy** (`return 0,0`) | I |

### 23.7 Client cache & cursor (§14–§16)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-CL1 | P3 | Site A + Site B, **cùng mã vận đơn**, cùng SignalR connection, switch A→B→A | **không** contamination cache/dữ liệu | I |
| AT-CL2 | P3 | áp cùng change batch 2 lần | idempotent; `incoming.version <= cached.version → ignore` | U |
| AT-CL3 | P3 | crash giữa "ghi page" và "ghi cursor" | cursor **không** tiến quá đĩa đã persist; lần sau replay an toàn | I |
| AT-CL4 | P3 | **disk full** | cache write fail · cursor **không** tiến · UI fallback remote · **không crash** | I |
| AT-CL5 | P3 | delta ảnh hưởng current page | mark stale → debounce 300–500ms → refetch **current page** | I |
| AT-CL6 | P3 | cuộn/ghi cache 10MB | **không** synchronous I/O trên UI thread; UI vẫn mượt | M |

### 23.8 SLO, tải & hạ tầng (§11, §17, §18)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-P1 | P0/P7 | 10 concurrent, cùng site, khác waybill | ghi `lock_wait_p95`, `ingest_p95`, `commit_p95`, throughput | L |
| AT-P2 | P0/P7 | 50 concurrent, cùng site, khác waybill | như trên; so với baseline P0 | L |
| AT-P3 | P7 | SignalR tắt | correctness giữ nguyên; freshness ≤ 10s; **không** tuyên bố P95<5s | L |
| AT-P4 | P7 | API restart / DB restart / mất mạng | không mất change; client tự phục hồi qua cursor | L |
| AT-P5 | P0 | `Internet → VPS-DB:5432` | **BLOCKED** | M |
| AT-P6 | P0 | `VPS-API → VPS-DB:5432` qua WireGuard, TLS VerifyFull | **PASS**, hostname khớp | M |
| AT-P7 | P0 | backup → restore → smoke-test.sh trên DB restored | **PASS** cả 3; fail → STOP RELEASE | M |
| AT-P8 | P0 | migration preflight có partial state | **STOP + REPORT** per-column, **không** auto-skip | M |

---

## §24. Ràng buộc cuối cho Claude Code

1. **CẤM** implement bất kỳ mục nào mang dấu **⛔** (xem quy ước §-1), dù diễn đạt là PENDING, Chờ Owner, BLOCKED hay CẤM.
2. **CẤM** tự chọn business policy (đặc biệt OD-1 terminal codes, OD-6 clock).
3. **CẤM** gọi Redis là dependency; **CẤM** implement Dashboard Epoch.
4. **CẤM** viết lại snapshot / idempotency / change-feed / retention đã đúng (§22).
5. **CẤM** thay đổi allocation semantics của `change_seq` hoặc schema `site_change_counters`.
6. **CẤM** dùng định danh không tồn tại (`ReadWriteSiteData`); dùng đúng `DeviceCapability.WriteSiteData`.
7. **CẤM** tạo endpoint historical replay khi chưa có archive source (OD-7).
8. Mọi thay đổi phải **additive**, **rollback-safe**, và có gate tương ứng ở §19.
9. Khi contract mâu thuẫn với code thật → **dừng và báo cáo**, không tự suy diễn.
