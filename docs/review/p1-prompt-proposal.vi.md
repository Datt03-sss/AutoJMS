# P1 Prompt Proposal — Server Safety (soạn sẵn, CHƯA được chạy)

> **Trạng thái**: 🔒 **KHOÁ**. Chỉ được đưa cho Claude Code khi **cả hai** điều kiện đúng:
> 1. **P0 gate G1–G9 đều PASS** ([p0-execution-runbook.vi.md](./p0-execution-runbook.vi.md))
> 2. **Owner đã ký OD-1, OD-2, OD-6** (§25 của v4.6)
>
> Nếu một trong hai chưa đủ → ⛔ **không dán prompt này**. Đưa sớm là vi phạm chính hợp đồng nó thi hành.

---

## Cách dùng

Owner copy toàn bộ khối "PROMPT" bên dưới vào Claude Code, sau khi **điền 3 giá trị đã ký** vào phần `[OWNER FILL]`. Prompt đã được viết theo `CLAUDE.md` của repo (Skills First, Minimal Edit Rule, Protected Files, Final Report Format).

---

## [OWNER FILL] — điền trước khi dán

```
OD-1 TERMINAL SCAN TYPES (từ Bước 0 của P0, đã ký):
  - key theo: [ scan_type_code | scan_type_name | cả hai ]     ← chọn theo kết quả query 0.4
  - danh sách: ______________________________________________
  - nguồn dữ liệu: staging (ngày ___) ; xác nhận lại production trước P6: [chưa]

OD-2 HORIZON (đã ký):
  - normal ingest horizon: ______ ngày   (mặc định đề xuất 45)
  - future skew:           ______ phút   (mặc định đề xuất 5)
  - (event retention 60d giữ nguyên — KHÔNG đổi ở P1)

OD-6 TERMINAL CLOCK (đã ký):
  - terminal_at = [ A: source_event_at | B: server observed time ]
```

---

## PROMPT (dán từ đây)

````
Bạn đang làm việc trên repo AutoJMS, branch `main`, theo `CLAUDE.md` và `AGENTS.md`.

BỐI CẢNH
Đây là hệ thống brownfield đang chạy. Hợp đồng ràng buộc: `docs/review/streaming-v4.6-contract.vi.md`.
Đọc §1 (baseline khoá), §26 (đã có trong code — cấm rewrite), §28 (22 giả định bị cấm), §29 (ràng buộc)
TRƯỚC KHI viết bất kỳ dòng nào. P0 đã PASS; Owner đã ký OD-1/OD-2/OD-6 với giá trị ghi ở cuối prompt.

NHIỆM VỤ: thực thi P1 — Server Safety. Chỉ ngần này, không hơn.

────────────────────────────────────────────────
A. MIGRATION (additive, forward-only)

A1. `backend/datahub/migrations/007_event_metadata.sql`
    ALTER TABLE waybill_scan_events
        ADD COLUMN IF NOT EXISTS event_kind            text,
        ADD COLUMN IF NOT EXISTS reducer_version       smallint NOT NULL DEFAULT 1,
        ADD COLUMN IF NOT EXISTS normalizer_version    smallint NOT NULL DEFAULT 1,
        ADD COLUMN IF NOT EXISTS source_schema_version text     NOT NULL DEFAULT 'v2';
    + INSERT INTO schema_migrations ('007_event_metadata') ON CONFLICT DO NOTHING;

    CẤM: đụng `event_occurred_at`, `ingested_at`, `fingerprint_version` (đã có, `001_core.sql:58`).

A2. `backend/datahub/migrations/008_terminal_tombstone.sql`
    ALTER TABLE waybill_projections
        ADD COLUMN IF NOT EXISTS is_terminal         boolean NOT NULL DEFAULT false,
        ADD COLUMN IF NOT EXISTS terminal_at         timestamptz,
        ADD COLUMN IF NOT EXISTS terminal_state_code integer,
        ADD COLUMN IF NOT EXISTS last_change_seq     bigint;      -- NULLABLE, xem C3
    CREATE TABLE IF NOT EXISTS waybill_tombstones (
        site_id uuid NOT NULL REFERENCES sites(id),
        waybill_no text NOT NULL,
        terminal_state_code integer,
        terminal_at timestamptz NOT NULL,
        purged_at timestamptz NOT NULL DEFAULT now(),
        PRIMARY KEY (site_id, waybill_no));
    + marker.

