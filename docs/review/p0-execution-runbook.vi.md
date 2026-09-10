# P0 Execution Runbook — Brownfield Freeze, Backup, Infra & Baseline

> Thi hành **§24/P0** của [streaming-v4.6-contract.vi.md](./streaming-v4.6-contract.vi.md).
> **Môi trường**: chạy trên **staging** trước (theo quyết định Owner). Production chỉ đọc, và chỉ ở Bước 0-bis.
> **Nguyên tắc P0**: ⛔ **KHÔNG đổi một dòng code ứng dụng. KHÔNG chạy migration.** P0 chỉ *đọc*, *sao lưu*, *đo*, và *thu thập bằng chứng*.
> **Quy tắc dừng**: bất kỳ bước nào FAIL → **STOP RELEASE**, ghi báo cáo, không đi tiếp.

---

## Chuẩn bị

```bash
cd backend/datahub/scripts
ENV=/path/to/.env.staging          # thay bằng đường dẫn thật
./dc.sh --env-file "$ENV" ps       # xác nhận stack đang chạy
```

Mọi lệnh dưới đây dùng ba script **đã có sẵn trong repo**: `dc.sh`, `run-sql.sh`, `backup-postgres.ps1` / `restore-postgres.ps1`, `smoke-test.sh`. ⛔ Không viết script mới.

---

## Bước 0 — Thu thập bằng chứng gỡ **OD-1** (read-only)

**Mục đích**: biến OD-1 từ "chờ đối chiếu JMS thật" thành "Owner xác nhận danh sách có bằng chứng".
**Cơ sở**: `waybill_scan_events` đã lưu **cả** `scan_type_code` **và** `scan_type_name`.

> ✅ **Đã có bằng chứng (10/09/2026)** — trích thẳng từ JMS thật bằng
> [tools/harvest_jms_events.py](../../tools/harvest_jms_events.py): 239/239 đơn, 4.753 sự kiện,
> 24 mã. Kết quả và ba cảnh báo kèm theo (cửa sổ settle 14 ngày, nhãn seed `110` sai, bench residue
> `BENCH-%` trên staging) nằm ở [od1-scan-vocabulary-evidence.vi.md](./od1-scan-vocabulary-evidence.vi.md).
> Query dưới đây vẫn là bản chính thức để chạy trên DB — **đọc §4 của tài liệu bằng chứng trước khi chạy**,
> nếu không query 0.2 sẽ trả về 1 dòng sai và query 0.5 sẽ báo drift giả.

Tạo `backend/datahub/tests/od1_scan_vocabulary.sql`:

```sql
-- 0.1 Toàn bộ từ vựng scan type thật, kèm tần suất
SELECT scan_type_code,
       scan_type_name,
       count(*)                        AS events,
       count(DISTINCT waybill_no)      AS waybills,
       min(event_occurred_at)          AS first_seen,
       max(event_occurred_at)          AS last_seen
  FROM waybill_scan_events
 GROUP BY scan_type_code, scan_type_name
 ORDER BY events DESC;

-- 0.2 BẰNG CHỨNG TERMINAL: scan type nào là event CUỐI CÙNG của một waybill
--     và waybill đó đã "yên" >= 14 ngày (loại đơn còn đang chạy).
WITH last_event AS (
    SELECT DISTINCT ON (site_id, waybill_no)
           site_id, waybill_no, scan_type_code, scan_type_name, event_occurred_at
      FROM waybill_scan_events
     ORDER BY site_id, waybill_no, event_occurred_at DESC, id DESC
),
settled AS (
    SELECT * FROM last_event
     WHERE event_occurred_at < now() - interval '14 days'
),
totals AS (
    SELECT scan_type_code, scan_type_name, count(*) AS total_occurrences
      FROM waybill_scan_events
     GROUP BY scan_type_code, scan_type_name
)
SELECT s.scan_type_code,
       s.scan_type_name,
       count(*)                                              AS times_final,
       t.total_occurrences,
       round(100.0 * count(*) / NULLIF(t.total_occurrences,0), 1) AS pct_final
  FROM settled s
  JOIN totals t
    ON t.scan_type_code IS NOT DISTINCT FROM s.scan_type_code
   AND t.scan_type_name IS NOT DISTINCT FROM s.scan_type_name
 GROUP BY s.scan_type_code, s.scan_type_name, t.total_occurrences
 ORDER BY times_final DESC;

-- 0.3 Đối chiếu với từ vựng client đang dùng (DkchJourneyAnalyzer.Classify)
SELECT scan_type_code, scan_type_name, count(*) AS events
  FROM waybill_scan_events
 WHERE scan_type_name ILIKE '%Ký nhận%'
    OR scan_type_name LIKE '%签收%'
    OR scan_type_name ILIKE '%chuyển hoàn%'
    OR scan_type_name LIKE '%退件%'
 GROUP BY 1,2
 ORDER BY events DESC;

-- 0.4 scan_type_code có bao giờ NULL không? (JmsObservation.Code là int? nullable)
SELECT count(*) FILTER (WHERE scan_type_code IS NULL) AS null_code,
       count(*)                                        AS total
  FROM waybill_scan_events;
```

