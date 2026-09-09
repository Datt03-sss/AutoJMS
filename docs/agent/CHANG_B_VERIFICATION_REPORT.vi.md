# Báo cáo rà soát Chặng B — commit `88403ca`

| | |
|---|---|
| **Đối tượng rà soát** | `88403ca` — *feat(backend): Chặng B hoàn tất — B12 retention tombstone & B15 license server graceful shutdown / version* |
| **Nền so sánh** | `5e8e207` |
| **Ngày rà soát** | 2026-08-26 |
| **Phương pháp** | `superpowers:verification-before-completion` — bằng chứng trước tuyên bố, không tin số liệu trong báo cáo gốc |
| **Kết luận** | ❌ **Chưa đóng được Chặng B.** Còn một lỗi correctness ở nửa client, chưa được test chạm tới. |

---

## 0. Hai câu tóm tắt

1. **Rủi ro số 1 mà báo cáo gốc tự nêu — "hai câu SQL mới chưa từng chạy trên PostgreSQL thật" — đã được gỡ.** Container đám mây có sẵn PostgreSQL 16; đã áp cả 6 migration và chạy nguyên văn hai câu SQL trên DB thật. Cả hai chạy đúng như tài liệu mô tả, kể cả ca biên.
2. **Nhưng phát sinh một lỗi mới mà báo cáo không bắt được**: client áp **hết `DELETE` rồi mới tới `UPSERT`** trong cùng một trang change, nên một upsert cũ cùng trang sẽ **hồi sinh** waybill vừa bị tombstone xoá. Lỗi đang ngủ cùng B12, phải sửa **trước khi** operator bật policy.

---

## 1. Phương pháp và giới hạn

Rà soát chạy trên hai môi trường:

| Môi trường | Có gì | Dùng để |
|---|---|---|
| VM Linux của máy chủ dự án (device bridge) | `git`, `node` v22, `npm` | đọc mã nguồn, chạy lại toàn bộ test JS |
| Container đám mây | **PostgreSQL 16.13**, `psql` | dựng DB thật, áp migration, chạy SQL của B12 |

**Không kiểm chứng được — phải nói rõ, không được suy diễn:**

| Tuyên bố | Vì sao không kiểm được |
|---|---|
| `dotnet build -c Release` → 0 Warning, 0 Error | Không có `dotnet` ở cả hai môi trường |
| `dotnet test` → 360/360 | `tests/AutoJMS.Tests` là `net8.0-windows` và tham chiếu dự án WinForms — chỉ chạy được trên Windows của chủ dự án |
| `verify.ps1` → ALL GATES PASSED | Không có `pwsh` |

Ba dòng này **được ghi nhận theo báo cáo, chưa được xác thực độc lập**. Xem §5.1 — có một bằng chứng gián tiếp mạnh cho con số 360.

---

## 2. Đối chiếu từng tuyên bố

### 2.1 Commit và phạm vi thay đổi

| Tuyên bố | Trạng thái | Bằng chứng |
|---|---|---|
| Commit `88403ca` tồn tại | ✅ | `git cat-file -t` → `commit` |
| Đã push lên `origin/main` | ✅ | `HEAD` = `origin/main` = `88403ca3086…` |
| 12 file thay đổi, khớp bảng "Files Changed" | ✅ | `git show --stat` → đúng 12 file, `+1230 / −112`, từng đường dẫn khớp |
| Không sửa Protected File nào | ✅ | Đối chiếu với danh sách 9 mục ở `AGENTS.md §Protected Files` — không mục nào nằm trong commit |
| `LicenseApiService.cs` không bị chạm | ✅ | Không có trong danh sách file của commit |
| Không thêm migration nào | ✅ | Không có file `.sql` nào trong commit |

### 2.2 Luận điểm nền của B12 — "cái hard-delete cần chèn tombstone KHÔNG TỒN TẠI"

Đây là tuyên bố quan trọng nhất của cả báo cáo, vì nó quyết định bản vá phải **dựng cơ chế** thay vì **chèn một câu lệnh**. Đã kiểm ở bản `5e8e207`:

| Kiểm | Kết quả |
|---|---|
| `RetentionRepository` trước commit có đụng `waybill_projections` không? | ❌ **Không** — chỉ `waybill_scan_events`, `dashboard_changes`, `audit_logs`, `idempotency_records` |
| `003_seed_retention.sql` có seed policy cho `waybill_projections` không? | ❌ **Không** — chỉ 3 policy: `waybill_scan_events` (60d), `dashboard_changes` (14d), `audit_logs` (90d) |

