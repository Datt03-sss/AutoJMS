# Streaming v4.6 — Production Contract

> **THAY THẾ** v4.2 → v4.5. Các bản trước chỉ giữ để tra lịch sử.
> **Ngày**: 2026-09-05 · **Trạng thái**: sẵn sàng duyệt **P0** (và chỉ P0).
> **Ưu tiên tuyệt đối**: `brownfield safety > correctness > rollback > simplicity > performance`.
> **Nguyên tắc**: v4.6 **không mở rộng kiến trúc**. Nó chỉ (a) khoá semantics còn hở, (b) sửa rủi ro correctness/cutover/client-cache, (c) đơn giản hoá phần chưa cần.
> **Source of truth khi xác minh hành vi đã tồn tại**: code trên `origin/main`.

---

## §-1. Quy ước dấu hiệu (đọc trước tiên)

| Dấu | Ý nghĩa | Hành động của Claude Code |
|---|---|---|
| **⛔** | Bị chặn / chưa được Owner chốt | **CẤM implement.** Dừng và báo cáo. |
| **✅ VERIFIED** | Đã kiểm bằng code thật, kèm vị trí | **CẤM viết lại.** Chỉ được viết test xác minh. |
| **🔴 CORRECTION** | v4.5 phát biểu **sai/thiếu chính xác**, v4.6 sửa | Dùng bản v4.6, bỏ bản cũ. |
| **PENDING OD-x** | Chờ Owner quyết định số x (§25) | Không tự chọn giá trị/hành vi thay Owner. |

---

## §0. Change Log v4.5 → v4.6

| # | Hạng mục | v4.5 | v4.6 | Loại |
|---|---|---|---|---|
| 1 | `ingested_at` | "server observed receive time" | **Chính xác: DB `DEFAULT now()` = transaction start time**; API **không bao giờ** ghi cột này; **đồng nhất cho toàn bộ items trong một bulk request** | 🔴 CORRECTION |
| 2 | Tie-break bằng received time | Được ngụ ý là khả thi | **Bất khả thi trên bulk** — mọi event cùng transaction có `ingested_at` giống hệt nhau | 🔴 CORRECTION |
| 3 | Delete-change retention 90d | Ngụ ý client có 90 ngày để delta-resync | **Sai.** Pruning chỉ cắt **prefix liên tục**; `pruned_through_seq` vẫn quyết định `RESYNC_REQUIRED`. Không có bảo đảm 90 ngày delta | 🔴 CORRECTION |
| 4 | Client version guard | `incoming.version <= cached.version → ignore` | **Không đủ và bất khả thi cho delete** — delete-change **không mang `version`**. Ordering authority phải là **`change_seq`** + **client delete marker** | 🔴 CORRECTION (blocker) |
| 5 | `last_change_seq` | Chỉ thêm cột | **Legacy semantics đầy đủ**: NULL ≠ 0, cấm backfill đoán, quy tắc API/ETag | Bổ sung |
| 6 | Client RAM "50–100 object" | Tuyên bố như đã đạt | **Chưa được chứng minh** — client hiện bind `List<WaybillDbModel>` đầy đủ vào `DataGridView`, **không** dùng VirtualMode | 🔴 CORRECTION |
| 7 | Cursor vs view cache | Gộp chung "persist cache → persist cursor" | **Tách hai khái niệm**: replication state vs disposable view cache. Page refetch fail **không** làm cursor rollback | Sửa |
| 8 | Reopen khi projection absent | Chỉ có case "đã purge → 410" | **Thêm case**: projection absent **+ tombstone absent** → semantics riêng, **cấm tự tạo projection** | Bổ sung |
| 9 | History cache | Một file/waybill | **Phân trang**: `history/{siteId}/{waybillNo}/{historyCursorHash}.json` | Đơn giản hoá |
| 10 | Safety Poll | 10s | **+ jitter ngẫu nhiên, một logical loop/site**; reconnect **không** spawn loop mới | Bổ sung |
| 11 | Baseline perf | Gộp chung | **Tách benchmark interactive vs bulk** | Sửa |
| 12 | Migration preflight | table/column/type/nullability/default | **+ index + constraint**; **+ phát hiện invalid index** sau `CONCURRENTLY` fail | Bổ sung |
| 13 | Hard-cut LocalDb | Gate `outbox = 0` | **5 điều kiện** (freeze → in-flight 0 → outbox 0 → retry queue 0 → pending local changes 0) | Làm chặt |
| 14 | Rollback P1 | "tắt terminal guard = rollback" | **Sai khi đã có terminal/tombstone state** — phân biệt 2 trường hợp | 🔴 CORRECTION |
| 15 | Cache key | siteId + queryHash + pageCursorHash | **+ `cacheSchemaVersion`** | Bổ sung |
| 16 | Assumptions | Rải rác | **§28: danh sách giả định BỊ CẤM** | Bổ sung |

---

## §1. Baseline kiến trúc — KHOÁ (không đổi trong v4.6)

1. **PostgreSQL = Source of Truth.**
2. **Client không truy cập PostgreSQL.**
3. **Redis không phải dependency** của bất kỳ phase bắt buộc nào.
4. **SignalR chỉ là advisory doorbell** (không payload nghiệp vụ).
5. **Safety Poll là correctness backstop.**
6. **Client Disk-First.**
7. **`site_change_counters FOR UPDATE` là serialize point hiện tại** — giữ nguyên.
8. **Existing idempotency giữ nguyên.**
9. **Existing lease fencing giữ nguyên ở `/jms/ingest`.**
10. **`/jms/observations` là interactive multi-writer, KHÔNG yêu cầu leader term.**
11. **Snapshot / change-feed / idempotency / retention đã đúng → REUSE, không rewrite.**
12. **Không chuyển per-waybill concurrency ở giai đoạn đầu.**

---

## §2. 🔴 Semantics hai đồng hồ `event_occurred_at` và `ingested_at` (ĐÃ KIỂM CODE)

### 2.1 Sự thật đã xác minh

