> ⛔ **SUPERSEDED bởi [streaming-v4.4-contract.vi.md](./streaming-v4.4-contract.vi.md)** (2026-09-05). Giữ lại chỉ để tra lịch sử quyết định. Lưu ý: finding **B5** trong bản review v4.2 là SAI về code — xem mục 0.2 của v4.4.

# Báo cáo Rà soát & Đánh giá Rủi ro — Kiến trúc Streaming v4.2

> **Phạm vi**: Đối chiếu `implementation_plan.md` (v4.2 Pre-Implementation Contract) + `walkthrough.md` với **codebase thật** trên `origin/main` (`D:\v1.2605.2(new-test)`).
> **Ngày**: 2026-09-05.
> **Người rà soát**: Claude (Cowork) — technical review, không thực thi code.
> **Đối chiếu code**: `backend/datahub/migrations/001..006`, `src/AutoJMS.DataHub.Api/{Infrastructure/IngestRepository.cs, Infrastructure/ChangeRepository.cs, Domain/ProjectionReducer.cs, Domain/EventFingerprintV1.cs, Endpoints/SyncEndpoints.cs, Program.cs}`, `src/AutoJMS/Data/DataHubClient.cs`.
> **Kết quả**: Xem [streaming-v4.3-plan.vi.md](./streaming-v4.3-plan.vi.md) — bản kế hoạch đã cải thiện, sẵn sàng để Owner duyệt.

---

## 0. Kết luận (Verdict)

**KHÔNG nên duyệt để triển khai nguyên trạng v4.2.** Bản v4.2 có phần khung tư duy tốt (advisory lock 64-bit, idempotency contract, terminal/tombstone, multi-site cursor), nhưng nó được **viết như một hệ thống greenfield**, trong khi thực tế đây là **brownfield** — pipeline ingest / dedupe / projection / change-feed / snapshot / retention / lease **đã chạy trên `main`**. Hệ quả:

1. Plan **bỏ sót** những cơ chế đang tồn tại và mang tính sống còn (cấp phát `change_seq`, khóa serialize per-site, lease fencing).
2. Plan **đặt tính đúng đắn lên Redis** — nhưng Redis **chưa hề tồn tại** trong code.
3. Plan **tái phát minh** những thứ đã làm xong (snapshot RepeatableRead, resync theo cursor pruned).
4. Một số **SQL/pseudo-code chạy sẽ lỗi** nếu Claude Code "làm y hệt hợp đồng".

Nếu giao cho Claude Code thực thi "chính xác 14 bước" như v4.2 tuyên bố, **rủi ro hồi quy (regression) một hệ thống đang hoạt động là cao**.

### Bảng tổng hợp mức độ

| Mức độ | Số lượng | Ý nghĩa |
|--------|:---:|---|
| 🔴 **Blocker** | 6 | Phải giải quyết **trước khi** duyệt. Gây mất dữ liệu, mất tính đúng đắn, hoặc không chạy được. |
| 🟠 **High** | 6 | Nợ kỹ thuật/hồi quy nghiêm trọng. Cần chốt hướng trước khi code. |
| 🟡 **Medium** | 7 | Tái phát minh, lãng phí, hoặc thiếu định nghĩa. Nên sửa trong v4.3. |
| ⚪ **Low/Nit** | 5 | Chi tiết cần làm rõ, ít rủi ro. |

---

## 1. Vấn đề nền tảng: Greenfield vs Brownfield

Điều quan trọng nhất không nằm ở một dòng SQL cụ thể, mà ở **khung tham chiếu**. Những gì hệ thống **đã có** (đã build, đã test, đang chạy):

