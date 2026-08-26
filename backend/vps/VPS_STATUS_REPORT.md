# 🛰️ Hiện trạng hạ tầng VPS DataHub (Staging) — bản đã che

> ⚠️ **Repo `Datt03-sss/AutoJMS` là PUBLIC. File này là bản ĐÃ CHE và phải giữ nguyên như vậy.**
>
> Chi tiết định danh hạ tầng — hostname, IP, tài khoản vận hành, đường dẫn file secrets, thư mục
> deploy, container ID, ngưỡng fail2ban/UFW — thuộc `backend/vps/VPS_STATUS_REPORT.private.md`,
> **nằm ngoài git** (`.gitignore`). File đó chỉ tồn tại trên máy Owner và máy Antigravity.
>
> Lý do tách: từng mục riêng lẻ nghe vô hại, nhưng **tổ hợp** IP + tên tài khoản + `sudo NOPASSWD`
> + danh sách cổng mở + ngưỡng ban của fail2ban là một bản đồ trinh sát hoàn chỉnh. Luật gốc:
> [DEPLOY_EXECUTION_CHECKLIST.vi.md §6](../datahub/deploy/DEPLOY_EXECUTION_CHECKLIST.vi.md).
>
> **Antigravity:** ghi chi tiết vào `*.private.md`, rồi cập nhật file này. Nếu giá trị hạ tầng lọt
> vào file tracked, `eng/harness/check-secrets.ps1` (phần 4 — infra denylist) **fail gate** thay vì
> pass im lặng như trước.

| | |
|---|---|
| **Cập nhật lần cuối** | 26/08/2026 (UTC+7) |
| **Trạng thái** | ✅ Staging Operational (Healthy) |
| **Người cập nhật** | Antigravity (VPS Infrastructure Operator) |
| **Bản đầy đủ** | `backend/vps/VPS_STATUS_REPORT.private.md` (ngoài git) |

---

## 1. Hệ thống & bảo mật

Cấu hình hardening **khớp đúng** `backend/datahub/deploy/bootstrap-vps.sh` — script đó là công
thức, đọc nó để biết chi tiết thay vì lặp lại trạng thái của một host cụ thể ở đây.

| Hạng mục | Trạng thái |
|---|---|
| Hệ điều hành | Ubuntu Server 24.04 LTS (Noble) |
| Đồng bộ đồng hồ | ✅ `System clock synchronized: yes` (`systemd-timesyncd`), múi giờ `Asia/Ho_Chi_Minh` |
| Xác thực SSH | ✅ Chỉ SSH key (Ed25519); mật khẩu bị cấm hoàn toàn |
| Tường lửa UFW | ✅ Active — chỉ 3 cổng HTTP/HTTPS/SSH mở, đúng như bootstrap script |
| `fail2ban` / `unattended-upgrades` | ✅ Cả hai active cho sshd |
| Bề mặt tấn công DB | ✅ PostgreSQL nằm trong Docker network `internal: true`, **không thò ra host** (`ss -tulpn` xác nhận) |

---

## 2. Container runtime

| Thành phần | Image | Trạng thái |
|---|---|---|
| Reverse proxy | `caddy:2.10-alpine` | Up (healthy) — TLS tự động qua ACME HTTP-01 cho `https://dev.jmsauto.online` |
| API | `autojms-datahub-api:local` | Up (healthy) — ASP.NET Core (.NET 10), build multi-stage từ monorepo |
| Database | `postgres:16-alpine` | Up (healthy) — chỉ hiển thị trong network nội bộ |

- **Secrets**: sinh trực tiếp trên VPS (4 giá trị 32-byte hex độc lập), lưu trong file env quyền
  `600` ngoài repo. Không có giá trị nào nằm trong git.
- **Deadlines**: connection string đã ghim `Options=-c statement_timeout=30s -c
  idle_in_transaction_session_timeout=60s` — khớp B14.