| Câu hỏi | Câu trả lời (bằng chứng) |
|---|---|
| `event_occurred_at` là gì? | **Source business event time.** Ghi tường minh từ `ScanTimeParser.Parse(input.ScanTime)` → tham số `@occurred_at` trong `IngestRepository.InsertEventAsync`. |
| `ingested_at` được tạo ở đâu? | ✅ **VERIFIED: KHÔNG ở tầng application.** `grep ingested_at src/AutoJMS.DataHub.Api` → **0 kết quả**. Cột **không** nằm trong danh sách INSERT của `InsertEventAsync`. |
| Vậy giá trị đến từ đâu? | **DB default**: `001_core.sql:60` → `ingested_at timestamptz NOT NULL DEFAULT now()`. |
| `now()` trong PostgreSQL là gì? | **`transaction_timestamp()` = thời điểm BẮT ĐẦU transaction.** Không phải statement time, không phải `clock_timestamp()`, **không phải** application ingress time. |

### 2.2 Ba hệ quả bắt buộc ghi vào contract

**(a) `ingested_at` là giá trị PER-TRANSACTION, không phải per-event.**
`now()` là hằng số trong một transaction ⇒ **toàn bộ tối đa 200 observation trong một bulk request có `ingested_at` GIỐNG HỆT NHAU.**

**(b) ⛔ CẤM dùng `ingested_at` làm tie-break per-event.**
Vì (a), mọi thiết kế kiểu `source_event_at → received_at → server_seq` **không hoạt động** trên bulk: tất cả item hoà nhau ở nấc thứ hai. Nếu cần tie-break thứ hai, dùng `id` (server sequence, đơn điệu per-event) — **không** dùng `ingested_at`.

**(c) `ingested_at` KHÔNG trôi theo thời gian chờ lock.**
Transaction bắt đầu **trước** `site_change_counters FOR UPDATE`, nên giá trị được chốt trước khi chờ khoá. Đây là **thuộc tính tốt nhưng do thứ tự tình cờ**, không phải thiết kế có chủ đích ⇒ phải ghi lại để không ai vô tình phá khi đổi thứ tự.

### 2.3 Reducer — chỉ MỘT đồng hồ

✅ **VERIFIED**: `ProjectionReducer.IsWinner` so sánh **`EventOccurredAt`**, tie-break bằng **`EventFingerprint`** (ordinal). Nó **không** dùng `ingested_at`.
⇒ **Hiện tại KHÔNG có vấn đề hai clock.** ⛔ **CẤM** đưa đồng hồ thứ hai vào reducer mà không bump `reducer_version` và không ghi rõ tác động lên rebuild/replay.

### 2.4 Chiến lược tương thích (nếu tương lai cần app-ingress clock)

| Lựa chọn | Đánh giá |
|---|---|
| Đổi ý nghĩa cột `ingested_at` hiện có | ⛔ **CẤM.** Sẽ **silently reinterpret** giá trị legacy — không phân biệt được hàng cũ (transaction time) với hàng mới (ingress time). |
| Thêm cột mới nullable (ví dụ `api_received_at`), chỉ điền từ hàng mới | ✅ Cách duy nhất được phép. NULL = "hàng có trước khi cột tồn tại", **không** phải 0/epoch. |

**v4.6 quyết định: KHÔNG thêm cột clock mới.** Giữ nguyên hai cột hiện có với semantics đã ghi ở §2.1.

---

## §3. `last_change_seq` — Legacy semantics

| Trường hợp | Quy định |
|---|---|
| Row **mới** (được ghi sau khi cột tồn tại và code đã set) | **Luôn có giá trị**, = `change_seq` của change gần nhất do chính transaction đó phát ra |
| Row **legacy** (có trước migration) | **NULL** |
| Diễn giải NULL | ⛔ **NULL KHÔNG được coi là 0.** NULL = *"không biết — có trước khi cột tồn tại"*. Coi NULL là 0 sẽ khiến client tưởng row chưa từng đổi. |
| Backfill | ⛔ **CẤM backfill đoán.** Không suy ra từ `dashboard_changes` vì change feed bị prune (14d + prefix rule §4) ⇒ dữ liệu **không đủ** để chắc chắn. **Không** chạy full-table migration nặng. |
| API / ETag | Khi `last_change_seq IS NULL`: **CẤM** bịa giá trị. API trả `lastChangeSeq: null`. ETag **không** được suy ra từ `last_change_seq` khi NULL — phải fallback sang `version` (luôn `NOT NULL DEFAULT 1`). |
| Client | Phải xử lý `null` như "unknown", không như "cũ nhất". |

---

## §4. 🔴 Change Feed Retention & Resync Semantics (ĐÃ KIỂM CODE)

### 4.1 Cơ chế thật (`RetentionRepository.DeleteChangesAsync`)

✅ **VERIFIED** — comment trong code nói rõ:
> *"Tombstones are held to their own, much longer clock. The cost is that pruning removes only a **contiguous prefix**, so a **retained tombstone pins every later row of that site's feed until it expires**; that is accepted deliberately, because an ordinary change a client missed is recoverable from a snapshot and a deletion is not."*

Cụ thể: pruning tính `first_recent_seq` = `min(change_seq)` của các row **phải giữ lại**, rồi chỉ xoá **prefix** phía dưới mốc đó, và đẩy `pruned_through_seq` lên mốc đó. Delete-change dùng `tombstoneRetention` riêng; change thường dùng `retention_policies('dashboard_changes')`.

### 4.2 ⛔ Cách diễn đạt BỊ CẤM và cách diễn đạt ĐÚNG

| ⛔ CẤM nói | ✅ Phải nói |
|---|---|
| "delete retention = 90d nên client có 90 ngày để delta-resync" | "**Cửa sổ delta recovery = `(pruned_through_seq, change_seq hiện tại]`**, không hơn." |
| "delete-change còn thì cursor cũ vẫn dùng được" | "`pruned_through_seq` là **thẩm quyền duy nhất**. Cursor ≤ `pruned_through_seq` → **`RESYNC_REQUIRED`**, kể cả khi delete-change cũ vẫn còn trong bảng." |
| "retention của delete mở rộng cửa sổ hồi phục" | "Một delete-change được giữ lại **ghim** prefix, nên cửa sổ **có thể** dài hơn 14 ngày — đó là **hệ quả phát sinh**, **không phải bảo đảm**, và không được thiết kế dựa vào nó." |

### 4.3 Đường resync chính
**Snapshot** (`/projections/snapshot`) là đường resync chính. Delta feed chỉ là tối ưu cho client còn trong cửa sổ.

---

## §5. Change Sequence Writer — giữ nguyên, chỉ thêm đo lường

**Giữ nguyên** hành vi counter-lock hiện có. **Không refactor sâu.**