- **Ingest 1 transaction/req**, bulk tối đa **200 observation/request**, mỗi request **một `Idempotency-Key`** hash toàn bộ body (`IngestRepository.IngestAsync`).
- **Serialize per-site** bằng `SELECT change_seq FROM site_change_counters ... FOR UPDATE` — đây chính là cơ chế chống race "hai first-observation cùng tạo projection", và là nơi **cấp phát `change_seq`** đơn điệu.
- **Dedupe** bằng `UNIQUE (site_id, event_fingerprint)` + `INSERT ... ON CONFLICT DO NOTHING RETURNING id`.
- **Idempotency** đầy đủ: reserve key (`status_code = 0` = in-progress), replay khi trùng, `IDEMPOTENCY_KEY_REUSED` khi khác body-hash, `IDEMPOTENCY_IN_PROGRESS`.
- **Lease fencing**: bulk ingest bắt buộc `X-Leader-Term`, kiểm tra `site_fetch_leases` bằng `clock_timestamp()`.
- **Change feed** `dashboard_changes(change_seq)` + `pruned_through_seq` + `RESYNC_REQUIRED`.
- **Snapshot**: `RepeatableRead` + `LIMIT maxRows + 1` + cờ `truncated` + `asOfChangeSeq` đọc nhất quán trong cùng transaction (`ChangeRepository.ReadSnapshotAsync`).
- **Retention** cấu hình được qua bảng `retention_policies` + `RetentionHostedService` (event 60 ngày theo `event_occurred_at`, changes 14 ngày, audit 90 ngày).
- **Client**: `DataHubClient.cs` giữ cursor `MAX(remote_seq)` trên **SQLite** (có defect đã biết).

→ Một "hợp đồng khóa cứng 100%" **phải là delta trên nền này**, không phải một bản mô tả song song. Toàn bộ báo cáo dưới đây đọc theo lăng kính đó.

---

## 2. 🔴 BLOCKER (phải sửa trước khi duyệt)

### B1 — Redis chưa tồn tại, nhưng plan đặt tính đúng đắn lên nó
**Plan nói**: I-2 coi Redis là "auxiliary cache" đã có; §5 (Detail Pointer) và §6 (Dashboard Epoch) xây cơ chế chống stale dựa trên Lua script trên Redis; outbox đẩy invalidation sang Redis.
**Thực tế**: `grep -ri redis src/AutoJMS.DataHub.Api` → **0 kết quả**. `render.yaml` → **không có Redis service**. `Program.cs` → chỉ Postgres + SignalR. Read path hiện tại (`ChangeRepository`) đọc **thẳng Postgres**.
**Rủi ro**: Redis là **hạ tầng mới hoàn toàn** (provision VPS, memory sizing, eviction policy, circuit breaker, giám sát). Plan under-scope nó thành "phụ trợ". Nếu triển khai vội, một lỗi cấu hình Redis có thể ảnh hưởng tính đúng đắn đọc — trong khi lợi ích thực tế chưa được chứng minh (xem M4).
**Cách sửa**: Tách Redis thành **Phase riêng, optional**. Bất biến I-2 đã đúng ("Redis lỗi → bypass 100% Postgres"): hãy biến điều đó thành hợp đồng thật — **tính đúng đắn KHÔNG bao giờ phụ thuộc Redis**; Redis chỉ giảm tải. Phase 1 (ingest/terminal/retention/cursor) **không cần Redis**.