A3. `backend/datahub/migrations/009_terminal_index_notx.sql`
    -- no-transaction
    CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_waybill_projections_terminal_retention
        ON waybill_projections (site_id, terminal_at) WHERE is_terminal = true;
    + marker.
    Hậu kiểm BẮT BUỘC sau khi chạy: `SELECT indexrelid::regclass FROM pg_index WHERE NOT indisvalid;`
    phải RỖNG. Nếu không rỗng → DROP INDEX CONCURRENTLY rồi chạy lại; vẫn fail → STOP + REPORT.
    (apply-migrations đã hỗ trợ `_notx`, nhưng KHÔNG tự phát hiện invalid index.)

CẤM ở A: tạo `fingerprint_policies`; tạo `get_waybill_advisory_lock_key`; đụng `site_change_counters`.

────────────────────────────────────────────────
B. TERMINAL POLICY (theo OD-1 đã ký)

B1. Biểu diễn terminal theo đúng cách OD-1 đã chốt (code / name / cả hai).
    Nếu key theo name: so khớp phải bao gồm cả biến thể tiếng Trung, tham chiếu
    `DkchJourneyAnalyzer.Classify` (vd `快件签收`/"Ký nhận CPN", `退件签收`/"Ký nhận chuyển hoàn").
B2. FAIL-CLOSED: nếu policy không resolve được → `is_terminal` giữ `false`. Không đoán.
B3. CẤM seed bất kỳ mã nào ngoài danh sách Owner đã ký.

────────────────────────────────────────────────
C. INGEST DELTA (sửa TỐI THIỂU trong IngestRepository)

GIỮ NGUYÊN, không được đụng: thứ tự transaction; `site_change_counters ... FOR UPDATE`;
lease fencing 3 chốt; toàn bộ luồng idempotency; `INSERT ... ON CONFLICT DO NOTHING RETURNING id`;
ranh giới all-or-nothing.

C1. Horizon guardrail (tiền kiểm, NGOÀI transaction):
    - `source_event_at` phải UTC; `<= now + <future skew>`; `>= now - <ingest horizon>`.
    - Vi phạm = lỗi cứng → rollback cả batch → 422.
    - Fail-fast khi khởi động nếu `ingest_horizon >= event_retention` (đọc từ config).

C2. Anti-resurrection, theo đúng thứ tự này:
    a) INSERT event TRƯỚC (giữ nguyên câu lệnh hiện có) — để audit nhất quán.
    b) SAU đó mới kiểm tombstone + `is_terminal`.
    c) Nếu bị chặn: KHÔNG mutate projection, KHÔNG tăng version, KHÔNG sinh change,
       đếm vào `terminalLocked`. KHÔNG rollback cả batch (giống duplicate).
    Response mở rộng: { accepted, duplicates, terminalLocked, changed, firstSeq, lastSeq }.
    `firstSeq/lastSeq` phản ánh số waybill THAY ĐỔI, không phải số event.

C3. `last_change_seq`: chỉ set cho row do transaction này ghi. TUYỆT ĐỐI KHÔNG backfill.
    NULL nghĩa là "không biết", KHÔNG phải 0.

C4. Terminal lifecycle: khi event terminal thắng reducer →
    is_terminal=true; terminal_at=<theo OD-6>; terminal_state_code=<mã của event>;
    version++; change_seq++ (qua đúng discipline hiện có); dashboard_changes('upsert').

CẤM ở C: thêm đồng hồ thứ hai vào reducer (`ingested_at` là transaction-time, giống hệt nhau
cho mọi item trong một bulk → KHÔNG dùng làm tie-break — xem §2 và §28 mục 1–3).

────────────────────────────────────────────────
D. ADMIN REOPEN