| Quy định | Nội dung |
|---|---|
| Discipline | Mọi path tạo `dashboard_changes` phải dùng **cùng** allocation discipline: lấy `site_change_counters FOR UPDATE` **trước**, cấp seq, insert change, cập nhật counter — **trong cùng transaction**. ✅ Hiện `IngestRepository` và `RetentionRepository` đều đã đúng. |
| ⛔ Cấm | Thay đổi `site_change_counters` (schema hoặc cách khoá) · thay đổi execution order · thay đổi allocation semantics |
| Wrapper | Nếu introduce wrapper, wrapper **chỉ được bọc logic hiện có**, không đổi thứ tự thực thi. Refactor sâu chỉ sau khi acceptance test pass. |

**Metric bắt buộc**: `counter_lock_wait` (p50/p95/max), đo riêng cho **interactive** và **bulk** (§18).

---

## §6. ⛔ Terminal Policy — Fail-Closed cho tới khi Owner ký OD-1

Cho tới khi OD-1 được ký:
- **Không** scan type nào tự động là terminal.
- **Không** seed terminal code (⛔ đặc biệt cấm `9001`/`9002`/`9004` — chưa có bằng chứng từ payload JMS thật; seed hiện tại `002` chỉ phân loại `98` inventory và `110` state_transition).
- `is_terminal` giữ `false`.
- **Không** tạo tombstone.
- **Không** activate projection purge policy.

**P1 chỉ được phép**: thêm schema, thêm framework/guard **capability** (đường dẫn code tồn tại nhưng không kích hoạt).
⛔ **CẤM implement guessed terminal codes** dưới mọi hình thức, kể cả "tạm để test".

---

## §7. Terminal Lifecycle (khoá hoàn toàn)

**Normal terminal**
```
is_terminal          = true
terminal_at          = <policy-defined clock — OD-6>
terminal_state_code  = <incoming terminal code>
version              ++
change_seq           ++      → dashboard_changes(operation='upsert')
```

**Reopen**
```
is_terminal          = false
terminal_at          = NULL
terminal_state_code  = NULL
version              ++
change_seq           ++      → dashboard_changes(operation='upsert')
```

**Terminal again (sau reopen)**
Metadata terminal **được tạo lại hoàn toàn** từ event terminal mới.
⛔ **CẤM** giữ lại `terminal_at` cũ. Lịch sử nằm ở event store + audit log, **không** ở projection.

---

## §8. Reopen Retry Safety

```
POST /api/v1/sites/{siteId}/waybills/{waybillNo}/reopen
Header: Idempotency-Key (BẮT BUỘC, 8–128)
Body:   { "reason": "<bắt buộc>" }
```

| Tình huống | Hành vi bắt buộc |
|---|---|
| same key + same request | **replay exact response** (status + body) |
| same key + different body | **409 IDEMPOTENCY_KEY_REUSED** |
| duplicate retry | **KHÔNG** bump `version` lần 2 · **KHÔNG** cấp `change_seq` lần 2 · **KHÔNG** thêm dòng `dashboard_changes` |
| projection absent **+ tombstone present** | **410 `PROJECTION_ALREADY_PURGED`** |
| projection absent **+ tombstone absent** | **404 `PROJECTION_NOT_FOUND`** — ⛔ **CẤM tự tạo projection**. Đây là trạng thái "chưa từng thấy hoặc đã mất ngoài quy trình purge", phải báo cáo, không tự sửa. |
| actor ghi vào audit | **Từ authentication principal.** ⛔ CẤM lấy từ body/query/header do client khai. Client chỉ gửi `reason`. |
| authz | Admin/operator token — ⛔ **KHÔNG** phải `DeviceCapability` (enum chỉ có `ReadSiteData`, `WriteSiteData`; cả 3 role đều giữ cả hai ⇒ capability không phân biệt được admin) |

---

## §9. ⛔ Historical Replay

**Không implement endpoint khi chưa có archive source (OD-7).**

Khi đã có source, workflow bắt buộc theo đúng thứ tự:
```
archive → validate → dry-run → rebuild → audit
```
⛔ **CẤM**: đi qua normal ingest · boolean `force_rebuild_tombstoned` · biến tombstoned waybill thành live projection ngoài workflow rebuild explicit.

---

## §10. 🔴 BLOCKER — Client Cache: Ordering Authority & Delete Marker

### 10.1 Vì sao version guard KHÔNG đủ (bằng chứng code)

✅ **VERIFIED**: delete-change **không mang `version`**. `RetentionRepository` ghi body = `jsonb_build_object('waybill_no', ...)`; client đọc key từ `entityKey` vì *"A tombstone's body carries only the key"* (`DataHubClient`).
⇒ **Guard chỉ dựa trên `version` là bất khả thi cho delete**, không chỉ là "chưa đủ".

Thêm nữa: `ProjectChangeItems` hiện chỉ collapse **trong phạm vi MỘT page**. Không có guard per-entity xuyên page/xuyên replay.

### 10.2 Quy định khoá

**Ordering authority cho việc áp change vào cache = `change_seq`** (đơn điệu per-site, và `/changes` đã trả theo đúng thứ tự đó).

| Quy tắc | Nội dung |
|---|---|
| R1 | Client lưu **per-entity** `lastAppliedChangeSeq`. |
| R2 | Với **mọi** change (upsert **và** delete): `if change.changeSeq <= entity.lastAppliedChangeSeq → ignore`. |
| R3 | Khi áp delete: **không xoá trắng entity** khỏi cache. Ghi **delete marker**: `{ deleted: true, lastAppliedChangeSeq: <seq của delete> }`. |
| R4 | Upsert đến sau với `changeSeq <= marker.lastAppliedChangeSeq` → **ignore** (đây chính là ca resurrect). |
| R5 | Delete marker có **TTL riêng** (khuyến nghị ≥ thời gian delta window) rồi mới được dọn; dọn sớm sẽ mở lại lỗ hổng resurrect. |
| R6 | `version` **vẫn dùng** — nhưng cho việc **reconcile bản đọc đầy đủ** (detail fetch / snapshot), không phải cho thứ tự áp delta. |

### 10.3 Hai khái niệm KHÁC NHAU — cấm nhầm lẫn