Chạy:
```bash
./run-sql.sh --env-file "$ENV" ../tests/od1_scan_vocabulary.sql
```

### Cách đọc kết quả
- **Ứng viên terminal** = dòng ở 0.2 có `pct_final` cao (gần 100%) **và** `times_final` đủ lớn để không phải nhiễu.
- Từ code client, hai ứng viên đã biết trước: `快件签收` / **"Ký nhận CPN"** và `退件签收` / **"Ký nhận chuyển hoàn"**.
- Nếu 0.4 cho thấy `scan_type_code` **có NULL**, thì ⛔ policy terminal **không được** chỉ key theo mã số — phải key theo `scan_type_name` (hoặc cả hai). Đây là đầu vào bắt buộc cho thiết kế policy ở P1.

### ⚠️ Cảnh báo về staging
Staging **có thể không phủ hết** scan type của production. Vì vậy:
- Kết quả staging = **danh sách ứng viên**, không phải danh sách cuối cùng.
- ⛔ **Bắt buộc chạy lại 0.1–0.4 trên production (read-only)** trước khi P6 bật terminal purge.
- Owner ký OD-1 dựa trên bằng chứng, và ghi rõ đã ký theo dữ liệu nguồn nào.

---

## Bước 1 — Schema Preflight (7 đối tượng, read-only)

Tạo `backend/datahub/tests/p0_preflight.sql`:

```sql
-- 1.1 Bảng
SELECT 'table' AS kind, t.name AS object,
       CASE WHEN c.table_name IS NULL THEN 'missing' ELSE 'exists' END AS state
  FROM (VALUES ('waybill_scan_events'),('waybill_projections'),
               ('site_change_counters'),('dashboard_changes'),
               ('idempotency_records'),('jms_event_policies'),
               ('retention_policies'),('waybill_tombstones')) AS t(name)
  LEFT JOIN information_schema.tables c
         ON c.table_name = t.name AND c.table_schema = 'public';

-- 1.2 Cột dự kiến THÊM ở P1 — phải là 'missing'; nếu 'exists' thì so type/nullability/default
SELECT 'column' AS kind,
       e.tbl || '.' || e.col AS object,
       CASE WHEN c.column_name IS NULL THEN 'missing' ELSE 'exists' END AS state,
       c.data_type, c.is_nullable, c.column_default
  FROM (VALUES
        ('waybill_scan_events','event_kind'),
        ('waybill_scan_events','reducer_version'),
        ('waybill_scan_events','normalizer_version'),
        ('waybill_scan_events','source_schema_version'),
        ('waybill_projections','is_terminal'),
        ('waybill_projections','terminal_at'),
        ('waybill_projections','terminal_state_code'),
        ('waybill_projections','last_change_seq')
       ) AS e(tbl,col)
  LEFT JOIN information_schema.columns c
         ON c.table_name = e.tbl AND c.column_name = e.col AND c.table_schema='public';

-- 1.3 Cột CẤM ĐỤNG — phải tồn tại đúng như kỳ vọng
SELECT 'guard' AS kind, table_name||'.'||column_name AS object,
       data_type, is_nullable, column_default
  FROM information_schema.columns
 WHERE table_schema='public'
   AND (table_name,column_name) IN (
        ('waybill_scan_events','event_occurred_at'),
        ('waybill_scan_events','ingested_at'),
        ('waybill_scan_events','fingerprint_version'),
        ('site_change_counters','change_seq'),
        ('site_change_counters','pruned_through_seq'));

-- 1.4 Index
SELECT 'index' AS kind, indexname AS object, tablename
  FROM pg_indexes
 WHERE schemaname='public'
   AND tablename IN ('waybill_scan_events','waybill_projections','dashboard_changes')
 ORDER BY tablename, indexname;

-- 1.5 Constraint (PK của jms_event_policies phải composite)
SELECT 'constraint' AS kind, tc.table_name||'.'||tc.constraint_name AS object,
       tc.constraint_type, string_agg(kcu.column_name, ',' ORDER BY kcu.ordinal_position) AS cols
  FROM information_schema.table_constraints tc
  JOIN information_schema.key_column_usage kcu
    ON kcu.constraint_name = tc.constraint_name AND kcu.table_schema = tc.table_schema
 WHERE tc.table_schema='public'
   AND tc.table_name IN ('jms_event_policies','waybill_projections','dashboard_changes','idempotency_records')
 GROUP BY 1,2,3 ORDER BY 2;

-- 1.6 INVALID index (bẫy CREATE INDEX CONCURRENTLY) — phải RỖNG
SELECT indexrelid::regclass AS invalid_index FROM pg_index WHERE NOT indisvalid;

-- 1.7 Migration đã áp
SELECT version, applied_at FROM schema_migrations ORDER BY version;

-- 1.8 ⚠️ AN TOÀN: policy purge projection phải CHƯA tồn tại
SELECT count(*) AS projection_purge_policies
  FROM retention_policies WHERE table_name = 'waybill_projections';
```

```bash
./run-sql.sh --env-file "$ENV" ../tests/p0_preflight.sql
```

### Tiêu chí PASS
| Kiểm | Kỳ vọng |
|---|---|
| 1.2 | Cả 8 cột **`missing`**. Nếu có `exists` mà type/nullability/default **khác kỳ vọng** → ⛔ **STOP + REPORT** (không auto-skip toàn bộ migration) |
| 1.3 | `event_occurred_at` & `ingested_at` tồn tại; `ingested_at` có `column_default = now()`; `fingerprint_version` `NOT NULL DEFAULT 1` |
| 1.5 | `jms_event_policies` PK = `reducer_version,scan_type_code` |
| 1.6 | **RỖNG**. Nếu có → xem Bước 1-bis |
| 1.8 | **`0`**. Nếu `> 0` → ⛔ **STOP NGAY**: purge theo bất hoạt đang bật mà chưa có tombstone |

### Bước 1-bis — nếu 1.6 có INVALID index
Tooling **không** tự phát hiện (xác nhận: `apply-migrations.ps1:117-122` ghi *"Recovering means dropping it by hand"*). Thủ công:
```sql
SELECT indexrelid::regclass FROM pg_index WHERE NOT indisvalid;
DROP INDEX CONCURRENTLY <tên_index>;
```
Rồi chạy lại 1.6 cho tới khi rỗng. Nếu vẫn fail → ⛔ STOP + REPORT.

---

## Bước 2 — Backup → Restore → Smoke (hard gate)

```powershell
# 2.1 Backup
./backup-postgres.ps1 -ComposeFile <compose> -ComposeEnvFile $ENV -OutputDirectory ./p0-backup

# 2.2 Restore vào instance TẠM (không phải staging đang chạy)
./restore-postgres.ps1 -DatabaseUrl <url-instance-tam> -DumpFile ./p0-backup/<file>
```
```bash
# 2.3 Smoke test trên DB ĐÃ RESTORE — 10 bước end-to-end
./smoke-test.sh --env-file /path/to/.env.restored
```

`smoke-test.sh` bao phủ: provision site → license assertion → enroll device → acquire lease → **ingest behind fence** → **replay cùng Idempotency-Key** → change feed → snapshot → 5 negative case → release lease.