Endpoint: POST /api/v1/sites/{siteId}/waybills/{waybillNo}/reopen
- Authz: admin/operator token. CẤM dùng DeviceCapability (enum chỉ có ReadSiteData/WriteSiteData;
  cả 3 role đều giữ cả hai nên capability KHÔNG phân biệt được admin).
- Header `Idempotency-Key` bắt buộc (8–128). Tái dùng bảng `idempotency_records`.
- same key + same body → replay exact response.
- same key + different body → 409 IDEMPOTENCY_KEY_REUSED.
- retry trùng: KHÔNG bump version lần 2, KHÔNG cấp change_seq lần 2, KHÔNG thêm dòng change.
- projection absent + tombstone present → 410 PROJECTION_ALREADY_PURGED.
- projection absent + tombstone absent → 404 PROJECTION_NOT_FOUND. CẤM tự tạo projection.
- Khi thành công: is_terminal=false; terminal_at=NULL; terminal_state_code=NULL;
  version++; change_seq++; dashboard_changes('upsert'); xoá tombstone nếu có.
- Audit: actor lấy từ authentication principal. Client chỉ gửi `reason`.

────────────────────────────────────────────────
E. TEST (bắt buộc, theo §27 của v4.6)
AT-T1 (fail-closed khi policy trống), AT-T2, AT-T3 (terminal→reopen→old event→new terminal),
AT-R1..AT-R6, AT-H1, AT-H2, AT-H4, AT-CS1, AT-LS1..AT-LS3, AT-CK1..AT-CK3.

────────────────────────────────────────────────
F. KHÔNG THUỘC PHẠM VI P1 (làm là sai)
Redis · Dashboard Epoch · bất kỳ thay đổi client nào · rewrite snapshot/change-feed/idempotency/retention ·
đổi predicate purge (đó là P6) · seed retention_policies('waybill_projections') ·
historical replay endpoint · fingerprint_policies · advisory lock function · refactor IChangeSequenceAllocator.

────────────────────────────────────────────────
G. XÁC MINH TRƯỚC KHI COMMIT
  dotnet restore .\AutoJMS.slnx
  dotnet build .\AutoJMS.slnx -c Release
  powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
Chạy migration trên STAGING trước, kèm hậu kiểm invalid index ở A3.
So throughput với baseline P0 (interactive và bulk RIÊNG): không được xấu hơn.

────────────────────────────────────────────────
H. QUY TẮC DỪNG
Nếu bất kỳ điều nào sau đây xảy ra → STOP và báo cáo, TUYỆT ĐỐI không tự hoà giải bằng cách đoán:
- Schema thật khác kỳ vọng ở A (type/nullability/default).
- `SELECT count(*) FROM retention_policies WHERE table_name='waybill_projections'` > 0.
- Hợp đồng v4.6 mâu thuẫn với code thật.
- Cần sửa Protected Files (`CLAUDE.md`) mà Owner chưa cho phép cho đúng việc này.
- Phải đoán bất kỳ giá trị OD nào.

I. BÁO CÁO
Xuất Final Report đúng 10 mục theo `CLAUDE.md` (Summary / Files Changed / Build-Verify Result /
Commit Message / Commit Hash / Pushed To / Behavior Changed / Behavior Intentionally Unchanged /
Owner Manual Test Checklist / Risks).

GIÁ TRỊ OWNER ĐÃ KÝ:
  OD-1: <dán từ [OWNER FILL]>
  OD-2: <dán từ [OWNER FILL]>
  OD-6: <dán từ [OWNER FILL]>
````

---

## Sau P1

P1 **không** bật purge và **không** đụng client. Thứ tự tiếp theo giữ nguyên §24 của v4.6:
**P2** server read → **P3** client disk cache (gồm blocker delete-marker §10) → **P4** write cutover (gate 5 điều kiện) → **P5** SignalR → **P6** retention (mới bật purge) → **P7** production validation.

⚠️ Nhắc lại điều kiện trước P6: **chạy lại query OD-1 trên production (read-only)** để xác nhận danh sách terminal đã ký từ staging vẫn đúng với dữ liệu thật.