| | Server tombstone | Client delete marker |
|---|---|---|
| Ở đâu | `waybill_tombstones` (PostgreSQL) | Disk cache của client |
| Mục đích | **Anti-resurrection phía server** — chặn event tạo lại projection | **Chống resurrect cache cục bộ** khi change bị replay/đảo thứ tự |
| Vòng đời | ≥ 2 năm (OD-2) | TTL cục bộ (R5) |
| Ai tạo | Retention purge | Client khi áp delete-change |

⛔ **CẤM** dùng cái này để suy ra cái kia.

---

## §11. Client Cursor vs View Cache — tách hai khái niệm

| Khái niệm | Bản chất | Quy tắc |
|---|---|---|
| **Replication state** (`changeCursor`) | Trạng thái đồng bộ **bền vững** | Chỉ tiến khi change đã được **persist/process thành công** vào replication store |
| **View cache** (dashboard current page) | **Disposable** | Có thể stale/mất bất cứ lúc nào mà **không** ảnh hưởng tính đúng đắn |

**Quy định bắt buộc:**
- ⛔ **CẤM** yêu cầu dashboard page phải phản ánh mọi change trước khi cursor được advance.
- Nếu change đã persist/processed **nhưng** refetch current page **thất bại**:
  - `cursor` **vẫn** phản ánh replication progress (không rollback),
  - page được đánh dấu **`stale`**,
  - lần đọc kế tiếp **bắt buộc refetch**.
- ⛔ **CẤM** rollback cursor chỉ vì view cache lỗi.

*(Ràng buộc §16.1 của v4.5 — "cursor không tiến quá trạng thái đĩa đã persist" — vẫn đúng, nhưng "trạng thái đĩa" ở đây nghĩa là **replication state**, không phải view cache.)*

---

## §12. Dashboard Cache

- **Chỉ** cache current page/query.
- Page cache **disposable**.
- Delta ảnh hưởng sort/filter → **mark stale** → **debounce 300–500ms** → **refetch current page**.
- ⛔ **CẤM** cố duy trì mọi cached page nhất quán bằng delta.
- Page khác chỉ tải lại khi user điều hướng tới.

**Cache key bắt buộc chứa đủ 4 thành phần:**
```
siteId + queryHash + pageCursorHash + cacheSchemaVersion
```
`cacheSchemaVersion` cho phép vô hiệu hoá toàn bộ cache cũ khi đổi format mà không cần migration client.

---

## §13. History Cache

⛔ **CẤM** một file history khổng lồ per waybill.
```
history/{siteId}/{waybillNo}/{historyCursorHash}.json
```
Chỉ cache **page được yêu cầu**. Quota/TTL áp dụng như disposable cache (§12).

---

## §14. 🔴 Client RAM Boundary (tuyên bố cũ CHƯA được chứng minh)

✅ **VERIFIED — hiện trạng**: client **không** dùng `DataGridView.VirtualMode`. `FullStackOperation.cs` bind trực tiếp `tabDash_dataGridView.DataSource = _lastFilteredDashRows` (một `List<WaybillDbModel>` đầy đủ), và thao tác sort tạo thêm bản sao đầy đủ (`.OrderBy(...).ToList()`).

⇒ ⛔ **CẤM tuyên bố "RAM chỉ giữ 50–100 object"** cho tới khi implementation chứng minh được.

**Yêu cầu contract (P3):**
- Chỉ **current visible page** được nạp vào UI.
- **Bounded presentation model** — có giới hạn trên rõ ràng.
- ⛔ Không `List`/`BindingList` chứa toàn bộ dashboard trong memory.
- ⛔ Không unbounded object cache.
- ⛔ Không synchronous disk I/O trên UI thread.
- Nếu dùng `DataGridView.VirtualMode` / `CellValueNeeded` → **ghi rõ trong thiết kế**; nếu không dùng thì phải nêu cơ chế bounded thay thế.

**Acceptance**: dataset remote lớn → chỉ current page vào UI → **đo** working set regression.

---

## §15. Disk Full

```
cache write fails
→ cursor KHÔNG advance quá trạng thái replication chưa persist
→ view cache đánh dấu unavailable/stale
→ fallback đọc remote
→ application KHÔNG crash
```

> **Nguyên tắc**: **Disk cache failure MUST NOT become business-data failure.**
> Mất cache = mất tốc độ. Mất cache **không được** = mất/ sai dữ liệu nghiệp vụ.

---

## §16. Site Switching

Thứ tự bắt buộc:
1. **Stop** xử lý update của site cũ
2. **Unsubscribe** SignalR group site cũ
3. **Subscribe** SignalR group site mới
4. **Load cursor** site mới
5. **Invalidate / load** current query
6. **Reject** mọi event của site cũ đến sau thời điểm switch

Cache paths: **site-scoped** (§12, §13). Cursor: **multi-site** (`cursor.json` tách nhánh theo `siteId`).

---

## §17. Safety Poll

| Thuộc tính | Quy định |
|---|---|
| Base interval | **10s** |
| Jitter | **Bắt buộc có jitter ngẫu nhiên** (chống thundering herd khi nhiều client cùng khởi động / cùng reconnect) |
| Số loop | **Đúng MỘT logical polling loop mỗi site** |
| SignalR reconnect | ⛔ **CẤM spawn thêm polling loop.** Reconnect chỉ được **đánh thức** loop hiện có |

**SLO**

| Chế độ | Cam kết |
|---|---|
| **Healthy** (SignalR hoạt động) | E2E propagation **P95 < 5s** |
| **Degraded** (SignalR không khả dụng) | **Correctness được đảm bảo**; freshness **≤ poll bound** |

⛔ **CẤM** tuyên bố `P95 < 5s` ở chế độ degraded.

---

## §18. P0 Baseline Performance — tách interactive và bulk

⛔ **CẤM gộp chung.** Đo **riêng biệt**:

| Nhóm | Endpoint | Tải |
|---|---|---|
| **Interactive** | `POST /api/v1/sites/{siteId}/jms/observations` | 10 concurrent · 50 concurrent |
| **Bulk** | `POST /api/v1/sites/{siteId}/jms/ingest` | 10 concurrent · 50 concurrent |

Điều kiện chung: **cùng site**, **khác waybill**.

**Metrics bắt buộc (mỗi nhóm)**: `counter_lock_wait` · `transaction_p95` · `commit_p95` · `throughput` · `error_rate`.

Baseline này là **mốc so sánh cho P1 và P7**. Không có baseline → không được vào P1.

