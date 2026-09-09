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
| 3.2 | API → DB qua WireGuard | từ VPS-API: `nc -vz <db-wg-ip> 5432` | **OPEN** |
| 3.3 | TLS VerifyFull | connection string dùng `SSL Mode=VerifyFull` | **PASS** |
| 3.4 | Certificate hostname | hostname trong cert khớp host đang kết nối | **khớp** |
| 3.5 | API → PostgreSQL | `./dc.sh --env-file "$ENV" logs --tail 50 api` + health `ready` | **PASS** |
| 3.6 | Docker bind | `docker ps --format '{{.Ports}}'` cho postgres | **không** bind `0.0.0.0:5432`; nếu cần thì bind nội bộ |

Bất kỳ dòng nào FAIL → ⛔ **STOP**.

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
| G1 | Bằng chứng OD-1 | Có bảng từ vựng + ứng viên terminal | ☐ |
| G2 | Preflight | 8 cột `missing`; không mismatch; 1.6 rỗng; **1.8 = 0** | ☐ |
| G3 | Backup | Tạo được file dump | ☐ |
| G4 | Restore | Restore thành công vào instance tạm | ☐ |
| G5 | Smoke trên DB restored | 10 bước PASS | ☐ |
| G6 | Infra 3.1–3.6 | Toàn bộ PASS | ☐ |
| G7 | Baseline A (interactive) | Đủ 5 metrics × 2 mức tải | ☐ |
| G8 | Baseline B (bulk) | Đủ 5 metrics × 2 mức tải | ☐ |
| G9 | OD ký | OD-1, OD-2, OD-6 tối thiểu | ☐ |

**Chỉ khi G1–G9 đều ✅ mới được đề xuất mở P1.**

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