### B2 — Bỏ sót `change_seq`/`site_change_counters`; advisory lock per-waybill mâu thuẫn mô hình khóa hiện tại
**Plan nói**: Dùng `pg_advisory_xact_lock(get_waybill_advisory_lock_key(siteId, waybill))` **per-waybill** để serialize (I-5, §3 bước 7). Plan **không nhắc gì** tới `site_change_counters` hay cách cấp phát `change_seq`.
**Thực tế**: `change_seq` được cấp phát và **serialize per-site** bằng `... FOR UPDATE` trên `site_change_counters`. Đây là backbone của toàn bộ cursor/delta.
**Rủi ro (nghiêm trọng)**: Có hai khả năng, cả hai đều hỏng:
- *Nếu giữ FOR UPDATE per-site + thêm advisory lock per-waybill*: khóa per-site đã serialize tất cả → advisory lock **thừa** (chỉ thêm overhead, không tăng concurrency).
- *Nếu bỏ FOR UPDATE để lấy concurrency per-waybill*: hai transaction song song cấp phát seq 100 và 101; nếu txn(101) commit **trước** txn(100), một reader đang ở cursor 99 chạy `WHERE change_seq > 99 ORDER BY change_seq LIMIT n` sẽ thấy 101, đẩy cursor lên 101, và **không bao giờ quay lại đọc 100** khi nó commit sau → **mất change vĩnh viễn** (client thiếu 1 cập nhật cho tới lần full resync). Chính khóa per-site hiện tại đang chống lỗi này bằng cách cấp phát **và** commit `change_seq` theo đúng thứ tự.
**Cách sửa**: Chốt rõ mô hình khóa (xem v4.3 §Ingest). Khuyến nghị: **giữ serialize allocation của `change_seq`** (per-site) để đảm bảo "commit order = seq order"; advisory lock per-waybill chỉ dùng cho các thao tác **ngoài luồng ingest chính** (retention purge, admin reopen) để chúng không chạy song song với ingest cùng waybill. Nếu thực sự cần concurrency ingest per-waybill, phải kèm **safe watermark** cho reader (chỉ đọc tới `min(in-flight seq) − 1`) — phức tạp, nên hoãn.

### B3 — Lease fencing (`X-Leader-Term`) bị bỏ khỏi "hợp đồng 14 bước"
**Plan nói**: §3 mô tả 14 bước ingest; bước 1 chỉ kiểm "Device Token + capability ReadWriteSiteData". **Không có** khái niệm leader/term/lease.
**Thực tế**: Bulk ingest hiện **bắt buộc** `X-Leader-Term`; `CheckFenceAsync` xác thực `leader_device_id + leader_term + lease_expires_at > clock_timestamp()` (kiểm 3 lần: trước idempotency, khi replay, và trước commit). Đây là cơ chế chống "leader cũ đã mất lease vẫn ghi".
**Rủi ro**: Nếu Claude Code implement "chính xác 14 bước", nó có thể **loại bỏ fencing** → hồi quy an toàn dữ liệu (hai leader ghi song song sau failover).
**Cách sửa**: Hợp đồng ingest v4.3 phải **giữ nguyên fencing** và ghi nó thành bước tường minh (xem v4.3).