✅ **Luận điểm đúng.** S7 là lỗ hổng tiềm ẩn, không phải đang chảy máu — và do đó cơ chế opt-in là lựa chọn đúng, không phải né việc.

### 2.3 Schema đỡ được bản vá mà không cần migration

| Tuyên bố | Trạng thái | Bằng chứng (`001_core.sql`) |
|---|---|---|
| `ck_dashboard_changes_operation` đã cho `'delete'` | ✅ | `CHECK (operation IN ('upsert', 'delete', 'resync'))` |
| `entity_type` không có allow-list CHECK | ✅ | `entity_type text NOT NULL`, không CHECK |
| `waybill_projections` không có FK trỏ vào | ✅ | `Referenced by:` của `waybill_projections` rỗng |
| `waybill_projections.updated_at` tồn tại | ✅ | `updated_at timestamptz NOT NULL DEFAULT now()` |
| `body` NOT NULL — tombstone phải có payload | ✅ | CTE cấp `jsonb_build_object('waybill_no', …)`, không phải `'{}'` rỗng |

Thêm một ràng buộc báo cáo không nhắc nhưng có liên quan: `ck_site_change_counters_pruned_range` đòi `pruned_through_seq <= change_seq`. CTE `bumped` chỉ **tăng** `change_seq`, nên không vi phạm.

### 2.4 Cửa sổ tombstone 90 / 30 / 365

| Tuyên bố | Trạng thái | Bằng chứng |
|---|---|---|
| Mặc định 90 ngày, clamp cứng 30–365 | ✅ | `DataHubRuntimeOptions`: hằng `90/30/365`; clamp **hai lớp** — ở `ParseBoundedInt` khi đọc env, và lần nữa trong `RunOnceAsync` |
| Biến env `DATAHUB_TOMBSTONE_RETENTION_DAYS` | ✅ | Đúng tên, đúng chỗ |
| Tombstone miễn trừ khỏi đồng hồ 14 ngày | ✅ | `CASE WHEN c.operation = 'delete' THEN … @tombstone_retention ELSE … delete_after END` |

Clamp hai lớp là chủ ý đúng: `RunOnceAsync` là nơi duy nhất còn phân biệt được "operator chọn 0" với "caller nội bộ truyền `default`", và `TimeSpan.Zero` sẽ prune sạch tombstone ngay lượt sau.

### 2.5 Đường đi client

| Tuyên bố | Trạng thái | Bằng chứng |
|---|---|---|
| `DeletedWaybillNos` tách khỏi `Rows` | ✅ | `DataHubChangePage` mang hai danh sách rời |
| Định tuyến theo `operation` | ✅ | `ProjectChangeItems`: `operation == "delete"` → `deleted`, còn lại → `ToWaybillRow` |
| Chuẩn hoá khoá giống nhau ở hai nhánh | ✅ | Cả hai đều `.Trim().ToUpperInvariant()` — không lệch hoa/thường |
| `DELETE FROM fs_waybills` bất chấp `_hasLease` | ✅ | Vòng delete không có guard `_hasLease`; vòng upsert có `if (_hasLease) continue;` |
| `PullEventsDeltaAsync` **không** skip tombstone | ✅ | Không có nhánh skip; cursor là `MAX(remote_seq)` nên skip sẽ làm cursor đứng — lý do ghi đúng |
| Resync không xoá hàng địa phương | ✅ | Toàn file chỉ có **một** `DELETE FROM fs_waybills`, nằm ở nhánh tombstone |
| `fs_events` không cascade | ✅ | Không tồn tại lệnh xoá `fs_events` theo waybill |

### 2.6 B15 — License server