| Kết quả | Hành động |
|---|---|
| Cả 3 bước PASS | ✅ Ghi thời gian restore → đây là **RTO đo được** (OD-8) |
| Bất kỳ bước FAIL | ⛔ **STOP RELEASE** |

⛔ **Tuyệt đối không migration trước khi 2.1–2.3 PASS.**

---

## Bước 3 — Infrastructure Gate

| # | Kiểm | Lệnh gợi ý | Kỳ vọng |
|---|---|---|---|
| 3.1 | Internet → DB:5432 | từ máy ngoài: `nc -vz <public-ip> 5432` | **BLOCKED / timeout** |
| 3.2 | API → DB qua WireGuard | từ VPS-API: `nc -vz <db-wg-ip> 5432` | **OPEN** — *chỉ multi-host* |
| 3.3 | TLS VerifyFull | connection string dùng `SSL Mode=VerifyFull` | **PASS** — *chỉ multi-host* |
| 3.4 | Certificate hostname | hostname trong cert khớp host đang kết nối | **khớp** — *chỉ multi-host* |
| 3.5 | API → PostgreSQL | `./dc.sh --env-file "$ENV" logs --tail 50 api` + health `ready` | **PASS** |
| 3.6 | Docker bind | `docker ps --format '{{.Ports}}'` cho postgres | **không** bind `0.0.0.0:5432`; nếu cần thì bind nội bộ |

Bất kỳ dòng nào FAIL → ⛔ **STOP**.

**3.2–3.4 giả định tô-pô multi-host** — API và PostgreSQL ở hai máy, nối bằng WireGuard, TLS bọc chặng giữa hai máy đó. Staging hiện **không** chạy tô-pô này: `postgres` chỉ nằm trên network `data` khai báo `internal: true`, mở cổng bằng `expose: "5432"` chứ không `ports:`, và API kết nối bằng `Host=postgres` trong cùng network. Không có chặng liên-máy nào để bọc, nên trên single-host **3.2, 3.3, 3.4 là N/A** — ghi `N/A (single-host)` chứ không ghi PASS, và chúng trở lại bắt buộc ngay khi production tách API với DB ra hai máy.

Đổi lại, single-host phải chứng minh 3.1 và 3.6 chặt hơn: `internal: true` khiến Docker không cấp route ra ngoài cho network `data`, và `expose` (khác `ports`) không mở cổng nào trên host — hai điều này chặn ở mức cấu hình compose, không phụ thuộc firewall còn đúng hay không.

---

## Bước 4 — Baseline hiệu năng (tách interactive vs bulk)

⛔ **CẤM gộp chung.** Hai bộ đo riêng, **cùng site**, **khác waybill**:

| Bộ | Endpoint | Tải |
|---|---|---|
| **A. Interactive** | `POST /api/v1/sites/{siteId}/jms/observations` (không `X-Leader-Term`) | 10 concurrent · 50 concurrent |
| **B. Bulk** | `POST /api/v1/sites/{siteId}/jms/ingest` (có `X-Leader-Term`) | 10 concurrent · 50 concurrent |

Mỗi bộ ghi **5 metrics**: `counter_lock_wait` (p50/p95/max) · `transaction_p95` · `commit_p95` · `throughput (req/s)` · `error_rate`.

Đo `counter_lock_wait` phía DB trong lúc chạy tải:
```sql
SELECT wait_event_type, wait_event, count(*)
  FROM pg_stat_activity
 WHERE state = 'active' AND datname = current_database()
 GROUP BY 1,2 ORDER BY 3 DESC;

SELECT relation::regclass, mode, granted, count(*)
  FROM pg_locks WHERE relation = 'site_change_counters'::regclass
 GROUP BY 1,2,3;
```

> Mỗi request phải mang `Idempotency-Key` **duy nhất** (8–128 ký tự), nếu không sẽ bị replay và số đo vô nghĩa.
> ⛔ Chạy trên **staging**, không chạy tải lên production.

**Đây là mốc so sánh bắt buộc cho P1 và P7.** Không có baseline → không được vào P1.