---

## §19. Migration Safety

### 19.1 Preflight — 7 đối tượng phải kiểm
`table` · `column` · `type` · `nullability` · `default` · `index` · `constraint`

Mỗi đối tượng phân loại: `missing` (được phép áp) · `exists & matches` (skip **riêng đối tượng đó**, ghi log) · `mismatch` (type / nullability / default / definition khác kỳ vọng).

> ⛔ Bất kỳ `mismatch` nào → **STOP + REPORT** chi tiết per-object.
> ⛔ **CẤM** "một đối tượng đã tồn tại → skip toàn bộ migration".
> ⛔ **CẤM** auto-resolve partial state.

Nguồn: `information_schema.tables`, `information_schema.columns` (`data_type`, `is_nullable`, `column_default`), `pg_indexes` / `pg_index`, `information_schema.table_constraints`.

### 19.2 `CREATE INDEX CONCURRENTLY`
- Chạy trong migration **riêng, ngoài transaction** (`*_notx.sql`).
- ⛔ **`IF NOT EXISTS` KHÔNG phải bằng chứng index dùng được.** Một `CONCURRENTLY` thất bại để lại index **invalid** nhưng vẫn "tồn tại".
- **Bắt buộc phát hiện invalid index**: kiểm `pg_index.indisvalid = false` cho index vừa tạo.
- **Thủ tục khi phát hiện invalid**: `DROP INDEX` (dạng concurrently) → **retry** → nếu vẫn fail thì **STOP + REPORT**, không bỏ qua.

### 19.3 Phạm vi schema delta (chỉ ngần này)
| Bảng | Thêm | Ghi chú |
|---|---|---|
| `waybill_scan_events` | `event_kind`, `reducer_version`, `normalizer_version`, `source_schema_version` | tất cả có default an toàn |
| `waybill_projections` | `is_terminal`, `terminal_at`, `terminal_state_code`, `last_change_seq` | `is_terminal NOT NULL DEFAULT false`; `last_change_seq` **nullable** (§3) |
| bảng mới | `waybill_tombstones` | PK `(site_id, waybill_no)` |
| index `_notx` | `ix_waybill_projections_terminal_retention` partial `WHERE is_terminal = true` | `CONCURRENTLY` + kiểm `indisvalid` |

⛔ **CẤM đụng**: `event_occurred_at`, `ingested_at` (§2) · `fingerprint_version` (`001_core.sql:58`) · PK `jms_event_policies` (`001_core.sql:129`) · `site_change_counters`.

---

## §20. Backup / Restore & Infra Gate (P0 hard gate)

**Thứ tự bắt buộc:**
1. **Backup** (`scripts/backup-postgres.ps1` — đã có)
2. **Restore** vào instance tạm (`scripts/restore-postgres.ps1` — đã có)
3. **API smoke test trên DB đã restore** (`scripts/smoke-test.sh` — đã có, 10 bước end-to-end)
4. **Network**:
   - `Internet → DB:5432` = **BLOCKED**
   - `API → DB` = **PASS** (qua WireGuard)
   - **TLS VerifyFull** = **PASS**, hostname khớp

> ⛔ Fail bất kỳ bước nào → **STOP RELEASE**.
> ⛔ **CẤM migration trước khi backup được verify.**

PostgreSQL container không public trực tiếp; nếu cần bind thì bind nội bộ (vd `10.0.0.2:5432:5432`), và **firewall vẫn là lớp kiểm soát độc lập**.

**RPO / RTO**: ghi **số thật**. RPO = khoảng cách giữa hai lần backup thực tế (⛔ PENDING OD-8). RTO = **đo** ở P0. ⛔ Không tuyên bố tốt hơn số đo được.

---

## §21. Hard-Cut LocalDb — gate 5 điều kiện

```
freeze new local writes
        ↓
wait in-flight operations = 0
        ↓
outbox = 0
        ↓
retry queue = 0
        ↓
pending local business changes = 0
        ↓
disable LocalDb writes
        ↓
enable remote-only
```

⛔ **CẤM** chỉ kiểm `outbox = 0`.

**Hard-cut là MỘT CHIỀU.** Sau hard-cut:
- Rollback chỉ được coi là **"remote-only rollback"** (quay lại phiên bản app trước nhưng vẫn remote-only) **hoặc** **full restore từ backup**.
- ⛔ **CẤM giả định LocalDb có thể quay lại hoạt động bình thường** sau hard-cut.

---

## §22. 🔴 Rollback P1 — phân biệt hai trường hợp

⛔ **CẤM tuyên bố "tắt terminal guard = full rollback".** Điều đó chỉ đúng khi chưa có state mới.

| Trường hợp | Rollback được phép |
|---|---|
| **Chưa có terminal/tombstone state nào được tạo** | Feature flag off → hành vi trở lại như trước. An toàn. |
| **Đã có terminal/tombstone state** | ⛔ **CẤM** xoá/đảo schema. Rollback ứng dụng **phải preserve state mới an toàn**. Nếu cần quay về hành vi cũ thì phải có **explicit compatibility behavior** (ví dụ: bỏ qua `is_terminal` khi đọc, nhưng **không** xoá dữ liệu). |

Lý do: `is_terminal`/`terminal_at`/`waybill_tombstones` là **dữ liệu nghiệp vụ mới**, không phải cờ tạm. Tombstone đặc biệt: xoá tombstone = mở lại khả năng resurrect.

---

## §23. Retention

- **Giữ worker hiện có.** ⛔ **CẤM viết worker thứ hai.**
- **Chỉ đổi predicate** của projection purge thành:
  ```
  is_terminal = true
  AND terminal_at <= <retention cutoff>
  ```
  và **thêm tombstone trong cùng transaction** với delete-change + xoá projection.
- **Active projection tuyệt đối không purge.**
- ⛔ **CẤM seed `retention_policies('waybill_projections')`** trước khi **tất cả** đã pass: terminal schema · anti-resurrection · tombstone · delete-change.

> ⚠️ **Rủi ro vận hành cụ thể**: predicate hiện tại của `DeleteProjectionsAsync` là `p.updated_at < now() - delete_after` — **theo bất hoạt, không theo terminal**. Nó đang ngủ **chỉ vì thiếu policy row**. Một câu `INSERT` là đủ kích hoạt xoá projection còn sống khi chưa có tombstone.

---