| Tuyên bố | Trạng thái | Bằng chứng (`server.js`) |
|---|---|---|
| `/api/version` và `/health/version` cùng payload | ✅ | Cả hai `res.json(versionPayload())`, cùng `healthLimiter` |
| Có commit hash | ✅ | `RENDER_GIT_COMMIT` → `GIT_COMMIT` → … , có nhánh "unknown" khi thiếu |
| Vá lỗ (a) thiếu SIGINT | ✅ | `process.on("SIGINT", …)` bên cạnh `SIGTERM` |
| Vá lỗ (b) không có hạn ép thoát | ✅ | `closeIdleConnections()` + `setTimeout(…, timeoutMs)` có `.unref()` |
| Vá lỗ (c) không idempotent | ✅ | Cờ `closing`, signal thứ hai chỉ log `shutdown.signal_ignored` |
| Vá lỗ (d) exit code cứng 0 | ✅ | `exit(0)` khi đóng sạch, `exit(1)` khi ép thoát hoặc `close()` lỗi |
| `SHUTDOWN_TIMEOUT_MS=0` bị clamp về 1000 | ✅ | `Math.max(1000, Number(…))` |
| `createShutdownHandler` export ra để test | ✅ | `module.exports.createShutdownHandler` |
| Handler chỉ đăng ký ở nhánh entry-point | ✅ | Trong `if (require.main === module)` — harness không thừa kế handler process-wide |

`harness.js` thêm `RENDER_GIT_COMMIT`, `GIT_COMMIT`, `SOURCE_VERSION`, `COMMIT_SHA`, `SHUTDOWN_TIMEOUT_MS` vào danh sách reset về `undefined` — đúng, nếu không thì CI runner có `GIT_COMMIT` trong shell sẽ làm test "báo unknown" pass/fail ngẫu nhiên.

### 2.7 Chữ ký đổi có lan hết chưa (kiểm tĩnh thay cho `dotnet build`)

Vì không build được, đã soi thủ công mọi điểm gọi — đúng theo luật *"không lỗi mới được coi là xong"*:

| Kiểm | Kết quả |
|---|---|
| `RunOnceAsync` đổi chữ ký (`+TimeSpan`) — còn caller nào chưa sửa? | Đúng **1** caller (`RetentionHostedService:19`), đã truyền `options.TombstoneRetention` |
| `RetentionRunResult` thêm 2 field — `Empty` còn hợp lệ? | `new(0,0,0,0)` vẫn hợp lệ vì 2 field mới có default `= 0` |
| Số placeholder log khớp số tham số? | 7 placeholder (`{Events} {Changes} {AuditLogs} {Idempotency} {Projections} {Tombstones} {UtcNow}`) — 7 tham số ✅ |

### 2.8 Chạy lại toàn bộ test JavaScript — bằng chứng tươi

Đây là bộ test **duy nhất** rà soát này chạy lại được từ đầu. Chia 3 nhóm cho vừa hạn thời gian mỗi lời gọi:

| Nhóm | Lệnh | Kết quả |
|---|---|---|
| 1 | `node --test datahub-assertion firebase-credentials google-sheets-grant health heartbeat` | **68 pass / 0 fail** |
| 2 | `node --test license-expiry license-record-fields logout verify-license-guards verify-license` | **55 pass / 0 fail** |
| 3 | `node --test version shutdown` *(hai file mới)* | **16 pass / 0 fail** |
| | **Tổng** | **139 pass / 0 fail** |

`npm run check` (`node --check server.js`) → exit **0**.

✅ Khớp chính xác tuyên bố `npm test → 139/139` và `npm run check → pass`. 16 test ở nhóm 3 cũng khớp với con số `123 → 139` mà plan doc ghi.

---

## 3. Kiểm chứng SQL trên PostgreSQL 16 thật

> Đây là phần gỡ **rủi ro số 1** mà báo cáo gốc tự nêu và đề nghị Antigravity `EXPLAIN` trên staging.

Đã dựng PostgreSQL **16.13** trong container, áp lần lượt 6 migration với `-v ON_ERROR_STOP=1`:

```
OK   001_core.sql
OK   002_seed_policies.sql
OK   003_seed_retention.sql
OK   004_projection_slot_payloads.sql
OK   005_change_retention_floor.sql
OK   006_revocation_and_retention_indexes.sql
```

Rồi seed dữ liệu có chủ đích và chạy **nguyên văn** SQL trích từ `RetentionRepository.cs`.

### 3.1 `DeleteProjectionsAsync`

Dữ liệu vào: site `BC001` (có `site_change_counters`, `change_seq = 5`) với 3 projection 400 ngày tuổi + 1 projection mới; site `BC002` **không có** dòng counter, có 1 projection 400 ngày tuổi; policy global `waybill_projections` `delete_after = 365 days`.