---

## Bước 5 — Owner ký Owner Decisions

Dùng mẫu ký ở **§25** của v4.6. Ba mục **chặn P1**:

| OD | Cần gì để ký |
|---|---|
| **OD-1** Terminal scan codes | Kết quả Bước 0 (+ ghi rõ nguồn dữ liệu là staging; hẹn xác nhận lại trên production trước P6) |
| **OD-2** Horizon | `45d / 60d / 90d / ≥2 năm`, giữ bất biến `ingest < event_retention` |
| **OD-6** Mốc terminal retention | **A** `source_event_at` (cần test late-terminal) hoặc **B** server observed time |

Năm mục còn lại (OD-3, 4, 5, 7, 8) có thể ký cùng lúc; **OD-8** điền RPO thật + RTO đo được ở Bước 2.

---

## Bảng Gate P0

| # | Hạng mục | PASS khi | Kết quả |
|---|---|---|---|
| G1 | Bằng chứng OD-1 | Có bảng từ vựng + ứng viên terminal | ✅ — 24 mã thật, ứng viên `100` (98,0%). Xem [od1-scan-vocabulary-evidence.vi.md](./od1-scan-vocabulary-evidence.vi.md) |
| G2 | Preflight | 8 cột `missing`; không mismatch; 1.6 rỗng; **1.8 = 0** | ⛔ **QUÁ HẠN** — P1 đã áp dụng 10/09 |
| G3 | Backup | Tạo được file dump | ✅ |
| G4 | Restore | Restore thành công vào instance tạm | ✅ |
| G5 | Smoke trên DB restored | Toàn vẹn bản restore + 10 bước PASS | ✅ |
| G6 | Infra 3.1–3.6 | 3.1/3.5/3.6 PASS; 3.2–3.4 N/A trên single-host | ✅ |
| G7 | Baseline A (interactive) | Đủ 5 metrics × 2 mức tải | ✅ |
| G8 | Baseline B (bulk) | Đủ 5 metrics × 2 mức tải | ✅ |
| G9 | OD ký | OD-1, OD-2, OD-6 tối thiểu | ☐ |

**Chỉ khi G1–G9 đều ✅ mới được đề xuất mở P1.**

**Câu trên đã bị vi phạm về thứ tự, và không thể sửa bằng cách đo lại.** Migration `007`/`008`/`009` vào staging lúc **10/09 01:19**, trong khi G1 và G2 chưa bao giờ ✅ — trái với chính điều **1** của mục "⛔ P0 KHÔNG được làm" ở cuối tài liệu này. Hệ quả: G2 nay là một gate tiền-P1 chạy trên môi trường hậu-P1, nên nó **không thể pass mà cũng không thể fail có nghĩa** — đó là quyết định của Owner, không phải việc đo thêm. Hai đường xử lý:

- **Chấp nhận** — ghi nhận P1 đã mở trên staging, hạ G2 xuống "N/A (đã qua thời điểm)", và ký OD dựa trên bằng chứng hiện có.
- **Dựng lại sạch** — restore một staging tiền-migration (dump đã có, RTO 2,3 s), chạy lại G2 đúng thứ tự, rồi mới áp migration.

G1 thì đường nào cũng không giải được bằng staging: bảng dưới cho thấy staging **chưa từng nhận một lần quét thật nào**, nên chờ thêm cũng không sinh ra ứng viên terminal.

### Bằng chứng đã ghi nhận — 10/09/2026

Hai môi trường khác nhau, nên mỗi số dưới đây đều ghi kèm nơi đo:

- **Staging VPS** — `https://dev.jmsauto.online`, single-host Docker Compose, DB đang phục vụ.
- **VM Test Lab** — máy ảo trong LAN của Owner, dựng riêng để restore vào **instance tạm**.