## §24. Phase Gates

| Phase | Nội dung | ⛔ Gate |
|---|---|---|
| **P0** Freeze + Backup + Infra + OD + Baseline | schema inventory (§19.1) · backup→restore→smoke (§20) · network (§20) · OD-1..OD-8 · **baseline interactive + bulk riêng** (§18) · **không đổi code** | Cả 4 bước §20 PASS · preflight không mismatch · Owner ký toàn bộ OD · có 2 bộ baseline |
| **P1** Server Safety | terminal columns · tombstones · horizon guardrail + fail-fast · anti-resurrection guard · terminal lifecycle · reopen · thay đổi ingest **tối thiểu**. Không Redis. Không client. Không rewrite snapshot/change-feed | Build Release PASS · test P1 PASS · throughput **không xấu hơn baseline** (§18) |
| **P2** Server Read | `/waybills` · `/detail` · `/history` · ETag · **reuse** `/changes` + snapshot | Build PASS · test P2 PASS · ETag đúng semantics kể cả `last_change_seq = NULL` (§3) |
| **P3** Client Remote Read + Disk Cache | disk cache site-scoped · **delete marker + changeSeq guard** (§10) · cursor/view tách biệt (§11) · multi-site cursor · site-scoped SignalR · async I/O · bounded RAM (§14) | Test AT-CL* PASS · **đo** working set · disk-full PASS |
| **P4** Write Cutover | interactive (no fencing) + bulk (fenced) · Normalizer · server reducer · **gate 5 điều kiện** (§21) | 5 điều kiện = 0 · shadow-mode khớp |
| **P5** SignalR | advisory doorbell · Safety Poll + jitter · một loop/site · no payload | Test mất kết nối + reconnect không spawn loop PASS |
| **P6** Retention | đổi predicate · tombstone · delete-change · batch purge · **rồi mới** seed policy | Test AT-RT* PASS |
| **P7** Production Validation | 50 concurrent (interactive + bulk riêng) · 2/5/10 client · crash · network loss · SignalR loss · disk full · DB restart · cache replay · retention race · site switching | Toàn bộ PASS · đạt SLO §17 |
| **R1** *(Optional)* Redis | **Chỉ khi** P7 chứng minh PostgreSQL cần tăng tốc. Bắt đầu **chỉ Detail Pointer**. ⛔ CẤM Dashboard Epoch | Có số đo chứng minh |

---

## §25. Owner Decisions — mẫu ký (vá các mục đang block)

> Mỗi mục dưới đây trình bày **lựa chọn + hệ quả + khuyến nghị**. Khuyến nghị là **đề xuất kỹ thuật, không phải quyết định**. Owner ký vào cột cuối.

| # | Quyết định | Lựa chọn & hệ quả | Khuyến nghị (không phải quyết định) | Owner ký |
|---|---|---|---|---|
| **OD-1** | Danh sách terminal scan code | Chưa có bằng chứng nào. Seed hiện chỉ có `98`=inventory, `110`=state_transition. Không ký → hệ thống fail-closed (§6), P1 chỉ thêm schema | Đối chiếu payload JMS thật rồi liệt kê; lưu dạng policy versioned trong bảng | ☐ |
| **OD-2** | Horizon | ingest **45d** · event/dedupe **60d** (giữ nguyên) · terminal purge **90d** · tombstone **≥2 năm**. Bất biến: `ingest < event_retention` | Chấp nhận bộ số trên; nếu đổi thì giữ bất biến | ☐ |
| **OD-3** | Reopen sau purge | Option A (chỉ reopen khi projection còn sống) + workflow rebuild riêng | Option A | ☐ |
| **OD-4** | DPAPI cho disk cache | Có PII (tên/địa chỉ/SĐT) → DPAPI per-user; chỉ mã đơn + trạng thái → ACL Windows đủ | Quyết theo mức PII thực tế của payload | ☐ |
| **OD-5** | Fingerprint policy | Giữ code-driven `EventFingerprintV1` vs chuyển table-driven | **Giữ code-driven**; khi có payload thật → cutover `fingerprint_version = 2` (không tái dùng số 1) | ☐ |
| **OD-6** | Mốc tính terminal retention | **A** = `source_event_at`: late terminal có thể đủ tuổi purge **ngay khi vừa nhận** (cần test `AT-TERM-LATE`) · **B** = server observed time: không purge-ngay, nhưng "tuổi" lệch khỏi thời gian nghiệp vụ | Nếu ưu tiên an toàn vận hành → **B**. Nếu ưu tiên đúng nghiệp vụ → **A** + bắt buộc test | ☐ |
| **OD-7** | Archive source cho Historical Replay | object storage · backup/archive · JMS export · nguồn khác. **Không có → không implement** (§9) | Hoãn cho tới khi có nhu cầu thật | ☐ |
| **OD-8** | Tần suất backup (RPO) | Owner ghi tần suất backup thật đang chạy; RTO **đo** ở P0 | Ghi số thật, không đặt mục tiêu vượt khả năng | ☐ |

---

## §26. Brownfield — ĐÃ CÓ TRONG CODE, KHÔNG ĐƯỢC REWRITE

| Hạng mục | Vị trí | |
|---|---|---|
| Tách interactive vs leader-fenced (`requireFence` false/true) | `IngestEndpoints.cs` | ✅ |
| Ingest 1-transaction, bulk ≤ 200, all-or-nothing | `IngestRepository.IngestAsync` | ✅ |
| Idempotency đầy đủ (reserve `status_code=0`, replay, `KEY_REUSED`, `IN_PROGRESS`) | `IngestRepository` | ✅ |
| Lease fencing 3 chốt, `clock_timestamp()` | `IngestRepository.CheckFenceAsync` | ✅ |
| Dedupe `UNIQUE(site_id,event_fingerprint)` + `ON CONFLICT DO NOTHING RETURNING id` | `001_core` + `InsertEventAsync` | ✅ |
| Cấp phát `change_seq` dưới `FOR UPDATE` (ingest **và** retention cùng lock order) | `IngestRepository`, `RetentionRepository` | ✅ |
| Snapshot `RepeatableRead` + `LIMIT maxRows+1` + `truncated` + `asOfChangeSeq` | `ChangeRepository.ReadSnapshotAsync` | ✅ |
| Delta feed + `pruned_through_seq` + `RESYNC_REQUIRED` | `ChangeRepository`, `ChangeCursorWindow` | ✅ |
| Prefix-only pruning + tombstone clock riêng | `RetentionRepository.DeleteChangesAsync` | ✅ |
| Projection purge + delete-change + counter-lock (CTE nguyên tử) | `RetentionRepository.DeleteProjectionsAsync` | ✅ **đang ngủ** |
| Client: routing `operation='delete'`, collapse last-op-wins **trong một page** | `DataHubClient.ProjectChangeItems` | ✅ *(thiếu guard xuyên page — §10)* |
| Authz: token là nguồn quyền, `siteId` URL chỉ đối chiếu | `TenantAuthorizationEvaluator` | ✅ |
| Pool / statement timeout / idle timeout cấu hình được | `PostgresDataSource`, `DataHubRuntimeOptions` | ✅ |
| Backup / Restore / Smoke / Apply-migrations | `backend/datahub/scripts/*` | ✅ |