| Bước | Kết quả thật |
|---|---|
| Câu `candidatesSql` | Trả đúng 4 hàng cũ, loại `WBNEW` |
| Câu CTE chính | `DELETE 3` |
| `dashboard_changes` | 3 tombstone `operation='delete'`, `change_seq` = 6, 7, 8 |
| `site_change_counters` | `5 → 8` (CTE `bumped` chạy dù không được câu chính tham chiếu — đúng ngữ nghĩa data-modifying CTE của PostgreSQL) |
| Projection của `BC002` (site không có counter) | **Được giữ lại** — đúng như tài liệu hứa, không xoá âm thầm |
| `WBNEW` | Không bị đụng |

✅ Câu lệnh chạy được, và ca biên "site không có counter" hành xử đúng như comment mô tả.

### 3.2 `DeleteChangesAsync`

Chạy tiếp trên chính DB đó (feed `BC001`: 5 change thường 200 ngày tuổi ở seq 1–5, 3 tombstone mới ở seq 6–8; policy `dashboard_changes` = 14 ngày):

| Kết quả thật |
|---|
| `DELETE 5` — dọn đúng tiền tố seq 1–5 |
| 3 tombstone seq 6–8 **sống sót** dưới đồng hồ 90 ngày |

✅ Miễn trừ tombstone khỏi đồng hồ 14 ngày hoạt động đúng.

### 3.3 Tái hiện rủi ro "tombstone ghim feed"

Báo cáo nêu đây là cái giá chấp nhận có ý thức. Đã **tái hiện được**, không phải suy đoán:

Site `BC003`: một tombstone **30 ngày tuổi** (dưới trần 90) ở `seq = 1`, theo sau là 5 change thường **200 ngày tuổi** ở seq 2–6.

```
 change_seq | operation | change_at
------------+-----------+------------
          1 | delete    | 2026-07-27   <- tombstone còn sống
          2 | upsert    | 2026-02-07   <- 200 ngày, lẽ ra prune được
          3 | upsert    | 2026-02-07
          4 | upsert    | 2026-02-07
          5 | upsert    | 2026-02-07
          6 | upsert    | 2026-02-07
```

Prune được **0/6 dòng**. ✅ Rủi ro có thật, độ lớn đúng như mô tả, và trần 365 ngày tồn tại chính vì thế.

---

## 4. ❌ Lỗi còn mở — thứ tự áp change trong một trang

**Mức độ:** correctness, mất/thừa dữ liệu âm thầm ở máy follower.
**Trạng thái:** đang **ngủ** (chưa có policy ⇒ chưa có tombstone nào được phát).
**Phải sửa trước:** bước 5 của chính checklist trong báo cáo gốc — *"sau khi Antigravity bật thử policy trên một site staging"*.

### 4.1 Cơ chế

`DataHubClient.ProjectChangeItems` tách một trang change thành hai danh sách rời và **vứt mất `changeSeq`**:

```csharp
internal static (List<JObject> Rows, List<string> Deleted) ProjectChangeItems(...)
```

`DataHubSyncService.PullWaybillsAsync` sau đó lấy cả hai từ **cùng một trang** rồi áp **hết delete trước, hết upsert sau**:

```csharp
var rows    = page.Rows;
var deleted = page.DeletedWaybillNos;
...
foreach (var waybillNo in deleted) { ... DELETE FROM fs_waybills ... }   // toàn bộ delete
foreach (var row in rows)          { if (_hasLease) continue; ... INSERT … ON CONFLICT … }  // rồi mới upsert
```

Một trang chứa tối đa **500** change (`ChangeRepository` clamp `1..500`), sắp theo `change_seq`. Nếu cùng trang có:

- upsert của waybill **X** ở seq thấp, và
- tombstone của chính **X** ở seq cao

thì thứ tự thực thi thành `DELETE X` → `INSERT X`, tức **X sống lại ở SQLite địa phương** trong khi server đã xoá. Cursor đã trôi qua tombstone nên **không bao giờ tự sửa**.

### 4.2 Phạm vi ảnh hưởng

| | |
|---|---|
| **Leader** (`_hasLease == true`) | An toàn — vòng upsert có `continue`, chỉ delete được áp |
| **Follower** (`_hasLease == false`) | **Bị lỗi** |
| Điều kiện kích hoạt | Bưu cục vắng đủ để ≤500 change trải hết cửa sổ `delete_after` của projection; hoặc follower offline lâu rồi bắt kịp một lần |