| Gate | Bằng chứng | Đo tại |
|---|---|---|
| G1 ⛔ | Từ vựng quét trên staging chỉ có **2 mã**: `110/state_transition` (7 802) và `98/inventory` (2). Nhưng **7 800 trên 7 804 sự kiện là waybill `BENCH-%`** — cặn của chính lượt đo baseline 07/09, mà harness hardcode `code: 110`; 4 sự kiện còn lại là fixture `SMOKE-WB-001` của `smoke-test.sh`. **14/14 site đều là bench hoặc smoke.** Không có ứng viên terminal để trình | Staging VPS |
| G2 ⛔ | `p0_preflight.sql` báo 2 dòng `*** STOP ***`, và báo **đúng**: nó khẳng định 8 cột P1 cùng `waybill_tombstones` phải **vắng mặt**, trong khi migration `007`/`008`/`009` đã áp dụng lúc **10/09 01:19**. Gate tiền-P1 chạy sau khi P1 đã vào | Staging VPS |
| G3 ✅ | `backup-postgres.sh` tạo dump sạch: Full **1,9 MB**, Critical (`--critical-only`) **169 KB** | Staging VPS |
| G4 ✅ | Restore vào PostgreSQL 16 trên **instance tạm**: **RTO Critical 3,4 s** (3 lần đo, DB quiesced), schema/row count/sequence khớp, 0 index INVALID, đủ 9 marker migration, checksum khớp **100%**. Đo lại trên **phần cứng VPS**: 1,44 / 0,68 / 0,57 s ad-hoc và **2,30 s** qua chính script đã ship (gồm cả `docker cp`) | VM Test Lab + Staging VPS |
| G5 ✅ | **Một lần chạy ghép cả hai nửa**: dựng stack Docker Compose thứ hai (`COMPOSE_PROJECT_NAME=g5`) trên VPS, restore dump critical vào đó, rồi chạy `smoke-test.sh` với **cả SQL lẫn HTTP trỏ vào chính instance restored** — **24/24 assertion PASS**. Toàn vẹn bản restore: 14 bảng, 0 index INVALID, 9 migration, 13 site | Staging VPS |
| G6 ✅ | 3.1 `TcpTestSucceeded: False` từ ngoài Internet, UFW active · 3.5 `/health/ready` báo `postgres: Healthy` · 3.6 `ss -tulpn` không có listener `5432` trên host · 3.2–3.4 **N/A (single-host)** | Staging VPS |

**G1 chặn vì thiếu dữ liệu, không vì thiếu công cụ.** Truy vấn từ vựng chạy được và trả về kết quả sạch; vấn đề là mọi hàng nó đếm đều do chính bộ test sinh ra. Cả hai mã có mặt đều là mã **do harness hardcode**: `110` trong `baseline_load.py`, `110` và `98` trong fixture của `smoke-test.sh`. Số hàng đã qua cửa sổ settle 14 ngày là **0** — lịch sử staging chỉ trải từ 08/09 đến 10/09 — nên **chờ thêm cũng không có gì để chờ**: nguồn duy nhất sinh ra dữ liệu là bộ test, và bộ test luôn sinh đúng hai mã đó. Muốn có ứng viên terminal phải lấy từ **production hoặc từ một bản trích JMS thật**, không lấy từ staging ở bất kỳ thời điểm nào. Đây cũng là lý do điều **4** của mục "⛔ P0 KHÔNG được làm" phải giữ nguyên: seed một mã terminal đoán được sẽ biến G1 thành vòng lặp tự khẳng định.

**G5 nay đóng đúng chữ.** Lần ký trước là hợp bằng chứng từ hai máy: bản restore chứng minh ở VM Lab, 10 bước hợp đồng chứng minh ở Staging, không lần nào ghép cả hai. Khoảng trống đó đã được lấp: `--base` của `smoke-test.sh` **chỉ đổi hướng HTTP**, phần SQL vẫn đi qua `docker compose exec`, nên muốn ghép phải dựng nguyên một compose project thứ hai chứ không chỉ đổi URL. Đã dựng, đã chạy, 24/24.

**RTO nay có số đo trên phần cứng Staging.** Cảnh báo cũ — "3,4 s đo ở VM Lab, không phải VPS" — đã được gỡ bằng 4 lần đo ở trên. Con số dùng cho OD-8 nên là **2,30 s**, vì đó là lần duy nhất chạy qua đúng script sẽ dùng khi có sự cố thật. Production vẫn có thể khác.