---

## §27. Acceptance Matrix

`T`: **I**=integration (DB thật) · **U**=unit · **M**=manual/ops · **L**=load/chaos

### Clock & reducer (§2)
| ID | Phase | Kịch bản | Kỳ vọng | T |
|---|---|---|---|---|
| AT-CK1 | P0 | Bulk 200 item trong 1 request | **Mọi** row có `ingested_at` **giống hệt nhau** (transaction time) | I |
| AT-CK2 | P0 | Ingest bị chờ counter lock | `ingested_at` **không** trôi theo thời gian chờ | I |
| AT-CK3 | P1 | Reducer | Chỉ dùng `event_occurred_at` + fingerprint; **không** dùng `ingested_at` | U |

### `last_change_seq` (§3)
| AT-LS1 | P2 | Legacy projection (`last_change_seq IS NULL`) | API trả `null`, **không** trả 0; ETag fallback sang `version` | I |
| AT-LS2 | P2 | Projection mới | `last_change_seq` có giá trị = seq của change vừa phát | I |
| AT-LS3 | P1 | Migration | **Không** chạy backfill; không full-table scan nặng | M |

### Change feed & resync (§4)
| AT-CF1 | P1 | Cursor cũ · normal changes đã prune · delete-change **vẫn còn** | **`RESYNC_REQUIRED`** (không vì delete-change còn mà cho delta) | I |
| AT-CF2 | P1 | Delete-change được giữ lại | Prefix **không** prune vượt qua nó; `pruned_through_seq` dừng đúng mốc | I |
| AT-CF3 | P2 | Client sau `RESYNC_REQUIRED` | Đi đường snapshot, phục hồi đầy đủ | I |

### Change sequence (§5)
| AT-CS1 | P1 | ingest change + retention delete change + reopen change | Cấp seq đúng, đơn điệu, không trùng, không nhảy qua seq chưa commit | I |
| AT-CS2 | P0/P7 | `counter_lock_wait` | Đo riêng interactive và bulk (§18) | L |

### Terminal (§6, §7)
| AT-T1 | P1 | ⛔ OD-1 chưa ký | Không code nào terminal · `is_terminal=false` · không tombstone · không purge | I |
| AT-T2 | P1 | terminal event | metadata set đúng · `version++` · 1 change | I |
| AT-T3 | P1 | **Chuỗi**: terminal → reopen → old event → new terminal → retention | metadata tạo lại từ event mới · `terminal_at` cũ **không** còn · retention dùng `terminal_at` **mới** | I |
| AT-TERM-LATE | P6 | *(chỉ khi OD-6 = A)* terminal đến muộn hơn cutoff | Hành vi purge-ngay được quan sát và chấp nhận có ý thức | I |

### Reopen (§8)
| AT-R1 | P1 | same key + same body ×2 | replay exact · `version` bump **đúng 1 lần** · **1** dòng change | I |
| AT-R2 | P1 | same key + different body | **409 IDEMPOTENCY_KEY_REUSED** | I |
| AT-R3 | P1 | projection absent + tombstone present | **410 PROJECTION_ALREADY_PURGED** | I |
| AT-R4 | P1 | projection absent + tombstone absent | **404 PROJECTION_NOT_FOUND**, **không** tạo projection | I |
| AT-R5 | P1 | audit | actor = principal, không lấy từ body | I |
| AT-R6 | P1 | device token thường gọi reopen | **403** | I |

### Client cache — BLOCKER (§10)
| AT-CL1 | P3 | **1** upsert v10 → **2** delete v11 → **3** replay upsert v10 | **Client PHẢI vẫn ở trạng thái deleted** | I |
| AT-CL2 | P3 | Áp cùng batch 2 lần (upsert) | Idempotent, không đổi kết quả | U |
| AT-CL3 | P3 | Delete marker TTL chưa hết | Upsert cũ vẫn bị ignore | U |
| AT-CL4 | P3 | Site A + Site B **cùng mã vận đơn**, switch A→B→A | Không contamination | I |
| AT-CL5 | P3 | Crash giữa ghi cache và ghi cursor | Replay an toàn; không resurrect | I |
| AT-CL6 | P3 | **Disk full** | cache fail · cursor không vượt replication chưa persist · view stale · fallback remote · **không crash** | I |
| AT-CL7 | P3 | Refetch current page **thất bại** | cursor **không rollback** · page `stale` · lần đọc sau refetch | I |
| AT-CL8 | P3 | Dataset remote lớn | Chỉ current page vào UI · **đo** working set | M |

### SignalR / Poll (§17)
| AT-SP1 | P5 | SignalR reconnect nhiều lần | **Không** spawn thêm polling loop; vẫn đúng 1 loop/site | I |
| AT-SP2 | P5 | Nhiều client cùng khởi động | Poll có jitter, không đồng pha | L |
| AT-SP3 | P7 | SignalR tắt | Correctness giữ · freshness ≤ poll bound · **không** tuyên bố P95<5s | L |

### Retention (§23)
| AT-RT1 | P6 | Projection active, lâu không đổi | **KHÔNG BAO GIỜ** purge | I |
| AT-RT2 | P6 | Terminal đủ tuổi | tombstone + delete-change + xoá projection **cùng transaction** | I |
| AT-RT3 | P6 | Retention ‖ ingest cùng waybill | Không mồ côi, không deadlock | I |
| AT-RT4 | P6 | Retention ‖ reopen | Không xoá đơn vừa reopen | I |
| AT-RT5 | P0/P6 | Policy chưa seed | Purge **không chạy** | I |