- ⚠️ Image vẫn là tag `:local`, **chưa có digest để rollback** (Cổng 3 còn mở).

---

## 3. Database & migrations

Đã áp đủ **6/6** forward-only migrations — **13 bảng, 26 index active**:

| Migration | Nội dung chính |
|---|---|
| `001_core` | 12 bảng nghiệp vụ, function `create_datahub_site` |
| `002_seed_policies` | JMS event policies hạt nhân |
| `003_seed_retention` | Retention clock mặc định (**không** seed `waybill_projections` — B12 mặc định tắt) |
| `004_projection_slot_payloads` | Projection slot payloads |
| `005_change_retention_floor` | Prune cursor floor cho change feed |
| `006_revocation_and_retention_indexes` | Bảng `revoked_device_credentials` + 4 index |

`backend/backend-schema-dump.sql` trong repo **phản chiếu đúng 001..006** (cập nhật ở `7f4497f`).

---

## 4. `EXPLAIN (ANALYZE, BUFFERS)` — cả 3 câu SQL của B12 đã kiểm chứng

Đo trực tiếp trên container PostgreSQL 16 Alpine của staging. Đây là bằng chứng đóng rủi ro
"SQL mới chưa từng chạy trên PostgreSQL thật" — rủi ro nghiêm trọng vì
`RetentionHostedService` bắt mọi exception và chỉ `LogWarning`, nên một lỗi cú pháp sẽ làm
retention **âm thầm ngừng chạy** chứ không làm sập API.

| Câu lệnh | Buffers | Thời gian | Kết luận |
|---|---|---|---|
| Fast-exit probe (`retention_policies`) | `shared hit=1` | 0.062 ms | ✅ Thoát tức thì khi B12 tắt, **không chạm** `waybill_projections` — đúng ý định |
| `candidatesSql` (`DeleteProjectionsAsync`) | `shared hit=15` | 0.355 ms | ✅ Plan hợp lệ |
| Bộ lọc `CASE` tombstone (`DeleteChangesAsync`) | `shared hit=3` | 0.233 ms | ✅ Cú pháp và `GroupAggregate` khớp |

---

## 5. Kiểm thử end-to-end & diễn tập phục hồi

1. **`smoke-test.sh`: 24/24 PASS** qua public HTTPS — tạo site → mint assertion → enroll → lease
   → ingest idempotency → changes cursor → snapshot → 5 negative check → release lease.
2. **Diễn tập restore database (Cổng 7 / H3)**: `backup-postgres.ps1` tạo dump sạch →
   `restore-postgres.ps1 -AllowExistingData` khôi phục 100% → `apply-migrations.ps1` xác thực
   nguyên vẹn 6 migration marker → `smoke-test.sh` chạy lại sau restore **24/24 PASS**.
3. **Control plane**: 4 seed manifest (`runtime-policy.*.json`, `tier-definitions.json`) đã
   publish, ETag xác thực qua public HTTPS.

---

## 6. Kiểm thử trên máy dev

- `dotnet build AutoJMS.slnx -c Release` → **0 Warning, 0 Error**
- `dotnet test` → **365/365 PASS** (`AutoJMS.Tests` 186, `AutoJMS.DataHub.Api.Tests` 179)
- `verify.ps1` → **OVERALL: ✅ ALL GATES PASSED**

---

## 7. Phối hợp giữa các agent

1. **Claude Code** đọc file này để nắm hạ tầng backend, migration đã áp và kết quả kiểm chứng.
   Cần giá trị định danh cụ thể (IP, tài khoản, đường dẫn) thì **hỏi Owner** — chúng không nằm
   trong git.
2. **Antigravity** cập nhật `*.private.md` **và** file này sau mỗi task VPS. Không đưa giá trị
   định danh vào file này.
3. Chi tiết quy ước: [.agent/rules/09-cross-agent-collaboration.md](../../.agent/rules/09-cross-agent-collaboration.md).