**24/24 PASS đó đi qua đường HMAC, không phải đường RSA.** Staging đang bật `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true` và `.env.staging` **không có** `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY`, nên `RsaLicenseAssertionValidator` tắt và mọi assertion trong lượt smoke đều là HMAC `v1.`. Cờ đó **đã ở sẵn trên staging từ trước** — lượt này chỉ phát hiện chứ không bật, và cũng không tắt để tránh làm hỏng môi trường đang phục vụ. Điều cần ghi rõ: **đường xác thực mà production sẽ dùng chưa từng được chạy thử một lần nào.** Đây là một hạng mục riêng cho P1, không phải một dòng của G5.

**G6 tick với 3 dòng N/A, không phải 3 dòng PASS.** WireGuard và `SSL Mode=VerifyFull` (3.2–3.4) là thiết kế cho production multi-host; staging chạy single-host nên không có chặng liên-máy để bọc — chi tiết ở Bước 3. Ngay khi production tách API và DB ra hai máy, ba dòng đó trở lại bắt buộc và G6 phải đo lại từ đầu.

### G7 / G8 — baseline đo lại ngày 10/09/2026

Báo cáo 07/09 để G7 và G8 ở **FAIL**, và nói rõ đó là FAIL của *kế hoạch đo*, không phải của hệ thống: tải đã chạy sạch nhưng **3 trên 5 metrics chưa từng đo được ở bất kỳ mức tải nào** — `counter_lock_wait (p50)`, `transaction_p95`, `commit_p95`. Lượt đo này nhắm đúng ba metrics đó.

Đo trên **stack Docker Compose thứ hai** dựng riêng ở VPS rồi xoá, restore từ dump full (7 804 scan event, 7 802 projection, 13 site) — **không** chạy tải lên stack đang phục vụ. Trước mọi lượt đo đã chạy **positive control bắt buộc** của `lock_wait_sampler.sql`: **36/60 mẫu thấy waiter, `max_wait_ms` leo tới 7 062 ms** ⇒ dụng cụ không mù. Overlap sampler × cửa sổ tải được **tính**, không giả định; cả bốn lượt phủ trọn 120 s.

`counter_lock_wait` ở đây là **`AccessExclusiveLock on tuple` của `site_change_counters`** (OID xác thực tại chỗ = 16457). Báo cáo 07/09 đã *loại* các dòng tuple-lock vì sợ trùng đếm với `ShareLock on transactionid`; ở cả bốn lượt này `ShareLock on transactionid` = **0**, nên không có gì để trùng.

| Gate | Mức tải | Sàn | req / ok / lỗi | rps | `counter_lock_wait` p50 / p95 / max | `transaction_p95` | `commit_p95` |
|---|---|---|---|---|---|---|---|
| **G7** A interactive | 10 | 5 ms | 932 / 932 / **0** | 7,7 | < 5 ms / < 5 ms / < 5 ms — **0 lần chờ** | 1 388,7 ms | 50,0 ms |
| **G7** A interactive | 50 | 5 ms | 1 000 / 1 000 / **0** | 8,3 | < 5 ms / < 5 ms / **568,0 ms** — 47 lần (4,70 %) | 2 452,0 ms | 61,2 ms |
| **G8** B bulk | 10 | 5 ms | 886 / 886 / **0** | 7,4 | < 5 ms / < 5 ms / < 5 ms — **0 lần chờ** | 1 714,8 ms | 57,4 ms |
| **G8** B bulk | 50 | 100 ms | 950 / 950 / **0** | 7,9 | < 100 ms / < 100 ms / **816,0 ms** — 11 lần (1,16 %) | 444,2 ms | 62,5 ms |

Lease ở chế độ bulk giữ được nguyên vẹn: `bulk-10` 29 renew / **0 fail**, `bulk-50` 30 renew / **0 fail**, `http_409 = 0` ở cả hai.

**Cách hạ sàn quan sát — và cái giá của nó.** Sàn 100 ms của báo cáo cũ là **kiểm duyệt trái**: nó vứt mọi lần chờ ngắn hơn 100 ms, nên `p50` không thể tồn tại. Hạ `deadlock_timeout` xuống 5 ms biến server log từ mẫu thành **census** và siết cận `p50`/`p95` chặt hơn **20 lần**. Nhưng chính nó phá lượt đo ở mức tải cao — cùng một tải, chỉ đổi sàn:

| Sàn `deadlock_timeout` | ok / 409 | `lease_renew_failures` | client p50 |
|---|---|---|---|
| 5 ms | 197 / **803** | 27 | 1 140,6 ms |
| 25 ms | 905 / 95 | 3 | 305,9 ms |
| **100 ms** | **950 / 0** | **0** | **63,9 ms** |

Ở concurrency 50 chế độ bulk, 5 worker cùng đập vào **một dòng** `site_change_counters`; sàn 5 ms bắt bộ dò deadlock chạy mỗi 5 ms cho từng backend đang chờ, và toàn bộ lease sập theo. Vì vậy `bulk-50` chỉ hợp lệ ở sàn 100 ms, và `p50`/`p95` của nó vẫn là **cận**, không phải số đo. Ba điểm tải còn lại đo được ở sàn 5 ms. **Ai đo lại phải chọn sàn theo mức tải, không dùng một sàn cho cả bốn lượt.**

**Hai điều chưa đổi so với 07/09.** Thứ nhất, `throughput` vẫn là **trần tự áp**: 7,4–8,3 rps là đúng mục tiêu `--target-rps 8`, và `IngressRateLimitMiddleware` chặn ở 600 req/phút cho mỗi IP nguồn, nên **không thể tìm điểm bão hoà từ một IP** — con số này chứng minh hệ chịu được ≥ 8 rps chứ không định vị được trần của nó. Thứ hai, `client_latency` không phải độ trễ của hệ ở trạng thái sạch: `log_min_duration_statement = 0` ghi ~1 150 dòng/giây qua log driver của Docker trong suốt lượt đo.

**Sampler và server log cho hai con số khác nhau, và cả hai đều đúng.** Ví dụ `interactive-50`: log ghi lần chờ hoàn tất dài nhất là 568,0 ms, còn sampler bắt được một backend đang chờ **1 425,5 ms**. Log chỉ đếm lần chờ đã `acquired`; sampler bắt cả lần đang dở, nhưng đếm **mọi** khoá chưa cấp trong database chứ không riêng `site_change_counters`. Đừng gộp hai cột này làm một.

---

## Mẫu báo cáo P0

```
P0 REPORT — <ngày> — môi trường: staging
1. OD-1 evidence:  <số scan type phát hiện> | ứng viên terminal: <danh sách + pct_final>
                   scan_type_code NULL: <n>/<total>
2. Preflight:      cột missing <n>/8 | mismatch: <none|chi tiết> | invalid index: <none|...>
                   retention_policies('waybill_projections'): <0|CẢNH BÁO>
3. Backup:         <path> | kích thước <..>
4. Restore:        PASS/FAIL | thời gian <..>  → RTO đo được
5. Smoke:          PASS/FAIL | bước fail: <..>
6. Infra:          3.1<..> 3.2<..> 3.3<..> 3.4<..> 3.5<..> 3.6<..>
7. Baseline A:     lock_wait p95 <..> | txn p95 <..> | commit p95 <..> | tps <..> | err <..>
8. Baseline B:     lock_wait p95 <..> | txn p95 <..> | commit p95 <..> | tps <..> | err <..>
9. OD đã ký:       OD-1 <..> OD-2 <..> OD-6 <..> (OD-3/4/5/7/8: <..>)
10. Kết luận:      ĐỦ ĐIỀU KIỆN MỞ P1  /  STOP (lý do: ...)
```

---

## ⛔ P0 KHÔNG được làm

1. Không chạy migration (kể cả `apply-migrations.sh`).
2. Không sửa code ứng dụng.
3. Không seed `retention_policies('waybill_projections')`.
4. Không seed terminal scan code vào `jms_event_policies`.
5. Không chạy tải lên production.
6. Không tự chọn giá trị cho bất kỳ OD nào.
7. Không "sửa tạm" khi preflight mismatch — phải STOP + REPORT.