### B4 — Bulk ingest vs "1 observation": không tương thích; test #21 mâu thuẫn ranh giới transaction
**Plan nói**: §3 khóa cứng luồng `POST /observations` cho **một** observation; advisory lock per-waybill. Nhưng test **#21** yêu cầu "Bulk 100 đơn có 1 đơn lỗi schema → 99 đơn hợp lệ vẫn commit" (partial success).
**Thực tế**: Endpoint chính là **bulk** (`/jms/ingest`, tối đa 200/req, **một** idempotency-key/req). `IngestRepository` là **all-or-nothing**: gặp `ScanTimeParser` lỗi → `RollbackAsync` **toàn bộ batch** → 400. Không có partial commit.
**Rủi ro**: (a) Hợp đồng 14 bước không định nghĩa hành vi bulk — mà bulk mới là đường chính. (b) Test #21 mô tả partial-success **trái ngược** thiết kế transaction hiện tại. (c) Advisory lock per-waybill trong 1 batch chứa **nhiều** waybill → nếu khóa theo thứ tự item, hai batch song song có tập waybill giao nhau ở thứ tự khác nhau sẽ **deadlock** kinh điển.
**Cách sửa**: Chốt **một** trong hai và ghi vào hợp đồng: giữ **all-or-nothing** (sửa test #21 thành "batch lỗi → 422, không commit phần nào") — khuyến nghị, đơn giản & đã đúng; hoặc chuyển sang **partial-success** (đổi contract + response schema `{accepted, rejected[]}` + bỏ idempotency toàn-batch). Nếu dùng advisory lock trong bulk: **sort waybill theo canonical trước khi khóa** để chống deadlock.

### B5 — Terminal Purge không phát `dashboard_changes 'delete'` → client giữ đơn đã purge vĩnh viễn
**Plan nói**: §7 Phase 2 tạo tombstone rồi `DELETE FROM waybill_projections`. **Không** insert `dashboard_changes`, **không** tăng `change_seq`.
**Thực tế**: Client đồng bộ bằng delta `GET /changes`. Bảng `dashboard_changes.operation` **đã hỗ trợ `'delete'`** (CHECK IN `upsert/delete/resync`) nhưng plan không dùng.
**Rủi ro**: Sau khi purge, đơn biến mất khỏi DB nhưng **client không hề nhận được tín hiệu xóa** → dashboard client hiển thị đơn "ma" cho tới lần full snapshot kế tiếp (có thể rất lâu). Đây là **mất tính đúng đắn** ở phía client.
**Cách sửa**: Purge (và bất kỳ thao tác xóa projection nào) **phải** phát một `dashboard_changes(operation='delete')` + tăng `change_seq` trong cùng transaction, để delta feed báo client bỏ dòng. (Xem v4.3 §Retention.)

### B6 — SQL/pseudocode sẽ lỗi nếu làm y hệt
**(a) Hàm advisory §2.1 dùng định danh không tồn tại.** Dòng 43 viết `md5(p_site_id::text || ':' || p_canonicalWaybill)` trong khi tham số là `p_canonical_waybill`. Postgres fold về lowercase → `p_canonicalwaybill ≠ p_canonical_waybill` → **lỗi biên dịch plpgsql**. (May là Migration 008 ở §15 viết **đúng** `p_canonical_waybill` — tức tài liệu **tự mâu thuẫn**; ai copy nhầm §2.1 sẽ vỡ.)
**(b) Step-10 INSERT thiếu cột `NOT NULL`.** Plan §3 bước 10 INSERT liệt kê `(... source_event_at, received_at, scan_type_code, payload)` nhưng schema thật có `event_occurred_at timestamptz NOT NULL` (không default). Migration 007 **không** drop/để-default cho `event_occurred_at`. → INSERT theo plan **vi phạm NOT NULL**. Ngoài ra plan bỏ nhiều cột đang được code ghi (`scan_type_name, status, network_code, operator_code, package_number, task_code`).
**Cách sửa**: Dùng nguyên mẫu INSERT thật của `IngestRepository.InsertEventAsync` làm chuẩn; mọi cột mới phải có default an toàn hoặc backfill (xem Migration 007 sửa lại trong v4.3).

---

## 3. 🟠 HIGH

### H1 — Dual timestamp columns: nợ kỹ thuật + "clock nào thắng"
Migration 007 thêm `source_event_at`, `received_at` **song song** với `event_occurred_at`, `ingested_at` đang dùng. Sau migration tồn tại **4 cột cho 2 khái niệm**. Reducer/retention/index sẽ mập mờ dùng cột nào. **Khuyến nghị**: đổi tên/di trú **một lần** (rename `event_occurred_at → source_event_at`, `ingested_at → received_at` qua view/compat), **không** duy trì song song. Nếu chưa muốn rename, thì **tái sử dụng** cột cũ thay vì thêm cột mới.

### H2 — Retention xung đột với `retention_policies` đang chạy
Plan hardcode "events > **7 ngày**" và "terminal > **90 ngày**". Thực tế `003_seed_retention` đặt events **60 ngày** (clock `event_occurred_at`), do `RetentionHostedService` thực thi. Hai cơ chế retention song song sẽ **giẫm chân nhau** (và 7 ngày ≠ 60 ngày — có chủ ý không?). **Khuyến nghị**: biểu diễn cả Phase 1 (event purge) và Phase 2 (terminal purge) **qua bảng `retention_policies` + worker hiện có**, không viết worker/DELETE mới hardcode.

### H3 — Reducer tie-break đổi ngầm, không bump `reducer_version`
Plan CC-2/§3 bước 12 nói thứ tự `source_event_at → received_at → server_seq`. Reducer thật (`ProjectionReducer.IsWinner`) hiện dùng `EventOccurredAt` rồi tie-break bằng **`EventFingerprint` (ordinal)** — **không** dùng `received_at`/`server_seq`. Đổi tie-break là **thay đổi ngữ nghĩa projection** cho các event trùng `source_event_at`. **Rủi ro**: kết quả winner khác → version/state có thể khác giữa hai lần rebuild. **Khuyến nghị**: nếu đổi, phải **bump `reducer_version`** và ghi rõ tính tương thích khi replay/rebuild; nếu không cần, giữ nguyên tie-break fingerprint.

### H4 — Fingerprint chuyển sang table-driven: va chạm `fingerprint_version` & dịch ranh giới dedupe
Plan Migration 008 tạo `fingerprint_policies(fingerprint_version, scan_type_code, fingerprint_paths)` (JSONPath **theo từng scan type**). Thực tế fingerprint do code `EventFingerprintV1` tính (một **superset cố định** ~19 field, prefix `v1:`). Chuyển sang table-driven là **thay công thức**. Nếu seed `fingerprint_version = 1` với công thức **mới** trong khi code cũ cũng gọi là "v1" → **hai nghĩa khác nhau cùng số version**; và fingerprint mới ≠ fingerprint cũ → mọi event cũ trông như "mới" → **ranh giới dedupe dịch chuyển**. **Khuyến nghị**: nếu áp table-driven, dùng `fingerprint_version ≥ 2`, giữ event `v1` nguyên vẹn, cutover có kiểm soát; hoặc **hoãn** — schema đã có sẵn `fingerprint_version` để đổi sau, không cần đổi ở Phase 1.

### H5 — Admin Reopen không phát change
§8.1 set `is_terminal = false`, xóa tombstone — nhưng không nói phát `dashboard_changes` (upsert) + tăng `change_seq`. Client sẽ **không thấy** đơn được mở lại. Cùng lớp lỗi với B5. **Khuyến nghị**: reopen phát `upsert` change trong cùng transaction.

### H6 — Client "disk-first + hard-cut SQLite": phạm vi lớn, thiếu migration/rollback
§9–§11 + CC-9 yêu cầu **viết lại tầng cache client** (disk-first, RAM-minimal, async I/O), **bỏ SQLite** (`DataHubClient` hiện lưu row trên SQLite). Đây là thay đổi WinForms lớn, đụng đường render `DataGridView`, và **không có** migration path (dữ liệu SQLite hiện có → cache đĩa mới) hay rollback. `CLAUDE.md` liệt `Main.cs/Main.Designer.cs` là **Protected Files**. **Khuyến nghị**: tách client thành **phase riêng**, sau khi server-side ổn định; có bước "drain outbox → 0" (CC-9) làm gate; và Owner phải cho phép rõ nếu chạm Protected Files.

---

## 4. 🟡 MEDIUM

| # | Vấn đề | Thực tế / Khuyến nghị |
|---|--------|------------------------|
| **M1** | §9 Snapshot "RepeatableRead + LIMIT maxRows+1" trình bày như MỚI (CC-6). | **Đã có** trong `ChangeRepository.ReadSnapshotAsync` (RepeatableRead, `LIMIT maxRows+1`, `truncated`, `asOfChangeSeq` đọc nhất quán). Field thật: `returnedRows`, `asOfChangeSeq`(=snapshotSeq), `capturedAt`. → Ghi nhận "đã xong", chỉ đồng bộ tên field, đừng code lại. |
| **M2** | Test #8 "Pruned Cursor Resync → 409". | **Đã có** (`pruned_through_seq` + `ChangeCursorWindow.RequiresResync` + `RESYNC_REQUIRED`). Ghi nhận đã xong. |
| **M3** | Redis pointer key (`projver:*`) **không TTL** ở cả read & write path. | Số key = số waybill từng đọc → **tăng bộ nhớ vô hạn**. Cần TTL dài cho pointer hoặc `maxmemory-policy=allkeys-lru` + sizing. (Chỉ áp dụng khi làm Phase Redis.) |
| **M4** | Dashboard epoch cache TTL 2–5s, key chứa `epoch`. | Site bận: `epoch` đổi mỗi `change_seq` → key đổi liên tục → **hit-ratio ≈ 0**. Chi phí (Lua, outbox, invalidation) > lợi ích. Cân nhắc **bỏ** dashboard cache, hoặc cache theo `queryHash` với invalidation nhẹ. |
| **M5** | Retention Phase 1 `DELETE ... WHERE id IN (SELECT ... LIMIT 2000)` không `ORDER BY`, không scope theo site. | Xóa hàng tùy ý; không phối hợp khóa; nên đi qua worker hiện có (đã có index `ix_waybill_scan_events_site_occurred`). |
| **M6** | Ma trận idempotency §4.2 thiếu trạng thái **in-progress**. | Code có `status_code=0` + `IDEMPOTENCY_IN_PROGRESS` (409). Thêm hàng thứ 5 vào ma trận. |
| **M7** | "Resolve & pin active policy versions" (bước 5) không có **nguồn sự thật**. | Không có bảng "active reducer/fingerprint/normalizer version". Hiện `reducer_version` mặc định 1 (`JmsEventPolicyCatalog.Default`). Cần định nghĩa nơi lưu "active version" nếu muốn pin per-request. `normalizer_version` là khái niệm hoàn toàn mới, chưa có gì. |

---

## 5. ⚪ LOW / Nits

- **L1 — Atomic rename trên Windows** (I-7): `File.Move(overwrite:true)`/`MoveFileEx(REPLACE_EXISTING)` chỉ atomic **cùng volume NTFS**; cần `FileStream.Flush(true)`/fsync **trước** rename; cẩn thận antivirus giữ handle. Ghi rõ trong hợp đồng client.
- **L2 — `received_at` = `now()` hay `clock_timestamp()`** (§12): plan nói bắt ở ingress bằng `clock_timestamp()`; cột hiện là `ingested_at DEFAULT now()` (= transaction-start time). Hai giá trị khác nhau khi chờ lock. Thống nhất một nguồn (khuyến nghị `clock_timestamp()` gán ở tầng app trước BEGIN, đúng như plan mô tả).
- **L3 — Nhãn/đếm**: "Mười một bất biến" — rà lại số lượng khi thêm invariant mới; "23 yêu cầu", "46 test" cần khớp sau khi sửa.
- **L4 — DPAPI/PII** (OD-4): quyết định trước khi code client cache (xem khuyến nghị §7).
- **L5 — `get_waybill_advisory_lock_key` IMMUTABLE**: đúng về mặt kỹ thuật (md5 deterministic); lưu ý `::bit(64)` phụ thuộc đúng 16 hex ký tự — `substr(md5(...),1,16)` luôn cho 16 → OK.

---

## 6. Nhận xét về Bộ 46 Acceptance Tests

Bộ test **bao phủ tốt** nhiều lớp (race, advisory collision, snapshot boundary, outbox retry idempotency, multi-site cursor). Cần điều chỉnh/bổ sung:

- **Sửa #21** (partial success) cho khớp mô hình transaction đã chốt ở B4.
- **Bổ sung** các test còn thiếu tương ứng các blocker:
  - *Purge-emits-delete*: purge đơn → client nhận `dashboard_changes(delete)` và xóa dòng (B5).
  - *Reopen-emits-change*: reopen → client nhận upsert (H5).
  - *Fence-under-ingest*: leader mất lease giữa batch → 409 `LEADER_FENCED`, không commit (B3).
  - *change_seq monotonic under concurrency*: nhiều ingest song song → reader không bao giờ nhảy qua seq chưa commit (B2).
  - *Fingerprint-version cutover*: đổi công thức fingerprint không phá dedupe của event `v1` cũ (H4).
  - *Retention-not-conflicting*: chỉ một cơ chế retention chạy; giá trị đến từ `retention_policies` (H2).

---

## 7. Khuyến nghị cho 5 "Owner Decision" (OD)

> Đây là **đề xuất để Owner duyệt**, không phải quyết định thay Owner.

- **OD-1 (Terminal scan codes)**: **Chưa chốt được từ code** — seed thật (`002`) mới chỉ phân loại code **98 (inventory), 110 (state_transition)**; plan lại giả định 201/301/401/9001 và terminal 9001/9002/9004. → **Phải đối chiếu JMS thật** trước khi seed. Trong lúc chờ: coi terminal như **policy versioned trong bảng** (không hardcode trong reducer), để đổi mà không sửa code.
- **OD-2 (90 ngày terminal retention)**: Hợp lý, nhưng **tách khỏi** 60 ngày event purge (H2). Đề xuất: giữ event 60 ngày (như hiện tại) + terminal projection 90 ngày, cả hai qua `retention_policies`. Nêu chi phí lưu trữ để Owner cân.
- **OD-3 (Reopen — Option A)**: Đồng ý Option A (chỉ reopen khi projection còn sống). Cần đảm bảo có **đường Historical Rebuild** riêng cho đơn đã purge (đừng để "410 GONE" là ngõ cụt nghiệp vụ).
- **OD-4 (DPAPI cho disk cache)**: Nếu cache đĩa chứa **PII** (tên/địa chỉ/SĐT người nhận) → **khuyến nghị bật DPAPI** (per-user) thay vì chỉ dựa ACL, vì máy trạm có thể dùng chung/nhiều tài khoản, và ACL không bảo vệ khi ổ đĩa bị tháo. Nếu cache chỉ chứa mã đơn + trạng thái → ACL mặc định là đủ.
- **OD-5 (Fingerprint fields theo scan type)**: **Giữ code-driven `EventFingerprintV1`** cho tới khi có payload JMS thật để xác thực JSONPath. Không seed 201/301/401/9001 như "final" (H4). Khi có payload thật → cutover `fingerprint_version = 2`.

---

## 8. Điều v4.2 làm ĐÚNG (giữ nguyên trong v4.3)

Để công bằng, các điểm mạnh cần bảo toàn:

1. **Hàm advisory 64-bit** (Migration 008, bản đúng) — khắc phục birthday-collision của `hashtext` 32-bit; dùng cho **retention/reopen** rất hợp lý.
2. **Idempotency contract** phân biệt HTTP-retry vs business-duplicate — khớp tinh thần code, chỉ cần thêm hàng in-progress.
3. **Terminal marker + Tombstone + anti-resurrection** — **hoàn toàn mới và đúng hướng** (schema chưa có `is_terminal`/tombstone).
4. **Transactional outbox** (`cache_invalidation_outbox`) — pattern đúng; chỉ cần tách phần Redis thành optional.
5. **Multi-site `cursor.json`** — sửa được **defect thật** của client (cursor `MAX(remote_seq)` bị kẹt khi fingerprint đã tồn tại).
6. **I-11 (cấm I/O đồng bộ trên UI thread)** — nguyên tắc client tốt, giữ.

---

## 9. Bước tiếp theo

Bản kế hoạch đã cải thiện **[streaming-v4.3-plan.vi.md](./streaming-v4.3-plan.vi.md)** đóng gói toàn bộ cách sửa trên thành một **hợp đồng brownfield theo phase**, với: migration 007–009 đã sửa đúng schema thật, hợp đồng ingest tích hợp counter + fencing, Redis tách phase optional, purge/reopen phát change, kế hoạch rollout + rollback, và bộ acceptance test cập nhật. Đề nghị Owner duyệt **theo từng phase** thay vì duyệt trọn gói.