### 4.3 Vì sao test không bắt được

Test `Upserts_and_tombstones_on_one_page_are_kept_apart()` dùng **ba mã waybill khác nhau**:

```csharp
Upsert("886000000001", 40),
Tombstone("886000000002", 41),
Upsert("886000000003", 42)
```

Nó khẳng định đúng cái thiết kế sinh ra lỗ hổng, và không có ca nào cho **cùng một mã** xuất hiện ở cả hai vai.

### 4.4 Hướng vá đề xuất

Giữ **thứ tự seq** — một danh sách thao tác có thứ tự thay cho hai danh sách rời — rồi áp tuần tự. Tối thiểu: loại khỏi `Rows` mọi waybill có tombstone `changeSeq` cao hơn.

> ⚠️ **Đừng** vá bằng cách "bỏ upsert nếu waybill nằm trong `deleted`". Ca ngược — xoá ở seq 5 rồi upsert lại ở seq 7 — **hiện đang đúng**, và cách vá đó sẽ phá nó.

Kèm test: cùng một mã waybill, upsert seq thấp + tombstone seq cao, kỳ vọng hàng **không** tồn tại sau khi áp.

---

## 5. Sai lệch nhỏ trong báo cáo gốc

### 5.1 "+15 test mới" — thực tế là **+18**

Đếm tĩnh số test-case (`[Fact]` + `[InlineData]`, tức cách xUnit đếm) trên toàn thư mục `tests/`:

| Mốc | Số test-case |
|---|---|
| `5e8e207` | **342** |
| `88403ca` | **360** |
| Chênh | **+18** |

Chi tiết hai file mới: `TombstoneRetentionTests.cs` = 4 `[Fact]` + 6 `[InlineData]` (2 `[Theory]`) = 10 case; `DataHubChangeTombstoneTests.cs` = 8 `[Fact]`.

Hai điều rút ra:

- Con số **360** trong báo cáo **được chứng thực gián tiếp** bằng đếm tĩnh — đây là bằng chứng mạnh nhất có thể có cho `dotnet test → 360/360` khi không chạy được `dotnet`.
- Nhưng **"+15" là đếm sai**; đúng ra là **+18**. Sai số nhỏ, không ảnh hưởng kết luận, nhưng đã ghi vào tài liệu thì nên sửa.

### 5.2 Checklist bước 3 tìm một dòng log có thể không bao giờ in ra

Checklist ghi: *"Máy follower offline một lúc rồi bật lại → log `[HybridSync] pulled waybills … deleted=0`"*. Nhưng dòng đó nằm sau guard:

```csharp
if (applied > 0)
    AppLogger.Info($"[HybridSync] pulled waybills rows={rows.Count} applied={applied} deleted={deleted.Count}");
```

Follower bật lại mà **không có gì để áp** sẽ không in dòng nào. Người kiểm thử sẽ kết luận "trượt" trong khi mọi thứ bình thường. Nên sửa mô tả bước này thành *"nếu có hàng được áp thì log phải ghi `deleted=0`"*.

### 5.3 Checklist bước 5 mạnh hơn cái mã nguồn bảo đảm

Checklist ghi: *"xác nhận log `{Projections}` và `{Tombstones}` **bằng nhau**"*. Nhưng chính comment trong mã nói hai số **có thể lệch một cách hợp lệ**:

```csharp
// The two counts can differ by design: a concurrent delete of the same projection
// leaves the tombstone standing, which is still the correct announcement.
```

Chiều an toàn cần kiểm là **`{Tombstones} >= {Projections}`** — vì `DELETE` lấy `inserted` làm driver, không đường nào xoá được một hàng mà thiếu tombstone. `{Tombstones} < {Projections}` mới là tín hiệu hỏng. Nên sửa bước này thành bất đẳng thức, không phải đẳng thức.

---

## 6. Những gì báo cáo tự nêu và đều đúng

Ghi lại để không phải kiểm lại lần sau:

| Rủi ro tự nêu | Trạng thái sau rà soát |
|---|---|
| SQL mới chưa chạy trên PG thật | ✅ **Đã gỡ** — xem §3. Vẫn nên `EXPLAIN` trên staging vì kế hoạch thực thi phụ thuộc dữ liệu thật, nhưng cú pháp và ngữ nghĩa đã được chứng minh. |
| Lỗi SQL không làm sập API, retention âm thầm ngừng chạy | ✅ Đúng — `RetentionHostedService` bắt mọi exception rồi `LogWarning`. Đây là lý do §3 đáng làm. |
| Tombstone ghim feed tới 90 ngày | ✅ Đúng, đã tái hiện được (§3.3) |
| Bẫy operator: `delete_after` của projection ngắn hơn của `waybill_scan_events` (60 ngày) | ✅ Đúng về cơ chế — projection sinh từ event, đã ghi thành công thức + 3 ràng buộc trong plan |
| `backend-schema-dump.sql` đã cũ | ✅ Đúng — header tự ghi *"Reflects 001..005"*, không có dấu vết 006 trong file. File tự mâu thuẫn với chính header của nó. |
| `fs_events` không cascade; `fs_outbox` cũ về lý thuyết tái tạo được projection | ✅ Nhất quán với mã nguồn |
| A1-a…A1-g vẫn chặn go-live | Việc của chủ sở hữu (nhập secret trên Render) — ngoài phạm vi rà soát mã |

---

## 7. Việc cần làm trước khi coi Chặng B là đóng

| # | Việc | Ai | Chặn cái gì |
|---|---|---|---|
| 1 | **Vá thứ tự áp change trong một trang** (§4.4) + test cùng-mã-waybill | Claude | Chặn việc bật policy B12 trên bất kỳ site nào |
| 2 | Chạy `dotnet build -c Release`, `dotnet test`, `verify.ps1` trên máy Windows và dán output | Chủ dự án | Ba dòng duy nhất chưa được xác thực độc lập |
| 3 | Sửa "+15" → "+18" trong plan/báo cáo | Claude | Không chặn gì, chỉ là chính xác |
| 4 | Sửa mô tả checklist bước 3 và bước 5 (§5.2, §5.3) | Claude | Chặn việc chủ dự án kết luận sai khi kiểm thủ công |
| 5 | `pg_dump -s` lại trên VPS sau 006 | VPS Ops | Đã có trong "việc còn nợ" của plan |
| 6 | `EXPLAIN` hai câu SQL trên staging có dữ liệu thật | Antigravity | Kế hoạch thực thi, không phải cú pháp — cú pháp đã xong ở §3 |

---

## Phụ lục A — Công thức dựng PostgreSQL thật để kiểm SQL

Ghi lại vì đây là thứ đã biến rủi ro số 1 từ "không kiểm được" thành "đã kiểm". Container đám mây **có sẵn** PostgreSQL 16 tại `/usr/lib/postgresql/16/bin`.

```bash
useradd -m pgtest; mkdir -p /tmp/pg; chown -R pgtest:pgtest /tmp/pg
su pgtest -c 'PATH=/usr/lib/postgresql/16/bin:$PATH initdb -D /tmp/pg/data -U postgres --auth=trust'
# initdb TỪ CHỐI chạy dưới root — bắt buộc phải có user thường

cat >> /tmp/pg/data/postgresql.conf <<'EOF'
listen_addresses = ''
unix_socket_directories = '/tmp/pg'
port = 5433
EOF

su pgtest -c 'PATH=/usr/lib/postgresql/16/bin:$PATH pg_ctl -D /tmp/pg/data -l /tmp/pg/server.log start'
su pgtest -c "psql -h /tmp/pg -p 5433 -U postgres -d datahub -v ON_ERROR_STOP=1 -f 001_core.sql"
```

Đưa 6 file migration vào container bằng `device_stage_files`, áp lần lượt, seed vài dòng, rồi chạy câu SQL cần soi.

## Phụ lục B — Bẫy môi trường gặp phải

| Bẫy | Cách đi vòng |
|---|---|
| `initdb` từ chối chạy dưới `root` | Tạo user thường (`useradd -m pgtest`) |
| Mỗi lời gọi shell trên máy chủ dự án là shell mới | `nohup … &` **không sống sót** — chia nhỏ việc cho vừa hạn ~45 s, đừng chạy nền rồi quay lại đọc log |
| `npm test` (139 test) quá hạn 45 s | Chia 3 nhóm file: `node --test test/a.js test/b.js …`, mỗi nhóm ~20 s |
| Cây làm việc thường dirty sẵn (`.agent/**`) | Không ảnh hưởng kết quả rà soát; đừng `git add .` |