### Migration / Infra (§19, §20)
| AT-MG1 | P0/P1 | Partial state (1 cột đã có, 1 chưa) | **STOP + REPORT** per-object, **không** skip toàn bộ | M |
| AT-MG2 | P1 | `CONCURRENTLY` fail | Phát hiện `indisvalid=false` → DROP → retry → nếu fail thì STOP | M |
| AT-MG3 | P0 | backup → restore → smoke trên DB restored | PASS cả 3; fail → STOP RELEASE | M |
| AT-MG4 | P0 | `Internet → DB:5432` / `API → DB` / TLS VerifyFull | **Multi-host:** BLOCKED / PASS / PASS — API↔DB qua WireGuard, `SSL Mode=VerifyFull`. **Single-host:** BLOCKED / PASS / **N/A** — không có chặng liên-máy để bọc TLS; thay bằng network `data` `internal: true` + `expose` (không `ports`) | M |

> AT-MG4 phụ thuộc tô-pô, nên `Kỳ vọng` chia hai nhánh chứ không có một đáp án chung. **`Internet → DB:5432` phải BLOCKED ở cả hai nhánh** — đó là yêu cầu bảo mật, không phải kết quả đo. WireGuard và `SSL Mode=VerifyFull` chỉ áp dụng khi API và DB nằm trên hai máy; đưa production về multi-host là đưa hai mục đó trở lại bắt buộc. Kết quả đã ghi nhận nằm ở `docs/review/p0-execution-runbook.vi.md` (G6), không ghi ở đây.

### Cutover (§21)
| AT-HC1 | P4 | Hard-cut khi retry queue > 0 | **Bị chặn** (không chỉ kiểm outbox) | I |
| AT-HC2 | P4 | Sau hard-cut | LocalDb **không** được coi là quay lại được; rollback = remote-only hoặc full restore | M |

### Perf baseline (§18)
| AT-PF1 | P0/P7 | Interactive 10 & 50 concurrent | Ghi đủ 5 metrics | L |
| AT-PF2 | P0/P7 | Bulk 10 & 50 concurrent | Ghi đủ 5 metrics; so baseline | L |

---

## §28. ⛔ Giả định KHÔNG được phép (explicit list)

Claude Code **không được** giả định bất kỳ điều nào sau đây. Mỗi dòng đã được kiểm và là **sai** hoặc **chưa chứng minh**:

1. ❌ `ingested_at` là application ingress time. → **Sai**: là transaction start time từ DB default.
2. ❌ `ingested_at` khác nhau giữa các event trong cùng một bulk request. → **Sai**: giống hệt nhau.
3. ❌ Có thể tie-break reducer bằng received time. → **Sai** trên bulk.
4. ❌ Delete-change mang `version`. → **Sai**: body chỉ có `waybill_no`.
5. ❌ Client có thể delta-resync trong 90 ngày vì delete retention 90 ngày. → **Sai**: `pruned_through_seq` quyết định.
6. ❌ `incoming.version <= cached.version` là đủ để chống resurrect. → **Sai**: không áp dụng được cho delete.
7. ❌ `last_change_seq = NULL` nghĩa là 0 / chưa từng đổi. → **Sai**: nghĩa là "không biết".
8. ❌ Client RAM chỉ giữ 50–100 object. → **Chưa chứng minh**: hiện bind full `List<WaybillDbModel>`.
9. ❌ `DataGridView` đang dùng VirtualMode. → **Sai**: không tìm thấy `VirtualMode`/`CellValueNeeded`.
10. ❌ Tồn tại capability `ReadWriteSiteData`. → **Sai**: enum chỉ có `ReadSiteData`, `WriteSiteData`.
11. ❌ `datahub_admin` là một `DeviceCapability`. → **Sai**: cả 3 role đều giữ cả hai capability.
12. ❌ `IF NOT EXISTS` chứng minh index dùng được. → **Sai**: index invalid vẫn "tồn tại".
13. ❌ `outbox = 0` là đủ để hard-cut LocalDb. → **Sai**: cần 5 điều kiện.
14. ❌ Tắt feature flag = full rollback P1. → **Sai** khi đã có terminal/tombstone state.
15. ❌ Projection purge hiện tại đã lọc theo terminal. → **Sai**: predicate hiện là `updated_at` (bất hoạt).
16. ❌ Retention purge chưa phát delete-change nên phải viết mới. → **Sai**: đã phát, đúng, trong CTE nguyên tử.
17. ❌ Snapshot chưa có `RepeatableRead` / `maxRows+1`. → **Sai**: đã có.
18. ❌ `fingerprint_version` chưa tồn tại. → **Sai**: `001_core.sql:58`.
19. ❌ PK `jms_event_policies` chỉ có `scan_type_code`. → **Sai**: đã composite.
20. ❌ Terminal scan code là 9001/9002/9004. → **Chưa chứng minh**: ⛔ PENDING OD-1.
21. ❌ Có thể tạo projection khi reopen mà projection không tồn tại. → **Cấm**.
22. ❌ Redis cần cho correctness. → **Sai**: không phải dependency.

---

## §29. Ràng buộc cuối cho Claude Code

1. ⛔ **CẤM** implement bất kỳ mục nào mang dấu **⛔** (§-1), dù diễn đạt là PENDING, BLOCKED hay CẤM.
2. ⛔ **CẤM** tự chọn business policy (đặc biệt OD-1, OD-6).
3. ⛔ **CẤM** gọi Redis là dependency; **CẤM** implement Dashboard Epoch.
4. ⛔ **CẤM** rewrite snapshot / idempotency / change-feed / retention đã đúng (§26).
5. ⛔ **CẤM** thay đổi allocation semantics của `change_seq` hoặc schema `site_change_counters`.
6. ⛔ **CẤM** đổi semantics của `event_occurred_at` / `ingested_at` (§2.4).
7. ⛔ **CẤM** dùng bất kỳ giả định nào ở §28.
8. Mọi thay đổi phải **additive**, **rollback-safe**, có gate ở §24.
9. **Nếu contract mâu thuẫn code thật → `STOP + REPORT`.** ⛔ CẤM tự hoà giải bằng cách đoán.
10. **Sau khi hoàn tất rà soát: chỉ đề xuất Owner duyệt P0. KHÔNG bắt đầu P1.**
