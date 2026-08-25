# Kế hoạch xây dựng backend AutoJMS và triển khai từng bước lên VPS

Ngày lập: 2026-08-25 · Nhánh: `main` · Trạng thái: **kế hoạch, chưa triển khai**

---

## Tài liệu này là gì và không là gì

**Là**: bản kế hoạch tổng, sắp xếp toàn bộ việc còn lại của backend thành các chặng có
điều kiện chặn (hard gate), theo đúng ba quyết định hạ tầng chủ sở hữu đã chốt ngày
2026-08-25.

**Không là**: bản hướng dẫn gõ lệnh. Phần thao tác chi tiết trên VPS đã có sẵn ở
[`backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md`](../../backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md)
(16 bước, hơn 1.000 dòng) và
[`backend/datahub/deploy/DEPLOY_EXECUTION_CHECKLIST.vi.md`](../../backend/datahub/deploy/DEPLOY_EXECUTION_CHECKLIST.vi.md).
Tài liệu này **trỏ tới** các bước đó, không chép lại, và chỉ viết thêm những chỗ hướng dẫn
kia **chưa phủ** hoặc **đã lệch** so với ba quyết định mới.

**Cách đọc**: mỗi chặng có mục *Điều kiện vào*, *Việc làm*, *Điều kiện ra*. Không được sang
chặng sau khi điều kiện ra chưa đạt. Các ô ⛔ là việc **chỉ chủ sở hữu** làm được (khoá,
mật khẩu, cấu hình Render, DNS, mua VPS) — agent không được thay.

---

## Phần 0 — Ba quyết định đã chốt và hệ quả

| # | Quyết định | Hệ quả lên kế hoạch |
|---|---|---|
| 1 | **License server (Node) KHÔNG lên VPS** — giữ ở Render hoặc host khác | Kế hoạch có **hai** đường triển khai song song, không phải một. VPS chỉ giữ nửa **public** của khoá assertion. |
| 2 | **Cả hai VPS đều mới, chưa có gì** | Bắt đầu từ provision + hardening OS (Bước 0–4 của guide), không phải từ audit hạ tầng cũ. |
| 3 | **Staging và production là HAI VPS riêng** | Không dùng §15.3 của guide ("nếu buộc phải dùng chung một VPS"). Cách ly bằng cả host lẫn `DATAHUB_CHANNEL`. |

**Lý do của (1)** — tách khoá: license server giữ nửa **private**, VPS chỉ giữ nửa **public**.
Nếu một box giữ cả hai nửa thì box đó bị chiếm là kẻ tấn công tự phát license vô hạn.
`RsaLicenseAssertionValidator` **chủ động từ chối** một khoá private (nó gọi
`rsa.ExportParameters(includePrivateParameters: true)` trong `try` và **thất bại nếu lệnh đó
thành công**) — đây là cơ chế thi hành ràng buộc trên, không phải lời khuyên.

**Lý do của (3)** — `DeviceAuthenticationMiddleware` trả `403 CHANNEL_MISMATCH` khi
`token.Channel != options.Channel`. Một trạm đã enroll ở staging **không thể** nói chuyện với
production dù có token hợp lệ. Hai VPS riêng biến ràng buộc mềm này thành ràng buộc cứng.

### 0.1 Một lệch lạc phải nói rõ

[`docs/datahub-deployment-options.md`](../datahub-deployment-options.md) dòng 6–7 và
[`backend/BACKEND_DEPLOY_STATUS.md`](../../backend/BACKEND_DEPLOY_STATUS.md) (ngày 2026-08-23)
ghi rằng **đã có** một VPS `dev.jmsauto.online` chạy `AutoJMS.DataHub.Api` + PostgreSQL (Docker)
+ Caddy, đã áp đủ 5 migration, `smoke-test.sh` đã pass.

Chủ sở hữu vẫn trả lời "VPS mới, chưa có gì". Kế hoạch này làm theo chỉ thị — **greenfield**.
Box `dev.jmsauto.online` do đó phải được xử lý dứt điểm ở Chặng C0: **hoặc** dùng lại nó làm
VPS staging (tiết kiệm, nhưng phải chấp nhận nó chưa hardening — xem 0.2), **hoặc** cho nghỉ
hẳn (huỷ DNS, tắt máy) để không còn một endpoint DataHub không ai theo dõi mà vẫn nhận enroll.
Bỏ lửng là lựa chọn duy nhất **không** được phép.

### 0.2 Điều `BACKEND_DEPLOY_STATUS.md` nói mà nay đã sai

Tài liệu đó (2026-08-23) ghi *"PUT /api/v1/admin/manifests/{objectPath} vắng mặt trong
`src/AutoJMS.DataHub.Api`, vắng trong `openapi/datahub-v1.yaml`"*. **Nay đã sai.** Kiểm chứng
ngày 2026-08-25:

- [`src/AutoJMS.DataHub.Api/Endpoints/ManifestEndpoints.cs:34`](../../src/AutoJMS.DataHub.Api/Endpoints/ManifestEndpoints.cs) —
  `endpoints.MapPut("/api/v1/admin/manifests/{**objectPath}", PublishAsync)`
- [`backend/datahub/openapi/datahub-v1.yaml:476`](../../backend/datahub/openapi/datahub-v1.yaml) —
  path đã có, `openapi-lint.ps1:157` còn ép nó phải yêu cầu `AdminBearer`.
- `src/AutoJMS.DataHub.Api/Manifests/` có `ManifestObjectPath.cs` + `ManifestStore.cs`.

Cũng vậy: `BACKEND_DEPLOY_STATUS.md` ghi *"VPS hardening chưa áp"* — điều đó vẫn đúng với box
`dev`, nhưng `bootstrap-vps.sh:191-217` **đã có sẵn** UFW + fail2ban; vấn đề là chưa **chạy**,
không phải chưa **viết**.

→ **Việc B0**: cập nhật `BACKEND_DEPLOY_STATUS.md` trước khi bất kỳ ai dùng nó làm căn cứ.
Một tài liệu trạng thái sai là thứ đắt hơn không có tài liệu trạng thái.

---

## Phần 1 — Backend gồm những gì (kiến trúc thực tế, không phải kiến trúc đã thiết kế)

Backend AutoJMS là **ba mặt phẳng**, chạy trên **hai loại host**:

```
┌─────────────────────────────────────────┐
│  MẶT PHẲNG LICENSE   (Render / off-VPS) │
│  backend/render-license-server (Node)   │
│                                         │
│  Giữ: JWT_PRIVATE_KEY,                  │
│       DATAHUB_LICENSE_ASSERTION_        │
│         PRIVATE_KEY  ← nửa private      │
│       Firebase service account          │
│                                         │
│  Làm: verify-license, heartbeat,        │
│       logout, phát assertion RS256,     │
│       googleSheets token grant          │
└───────────────┬─────────────────────────┘
                │ assertion RS256 (v1rs256.payload.sig)
                │ đi qua MÁY TRẠM, không phải server-to-server
                ▼
┌─────────────────────────────────────────┐   ┌──────────────────────┐
│  MẶT PHẲNG DỮ LIỆU        (VPS)         │   │  MÁY TRẠM (WinForms) │
│  src/AutoJMS.DataHub.Api + PostgreSQL   │◄──┤  DataHubClient       │
│                                         │   │  SQLite/SQLCipher    │
│  Giữ: DATAHUB_LICENSE_ASSERTION_        │   │  SignalR client      │
│         PUBLIC_KEY   ← chỉ nửa public   │   └──────────────────────┘
│       DATAHUB_DEVICE_TOKEN_SIGNING_KEY  │
│       DATAHUB_ENROLLMENT_PEPPER          │
│       DATAHUB_ADMIN_TOKEN                │
│                                         │
│  Làm: enroll, lease, ingest, changes,   │
│       snapshot, hub /hubs/site, retention│
├─────────────────────────────────────────┤
│  MẶT PHẲNG ĐIỀU KHIỂN  (cùng VPS)       │
│  ManifestStore trên volume manifests_data│
│  configs/runtime-policy.*.json          │
│  manifest/tier-definitions.json         │
│  manifest/version-latest.json           │
└─────────────────────────────────────────┘
```

Ba điều dễ hiểu sai về sơ đồ này, cần chốt trước khi lập lịch:

1. **Assertion đi qua máy trạm.** Render không gọi VPS. Trạm gọi `verify-license` → nhận
   assertion + `DATAHUB_API_BASE_URL` → tự `POST /api/v1/devices/enroll`. Nghĩa là **VPS không
   cần biết Render ở đâu**, và Render **không cần** IP allowlist của VPS. Nhưng cũng nghĩa là
   một trạm ngoại tuyến với Render thì không enroll được, dù VPS sống.
2. **Mặt phẳng điều khiển nằm trên VPS, không nằm trên Render.** Chính sách tier
   (`runtime-policy.*.json`) và `tier-definitions.json` được phục vụ bởi DataHub API. Do đó
   **VPS staging và VPS production có hai bản seed riêng** — phải publish hai lần, không
   copy được đường tắt.
3. **Không có Windows Service riêng.** Fetch chạy trong tiến trình UI của AutoJMS, giữ quyền
   ghi bằng `POST /api/v1/sites/{siteId}/lease/{acquire,renew,release}`. Không có PG function
   canonical writer, không có RLS — handler của endpoint ingest là **người ghi duy nhất**, phạm
   vi lấy từ device token. Doorbell SignalR phát trực tiếp từ handler ingest, **không** qua
   LISTEN/NOTIFY.

### 1.1 Tải thực tế — VPS không bị chặn ở throughput

Từ [`docs/datahub-deployment-options.md`](../datahub-deployment-options.md) §1:
`BatchSize = 40` vận đơn/request, song song 8 caller, governor toàn app 12 POST JMS đồng thời,
chu kỳ tracking mặc định 30 phút.

`requests_per_cycle = ceil(N / 40)`. Với **N = 10.000 vận đơn**: 250 request / 30 phút =
**8,3 request/phút**.

Kết luận cho việc chọn máy: **sizing không do CPU quyết định**, mà do *working set của
PostgreSQL* và *retention*. Bảng ở §1.1 của guide (staging 2 vCPU / 4 GB / 40 GB; production
4 vCPU / 8 GB / 80 GB) là đúng và **không cần nâng** vì lý do tải. Chỉ nâng disk nếu retention
được kéo dài hơn mặc định (`waybill_scan_events` 60 ngày, `dashboard_changes` 14 ngày,
`audit_logs` 90 ngày).

Ngân sách độ trễ (§7.2): commit + notify 1–10 ms → hub nhận 1–5 ms → SignalR đẩy VPS(VN)
10–40 ms → client debounce 200–500 ms → delta-pull + áp SQLite 20–100 ms ⇒ tổng **250–650 ms**
so với mục tiêu < 2 s. Còn dư biên rất lớn.

**Một trần cứng phải nhớ khi sizing dữ liệu**: snapshot **không có cursor pagination**.
`ChangeRepository.MaximumSnapshotRows = 10_000`. Site vượt 10.000 projection sẽ **luôn** trả
`truncated=true` và phần đuôi chỉ lấy lại được qua changes feed. Đây là ràng buộc thiết kế cần
biết trước khi hứa với khách một site lớn, không phải bug cần vá gấp.

---

## Phần 2 — Bảng trạng thái: đã có gì, thiếu gì

Kiểm kê ngày 2026-08-25 trên 6 vùng bề mặt backend (license server, cấu hình DataHub, HTTP
surface, migration/DB, deploy assets, kỳ vọng phía client): **165 hạng mục đã có**, **57 khoảng
trống**. Bảng dưới gom theo khả năng chặn go-live.

### 2.1 Đã hoàn chỉnh — không cần làm gì thêm

| Vùng | Hạng mục |
|---|---|
| Container | `Dockerfile` multi-stage .NET 10, chạy non-root `$APP_UID`, `VOLUME ["/manifests"]`, healthcheck bằng `curl` |
| Compose | 3 service, image ghim `@sha256`, `data` network `internal: true` (Postgres **không** thò ra host), `depends_on` gated theo health, log rotate json-file |
| TLS | Caddy 2.10-alpine, ACME HTTP-01 tự xin + tự gia hạn, `admin off`, cert nằm ở volume `caddy_data` |
| SignalR | Caddyfile đã proxy `/hubs/site` với `flush_interval -1` và timeout đọc/ghi 1 h |
| Hardening | `bootstrap-vps.sh` idempotent: unattended-upgrades, check `libseccomp2 ≥ 2.5.1` (bắt buộc cho clone3 của .NET 10), UFW default-deny + mở 22/80/443, fail2ban (`bantime 1h`, `maxretry 5`, backend systemd), Docker từ repo chính thức, PowerShell 7 |
| Migration | 5 file forward-only 001–005 + `apply-migrations.{ps1,sh}` idempotent, mỗi file `--single-transaction` + `ON_ERROR_STOP=1`, đọc lại version marker và ném lỗi nếu không ghi được |
| Ops script | `start-stack.ps1` (ép `@sha256`, `--no-build`, so digest sau pull), `provision-site.ps1`, `backup-postgres.ps1`, `restore-postgres.ps1`, `publish-manifests.{ps1,sh}`, `smoke-test.sh` (10 bước + 5 case âm), `deployment-static-smoke.ps1` (20+ check tĩnh) |
| Mặt phẳng điều khiển | `PUT /api/v1/admin/manifests/{**objectPath}` **đã có** (xem 0.2), `ManifestStore` giới hạn 1 MiB, ETag = SHA-256 hex |
| Xác thực assertion | `RsaLicenseAssertionValidator` hoàn chỉnh (`v1rs256`, RSA ≥ 2048, PKCS#1 v1.5 / SHA-256, từ chối khoá private) |
| License server | 115 test / 9 file, `license-expiry.js` theo mốc ngày 16, `render.yaml` blueprint đầy đủ, `firebase-credentials.js` nhận 5 nguồn credential |

### 2.2 Chặn go-live — phải xong trước khi mở cho khách

| # | Khoảng trống | Vì sao chặn | Ai làm |
|---|---|---|---|
| **G1** | Chưa chốt **nguồn deploy** của license server (mục L-1) | Render production đang chạy repo `AutoJMS-API` (HEAD `c6f05433`, `server.js` 895 dòng), **không** phải `backend/render-license-server/` (1.250 dòng). Bản đang chạy **thiếu** `issueDataHubAssertion`, khối `datahub` trong response, `seats`, `tokenVersion` — nghĩa là **không phát được assertion**, nghĩa là **không enroll được**, nghĩa là toàn bộ VPS vô dụng | ⛔ chủ sở hữu |
| **G2** | `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` (hoặc `_PATH`) chưa có trên VPS | API nạp `UnavailableLicenseAssertionValidator`; `POST /devices/enroll` trả **503** fail-closed | ⛔ chủ sở hữu sinh cặp khoá |
| **G3** | `_ISSUER`/`_AUDIENCE` phải **khớp từng ký tự** giữa hai host | Default trong code license server là `autojms-license` / `autojms-datahub-enroll`; template production của VPS đòi `autojms-license-production` / `autojms-datahub-enroll-production`. Lệch ⇒ assertion ký đúng vẫn bị từ chối `LICENSE_ASSERTION_INVALID`, trạm chỉ báo "enroll thất bại" | agent (tài liệu) + ⛔ chủ sở hữu (đặt biến) |
| **G4** | Seed mặt phẳng điều khiển chưa publish | Fetch policy đi 6 đường, thất bại hết ⇒ `RuntimePolicyDocument.SafeDefault("BASE")`. Máy ULTRA **chạy như BASE**, log chỉ ghi `[Policy] source=safe-default tier=BASE`, **không có error nào** | agent chạy script sau khi có `DATAHUB_ADMIN_TOKEN` |
| **G5** | `DATAHUB_ADMIN_TOKEN` không có ⇒ health check vẫn báo **Healthy**, chỉ kèm một dòng ghi chú text | Mọi `PUT` manifest trả 503 ⇒ cả fleet **âm thầm** không bao giờ nhận policy hay bản cập nhật | agent (sửa health check) |
| **G6** | Không có backup theo lịch | Guide §12.2 chỉ *ghi* dòng crontab; repo **không** có `cron.d`, không systemd timer, không crontab file. VPS mới ⇒ **không có backup nào** | agent (thêm file) |
| **G7** | Chưa diễn tập restore | `restore-postgres.ps1` có, nhưng chưa ai chạy thật. Backup chưa restore được là backup chưa tồn tại | agent + ⛔ xác nhận |
| **G8** | `ApiProblemDetails.cs:33` hardcode `https://datahub.example.com/problems/...` | Domain giả rò ra mọi response lỗi; và vì hai VPS có hai domain nên giá trị này **phải là cấu hình**, không phải hằng | agent |
| **G9** | Guide §10.2 dùng câu verify `s.seats` — **cột này không tồn tại** trong bất kỳ migration nào | Operator làm đúng vẫn thấy `column s.seats does not exist` và tưởng deploy sai. `seats` chỉ sống trong JWT assertion (`LicenseAssertionIdentity.Seats`), không lưu DB | agent |
| **G10** | `DATAHUB_API_BASE_URL` trên license server vẫn là placeholder `https://datahub.example.com` | Kể cả sau khi A và G2 xong: license server trả URL này cho client trong khối `datahub`, **không báo lỗi**. `DataHubClient` được `Configure` với token thật rồi gọi một domain không resolve ⇒ mọi request timeout. Trạm không biết mình đang gọi sai chỗ | ⛔ chủ sở hữu |
| **G11** | `siteId` khi provision trên VPS **phải trùng đúng GUID** mà license server gửi trong khối `datahub` | Lệch GUID ⇒ `POST /sites/{siteId}/lease/acquire` trả **404**. `DataHubClient` coi 404 là **Unreachable**, *không* phải Denied — nên trạm **vẫn tự coi mình có thể là leader** và tiếp tục gọi `/jms/ingest` (cũng 404). Mọi write thất bại, không có thông báo | ⛔ (chốt danh sách GUID) + agent |
| **G12** | Health check **không** kiểm schema — stack báo Healthy với DB **rỗng** | `PostgresHealthCheck` chỉ gọi `CanConnectAsync`; `RuntimeConfigurationHealthCheck` **không** đọc `schema_migrations`, `jms_event_policies` hay `retention_policies`. Bỏ bước migration ⇒ `/health/ready` **200**, Caddy route traffic, rồi từng endpoint thật gãy ở tầng SQL. **Không có lưới an toàn nào** cho bước tay quan trọng nhất | agent (thêm sub-check) |

### 2.3 Rủi ro thầm lặng — không chặn boot, nhưng gây "chạy mà sai"

Điểm chung của cả nhóm: **hệ thống báo khoẻ, người dùng không thấy lỗi, dữ liệu vẫn sai hoặc
ngừng chảy.** Đây là nhóm đắt nhất khi bỏ qua.

| # | Hiện tượng người dùng thấy | Nguyên nhân thật |
|---|---|---|
| S1 | Tab FullStack trống | enroll thất bại ⇒ `HasCredentials=false` ⇒ mọi đường DataHub thành no-op. **Không phân biệt được** với `siteId` sai hay VPS chết. Và **không có retry/backoff** — DataHub tắt cả session |
| S2 | Máy ULTRA thiếu FullStack, thiếu background sync | Policy 404 cả 6 đường ⇒ `SafeDefault('BASE')`. Triệu chứng **y hệt** license BASE |
| S3 | Sync ngừng ~24 h sau khi bật máy | Gia hạn device token thất bại ⇒ giữ token cũ đã hết hạn ⇒ 401 mọi call. Không có thông báo |
| S4 | App treo tới 90 giây lúc khởi động | Fetch runtime policy **đồng bộ** (`.GetAwaiter().GetResult()`) qua 6 URL **tuần tự**, mỗi URL timeout 15 s. VPS "còn sống nhưng chậm" là trường hợp xấu nhất |
| S5 | Thiếu vận đơn cũ, không có cảnh báo | `snapshot truncated=true` chỉ log warning, UI không báo |
| S6 | Site code `214A02` không sync | `TryGetSiteId()` cần GUID; site code không phải GUID ⇒ thất bại ở mức **Debug** |
| S7 | Trạm ngoại tuyến vẫn thấy vận đơn đã bị xoá, mãi mãi | `dashboard_changes` đã có `CHECK` cho `operation='delete'` nhưng **không job nào phát tombstone**. Hard-delete ⇒ client offline không bao giờ biết |
| S8 | Enroll bị chặn vì hết seat, không ai biết vì sao | Mã lỗi seat-limit **được log nhưng không hiện cho người dùng** |
| S9 | Publish manifest "thành công" rồi mất sau restart | `DATAHUB_MANIFEST_ROOT` mặc định `/manifests`; nếu volume `manifests_data` không mount đúng đó, ghi vào layer container ephemeral, **không ai phát hiện** |
| S10 | Rate limit bị bypass | `DATAHUB_TRUSTED_PROXY_NETWORKS` mặc định gồm `10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16` ⇒ trên host Docker dùng chung, tenant khác trong các subnet đó **giả được `X-Forwarded-For`**. Health check **không bao giờ** kiểm giá trị này |
| S11 | Mọi device token đều `TOKEN_INVALID`, host vẫn boot Healthy | `DATAHUB_DEVICE_TOKEN_ISSUER`/`_AUDIENCE` **không** nằm trong health check |
| S12 | Retention ngừng chạy, bảng changes phình dần | Không có hook quan trắc nào cho retention job |

### 2.4 Nợ kỹ thuật đã biết — xử lý trong hoặc sau rollout

- **Migration còn thiếu index** (⚠️ *migration là Protected File*): không có index cho
  `dashboard_changes(change_at)` / `(site_id, change_at)` — PK là `(site_id, change_seq)` nên
  prune 14 ngày **không có đường index cho cột thời gian** ⇒ tuần tự scan. Tương tự,
  retention của `waybill_scan_events` lọc `event_occurred_at` mà **không** bind `waybill_no`,
  nên `ix_waybill_scan_events_site_waybill_time` không dùng được; thiếu index
  `(site_id, event_occurred_at)`. `audit_logs` retention `ORDER BY a.at, a.id` xuyên site mà
  `ix_audit_logs_site_at(site_id, at)` không phục vụ hiệu quả.
- **Không có bảng `jti_cache` / blacklist**: device token bị trộm sống tới hết hạn (mặc định
  86.400 s). Chỉ có so `credential_hash` và bump `token_version` khi re-enroll là vô hiệu hoá
  được.
- **Migration không tự áp lúc boot**: không init container, không ENTRYPOINT chain. `depends_on`
  health-gate của Caddy do đó phụ thuộc **một bước tay người**.
- **Không có checksum migration** (`schema_migrations` chỉ lưu version + timestamp) ⇒ sửa file
  migration sau khi đã áp là **không phát hiện được**. Không có down migration; rollback schema
  = restore `pg_dump` trước migration (§13.3). Không có `pg_advisory_lock` cho runner (hiện an
  toàn chỉ nhờ `IF NOT EXISTS` + `ON CONFLICT DO NOTHING`).
- **`backend/backend-schema-dump.sql` là file rỗng** — ảnh chụp schema chuẩn chưa từng được sinh.
- **Không có CI**: repo **không có** `.github/`. `eng/harness/verify.ps1` chỉ chạy local. Build
  và push image hoàn toàn thủ công.
- **Không có script deploy tổng, không có script rollback**: một lần deploy sạch cần operator
  chạy 4–6 lệnh riêng theo đúng thứ tự. Rollback là chuỗi lệnh tay trong §13, và **digest image
  cũ phải do operator tự lưu** — không lưu là không rollback nhanh được.
- **Không có nơi lưu secret ngoài file `.env`**: không Vault, không Docker secret, không rotation
  tự động, không audit truy cập.
- **Không có quan trắc uptime**: không probe ngoài, không Prometheus (`/metrics` không tồn tại),
  không alert. Guide §14.5 chỉ đề nghị "xem lịch mỗi tuần".
- **License server**: không graceful shutdown (`server.close()` chưa bao giờ được gọi), không
  structured logging, JTI replay cache là `NodeCache` **in-memory** (restart là xoá ⇒ access
  token phát trước đó **replay được** ở `/api/heartbeat` trong phần còn lại của 60 phút; hai
  instance cũng vậy), rate limiter dùng store in-memory (hai instance hoặc rolling restart là
  vượt được budget). `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` là **env-var-only**, thấy được qua
  `docker inspect` và `/proc` — **không có** biến thể `_PRIVATE_KEY_FILE`.
- **`DATAHUB_API_BASE_URL` mặc định là placeholder `https://datahub.example.com`** và được trả
  về cho client **không báo lỗi** nếu sai.
- **Lệch phiên bản dependency**: repo ghim `express ^4.19.2` / `firebase-admin ^12.7.0`; bản đang
  chạy dùng `^5.2.1` / `^13.8.0`.
- **HTTP surface**: không có `/api/version` hay build-ID; không `X-Request-Id` trên response
  thành công (traceId chỉ có trong body lỗi); không CORS policy; `AdminAuthenticationMiddleware`
  không phân biệt "không có header" với "token sai"; không audit entry cho enroll **thất bại**;
  không cache dùng chung trước manifest GET (Caddy không cache mặc định) nên **mọi desktop poll
  VPS mỗi 60 s**; HEAD tốn budget như GET; rate limit per-device bị áp **hai lần** (`IngressIp
  Limiter._deviceLimiter` 240/phút + policy `device` ở endpoint 240/phút, hai cửa sổ độc lập).
- **`statement_timeout` (30 s) chỉ đặt trong `ChangeRepository`** (`ReadChangesAsync`,
  `ReadSnapshotAsync`). `IngestPipeline`, `LeaseRepository`, `EnrollmentRepository`,
  `DeviceRepository` **không có** chốt nào.
- **`/health/ready` tiêu budget ingress 600/phút/IP** — load balancer poll dày từ một IP có thể
  tự rate-limit chính mình. `/health/live` thì dependency-free (đúng).

---

## Phần 3 — Chặng A: chốt nguồn deploy license server ⛔

**Đây là chặng đầu tiên và không có đường vòng.** Ba quyết định hạ tầng ngày 2026-08-25
**không** giải quyết mục này.

**Điều kiện vào**: không có.

**Việc làm** — chủ sở hữu chọn **một** trong ba, rồi ghi lựa chọn vào
`backend/BACKEND_DEPLOY_STATUS.md`:

| Lựa chọn | Nội dung | Đánh giá |
|---|---|---|
| **A1** (khuyến nghị) | Trỏ Render service về monorepo `AutoJMS`, dùng `backend/render.yaml` làm blueprint | Một nguồn sự thật duy nhất. Repo đã có 115 test, đã có `issueDataHubAssertion`, đã bỏ Supabase. Rủi ro: cần đặt lại toàn bộ env trên Render và **kiểm phá vỡ tương thích với client cũ** trước khi bật |
| **A2** | Port các thay đổi từ `backend/render-license-server/` sang repo `AutoJMS-API`, tiếp tục deploy repo đó | Ít động vào Render, nhưng **duy trì vĩnh viễn hai bản** 895 vs 1.250 dòng. Mọi báo cáo "đã vá" tiếp tục nhập nhằng repo/production |
| **A3** | Dựng service Render **mới** từ monorepo, chạy song song, cutover bằng đổi `LICENSE_API_BASE_URL` phía client | An toàn nhất, đắt nhất. Chỉ nên chọn nếu có nhiều máy khách đang chạy và không thể chịu downtime |

**Việc kèm theo, bắt buộc với cả ba lựa chọn:**

1. ⛔ **Thu hồi Supabase anon key** đang bị bản production phát cho mọi client (mục L-3).
   Bản đang chạy vẫn có `DEFAULT_SUPABASE_PROJECT_REF` và trao anon key cho từng client.
2. ⛔ **Ghim Render về 1 instance.** JTI replay cache và rate limiter đều in-memory; hai
   instance là hai lỗ hổng, không phải khả năng chịu tải.
3. Đối chiếu `express` / `firebase-admin` giữa repo và bản đang chạy, chốt một phiên bản.
4. `render.yaml` đã đặt sẵn `DATAHUB_LICENSE_ASSERTION_ISSUER=autojms-license-production` và
   `_AUDIENCE=autojms-datahub-enroll-production` **inline** — đúng. Nếu chọn A2/A3 mà không
   dùng blueprint này thì **phải đặt tay hai biến đó**, vì default trong code là giá trị
   **không** khớp VPS (xem G3).
5. ⛔ **Đặt `DATAHUB_API_BASE_URL` thành hostname VPS thật** (G10). Default là placeholder
   `https://datahub.example.com` và license server **trả nó cho client không kèm lỗi nào**. Kết
   quả: `DataHubClient` được cấu hình với token thật rồi gọi một domain không resolve — trạm
   không có cách nào biết mình đang gọi sai chỗ. Giá trị phải **khớp domain đã cấp TLS trong
   Caddy**, và **khác nhau giữa staging và production** (đó là lý do license server staging và
   production không thể là cùng một service nếu bạn muốn cả hai chạy song song).

**Điều kiện ra**: `GET <license-url>/health` trả 200; `POST /api/verify-license` với một
license ULTRA thật trả response **có khối `datahub`** chứa `assertion` và `apiBaseUrl`. Chưa đạt
điều này thì **không sang Chặng B** — mọi thứ dưới đây phụ thuộc vào assertion.

> **Một mâu thuẫn thứ tự cần xử lý tường minh.** Chặng A xong trước khi VPS tồn tại, nên
> `apiBaseUrl` lúc đó trỏ tới một host chưa dựng. Điều đó **không sao** với điều kiện ra ở trên
> (ta chỉ kiểm *có* assertion, không kiểm enroll được). Nhưng nó tạo một ràng buộc thật:
> **đừng trỏ máy khách đang chạy sang license server mới trước khi VPS đã healthy và đã qua
> Chặng F.** Nếu trỏ sớm, trạm nhận `apiBaseUrl` hợp lệ, thử enroll, thất bại, và vì **không có
> retry/backoff** (S1) nó **tắt DataHub cho cả session** — phải khởi động lại app mới thử lại.
> Với A3 (service Render mới, chạy song song) ràng buộc này tự nhiên được thoả; với A1/A2 thì
> phải chọn thời điểm deploy, hoặc dùng một `apiBaseUrl` staging trong giai đoạn chuyển tiếp.

---

## Phần 4 — Chặng B: vá code trước khi mua VPS

Làm trên máy dev, không cần VPS. Mỗi việc kết thúc bằng `verify.ps1` xanh.

| # | Việc | File | Ghi chú |
|---|---|---|---|
| **B0** | Cập nhật `BACKEND_DEPLOY_STATUS.md`: đánh dấu manifest endpoint **đã có**, ghi rõ box `dev.jmsauto.online` sẽ nghỉ hay thành staging | `backend/BACKEND_DEPLOY_STATUS.md` | Xem 0.2 |
| **B1** | Đưa base URI của problem type thành **cấu hình**, mặc định suy ra từ `DATAHUB_PUBLIC_HOST` | `src/AutoJMS.DataHub.Api/.../ApiProblemDetails.cs:33` | Hai VPS = hai domain, nên không thể là hằng số (G8) |
| **B2** | Sửa câu verify ở guide §10.2: bỏ `s.seats` | `backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md` | `seats` chỉ có trong JWT, không có trong DB (G9) |
| **B3** | Health check phải **Degraded/Unhealthy** khi thiếu `DATAHUB_ADMIN_TOKEN`, và phải kiểm cả 4 biến issuer/audience + `DATAHUB_TRUSTED_PROXY_NETWORKS` + `DATAHUB_MANIFEST_ROOT` có ghi được thật | `src/AutoJMS.DataHub.Api/Health/RuntimeConfigurationHealthCheck.cs` | Vá G5, S9, S10, S11 cùng lúc — đây là việc có tỉ lệ lợi/chi phí cao nhất trong cả kế hoạch |
| **B4** | Đặt **ngân sách tổng** cho fetch runtime policy (ví dụ 8 s cho cả 6 đường) và chuyển sang bất đồng bộ nếu được | phía client, vùng `VpsRuntimePolicyService` | Vá S4 (treo tới 90 s). Nếu chạm `Program.cs` ⇒ **cần xin phép** (Protected) |
| **B5** | Enroll thất bại: retry có backoff + **hiện lỗi cho người dùng**, tách rõ "sai siteId" / "hết seat" / "VPS không phản hồi" | phía client, vùng `DataHubClient` + enroll | Vá S1, S8 |
| **B6** | Gia hạn device token thất bại: nâng mức log lên Error và báo UI | phía client | Vá S3 |
| **B7** | `snapshot truncated=true` ⇒ báo UI, không chỉ log | phía client | Vá S5 |
| **B8** | `TryGetSiteId()` thất bại ⇒ log **Error** kèm giá trị nhận được (che bớt), không phải Debug | phía client | Vá S6 |
| **B9** | Sinh `backend/backend-schema-dump.sql` thật từ một DB đã áp 001–005 | | Hiện là file rỗng |
| **B10** | Thêm `.github/workflows/verify.yml` gọi đúng các bước của `verify.ps1` | mới | Không có CI là mọi gate đều là gate danh dự |
| **B11** | Tách điều kiện READ khỏi điều kiện FETCH: "License DataHub hợp lệ ⇒ được ĐỌC", không phụ thuộc token JMS | `FullStackOperation.cs` ~dòng 122 | Mục §8 của `datahub-deployment-options.md` |
| **B12** | Job night-purge phát **tombstone** (`operation='delete'`) trước khi hard-delete; retention tombstone ≥ cửa sổ offline dài nhất (đề xuất 30–90 ngày) | API | Vá S7 |
| **B13** ⚠️ | Migration `006`: bảng `jti_cache`/revocation + index `dashboard_changes(site_id, change_at)` + index `waybill_scan_events(site_id, event_occurred_at)` + index phục vụ retention `audit_logs` | `backend/datahub/migrations/006_*.sql` | **Migration là Protected File — cần chủ sở hữu cho phép riêng cho việc này.** Xem cảnh báo `CONCURRENTLY` ngay dưới bảng |
| **B16** | Thêm sub-check schema cho `/health/ready`: `schema_migrations` có đủ marker, `jms_event_policies` và `retention_policies` có ≥ 1 dòng | `Health/` | Vá G12 — hiện **không có lưới an toàn nào** cho bước migration thủ công |
| **B17** | Ép server-side `DeviceIdentity.Role` (hiện enroll ghi `'operator'` nhưng **không endpoint nào kiểm**) | API | RBAC hiện là vỏ không có ruột. Không chặn go-live vì chỉ có một role, nhưng phải vá trước khi thêm role thứ hai |
| **B14** | `statement_timeout` cho `IngestPipeline`, `LeaseRepository`, `EnrollmentRepository`, `DeviceRepository` | API | Một query treo ở lease là fencing sai |
| **B15** | License server: graceful shutdown (`server.close()` khi SIGTERM/SIGINT) + structured logging + `/api/version` | `backend/render-license-server/server.js` | Không có graceful shutdown ⇒ mỗi lần deploy là cắt request đang bay |

> ⚠️ **Cảnh báo về `CREATE INDEX CONCURRENTLY` trong B13.** Cách đúng để thêm index vào bảng
> đang chạy production là `CONCURRENTLY` (không lock bảng). Nhưng `CONCURRENTLY` **không chạy
> được trong transaction block**, còn `apply-migrations.{ps1:111,sh:67}` áp **mọi** file bằng
> `--single-transaction`. Nghĩa là một migration `006` viết bằng `CONCURRENTLY` sẽ **thất bại**
> dưới runner hiện tại với lỗi *"CREATE INDEX CONCURRENTLY cannot run inside a transaction
> block"*. Ba đường đi, chọn khi xin phép:
> 1. Tách `006` thành hai file: DDL trong transaction, phần `CONCURRENTLY` chạy tay ngoài runner
>    (ghi rõ trong guide).
> 2. Cho runner nhận một quy ước tên file (ví dụ `006_..._notx.sql`) để bỏ `--single-transaction`
>    cho riêng file đó — **nhưng** khi đó mất tính nguyên tử, file phải tự idempotent hoàn toàn.
> 3. Chấp nhận index thường (có lock) vì hai VPS đều mới, bảng còn nhỏ — **đây là lựa chọn hợp
>    lý nhất nếu B13 làm trước khi có dữ liệu thật**, và cũng là lý do nên làm B13 **sớm**.

**Thứ tự khuyến nghị**: B0 → B3 → **B16** → B1 → B2 → B9 → B10 → (B4…B8 nhóm client) → B11 →
B12 → B14 → B15 → B17 → B13 (chờ phép).

**Điều kiện ra**: `dotnet build AutoJMS.slnx -c Release` 0 error, `verify.ps1` **ALL GATES
PASSED**, và B13 đã được cho phép hoặc đã được ghi nhận là hoãn có ý thức.

---

## Phần 5 — Chặng C: VPS staging — provision và hardening

**Điều kiện vào**: Chặng A đã ra (có assertion thật để test enroll).

### C0 — Xử lý box `dev.jmsauto.online` ⛔

Chọn **một**:
- **Dùng lại làm staging**: chạy `bootstrap-vps.sh --harden-ssh` trên nó (hardening chưa từng
  áp), rồi coi như đã ở C3.
- **Cho nghỉ**: `docker compose down`, huỷ A record, tắt máy. Ghi vào
  `BACKEND_DEPLOY_STATUS.md`.

Không được để nó chạy tiếp mà không ai theo dõi: nó là một endpoint DataHub **đang nhận enroll**.

### C1 — Mua VPS ⛔

Staging: **2 vCPU / 4 GB / 40 GB SSD / Ubuntu Server 24.04 LTS**. Đặt tại VN hoặc Singapore
(ngân sách độ trễ ở 1.1 giả định VPS ở VN: 10–40 ms cho SignalR push).

Cơ sở tính RAM: `postgres` 2 GB + `api` 768 MB + `caddy` 256 MB + OS. 4 GB là mức **chạy được**;
production cần 8 GB để có biên cho `pg_dump` chạy đồng thời.

### C2 — DNS ⛔

A record `datahub-stg` → IP VPS staging, TTL 300. **Kiểm `dig +short` trả đúng IP trước khi
khởi động Caddy** — ACME thất bại có thể chạm rate limit của Let's Encrypt và khoá bạn nhiều giờ.
`DATAHUB_PUBLIC_HOST` phải là tên DNS thật: **không** IP trần, **không** `.invalid`/`.example`.

### C3 — Hardening

Chạy [`bootstrap-vps.sh`](../../backend/datahub/deploy/bootstrap-vps.sh) — script đã idempotent
và đã phủ: apt upgrade + unattended-upgrades, check `libseccomp2 ≥ 2.5.1`, timezone
`Asia/Ho_Chi_Minh` + NTP, tạo user không phải root có sudo, copy SSH key, UFW default-deny mở
22/80/443, fail2ban, Docker Engine, PowerShell 7, và **kiểm 5432 không thò ra ngoài**.

Ba việc script **không** làm, phải làm tay:

1. `--harden-ssh` chỉ tắt password auth khi user đã có `authorized_keys`. **Mở một session SSH
   mới và xác nhận `sudo -v` chạy được TRƯỚC KHI đóng session root.** Đây là bước duy nhất
   trong cả kế hoạch có thể làm mất quyền vào máy.
2. `PermitRootLogin prohibit-password` (nợ kỹ thuật đã ghi nhận từ trước).
3. `timedatectl` phải báo `System clock synchronized: yes` — **lease fencing dùng
   `clock_timestamp()` của PostgreSQL**, đồng hồ lệch nhiều là fence sai.

> ⚠️ **UFW không bảo vệ các port do Docker publish.** Docker sửa `iptables` trực tiếp và đi
> vòng qua chain `FORWARD` của UFW, nên `80/443` **mở bất kể** rule UFW nói gì. Giá trị thật của
> UFW ở đây là bảo vệ SSH và bịt mọi port **khác**; đừng dựa vào nó để nghĩ rằng bạn kiểm soát
> được 80/443. Việc `data` network của Compose có `internal: true` (Postgres không publish) mới
> là thứ giữ 5432 kín — và đó là lý do §2.5 phải chạy **lại** sau khi stack lên, không chỉ trước.

**Điều kiện ra**: chạy lại §2.5 của guide (kiểm bề mặt tấn công) — chỉ 22/80/443 mở, 5432 không
thấy từ ngoài, fail2ban `[sshd]` active.

---

## Phần 6 — Chặng D: khoá và biến môi trường

Chặng này là nơi hầu hết các sự cố "chạy mà sai" được sinh ra hoặc bị chặn.

### D1 — Sinh cặp khoá RSA assertion ⛔

Chủ sở hữu sinh **một** cặp RSA ≥ 2048 bit cho **mỗi channel** (staging và production nên dùng
hai cặp khác nhau — nếu dùng chung, một khoá rò là rò cả hai môi trường).

| Nửa khoá | Đặt ở đâu | Biến |
|---|---|---|
| **Private** | **Chỉ** license server (Render) | `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` |
| **Public** | **Chỉ** VPS | `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` hoặc `_PUBLIC_KEY_PATH` |

**Không bao giờ** đặt nửa private lên VPS. Nếu vô tình đặt, `RsaLicenseAssertionValidator` sẽ
từ chối và enroll đóng — hành vi đúng, nhưng lúc đó bạn đã có một khoá private trong file `.env`
trên một máy công khai và **phải xoay khoá**, không phải chỉ sửa biến.

`_PUBLIC_KEY_PATH` **có ưu tiên cao hơn** PEM inline. Chấp nhận escape `\n`. Khuyến nghị dùng
`_PATH` + mount read-only để khoá không nằm trong `docker inspect`.

### D2 — Cặp issuer/audience phải khớp từng ký tự

Đây là G3, và là lỗi tốn thời gian nhất vì triệu chứng phía trạm chỉ là "enroll thất bại".

| Biến | License server (Render) | VPS staging | VPS production |
|---|---|---|---|
| `DATAHUB_LICENSE_ASSERTION_ISSUER` | phải **đặt tay** | `autojms-license-staging` | `autojms-license-production` |
| `DATAHUB_LICENSE_ASSERTION_AUDIENCE` | phải **đặt tay** | `autojms-datahub-enroll-staging` | `autojms-datahub-enroll-production` |

**Default trong code license server là `autojms-license` / `autojms-datahub-enroll` — KHÔNG
khớp với giá trị nào ở trên.** `render.yaml` đặt sẵn cặp `-production` inline; nếu deploy không
qua blueprint đó thì phải đặt tay. Với staging, license server phải phát assertion mang cặp
`-staging` (hoặc dùng `issue-staging-assertion.ps1` với HMAC — xem D5).

`LicenseAssertionClaims.Validate` kiểm issuer/audience **sau** khi xác minh chữ ký, nên chữ ký
đúng vẫn trả `LICENSE_ASSERTION_INVALID`. Health check **không** kiểm hai biến này.

**Thứ tự đặt biến để không có cửa sổ lệch** — quan trọng nếu đã có máy khách đang chạy:

1. Đặt `_ISSUER`/`_AUDIENCE` trên **VPS trước**. Lúc này enroll vẫn trả **503** vì chưa có public
   key ⇒ không có trạm nào enroll sai được.
2. Deploy license server với **cùng** cặp giá trị.
3. **Cuối cùng** mới thêm `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` vào VPS ⇒ enroll mở ra khi hai
   phía đã khớp.

Làm ngược lại (Render trước, VPS sau) tạo một cửa sổ mà mọi enroll đều thất bại
`LICENSE_ASSERTION_INVALID` — và vì **không có retry/backoff** phía trạm (S1), mỗi trạm rơi vào
cửa sổ đó sẽ **tắt DataHub cho cả session**, phải khởi động lại app mới thử lại.

### D2.1 — Hai biến không bao giờ được đổi sau khi đã có thiết bị enroll

`DATAHUB_DEVICE_TOKEN_ISSUER` và `DATAHUB_DEVICE_TOKEN_AUDIENCE` được
`HmacDeviceTokenService.ValidateAsync` so ở dòng 72-74. Đổi chúng ở một lần deploy sau ⇒ **toàn
bộ** device token đã phát (mặc định còn hiệu lực tới 24 giờ) trả `TOKEN_INVALID`. Cả fleet mất
sync **đồng loạt và im lặng** — chỉ có 401 trong log, host vẫn Healthy (S11).

⇒ Sau lần deploy đầu tiên: **ghi hai giá trị này vào password manager kèm ghi chú "KHÔNG ĐỔI khi
đã có thiết bị enroll"**. Nếu buộc phải đổi, đó là một cuộc re-enroll toàn fleet có kế hoạch,
không phải một lần sửa `.env`.

### D3 — Ba mặc định nguy hiểm, phải đặt tường minh

| Biến | Mặc định | Phải đặt thành | Vì sao |
|---|---|---|---|
| `DATAHUB_TRUSTED_PROXY_NETWORKS` | `127.0.0.1/32,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16` | **chỉ** subnet thật của container Caddy | Trên host Docker dùng chung, tenant khác trong `10/8`, `172.16/12`, `192.168/16` **giả được `X-Forwarded-For`** và bypass rate limit enroll + ingress. Health check không bao giờ kiểm (S10) |
| `DATAHUB_MANIFEST_ROOT` | `/manifests` | đúng điểm mount của volume `manifests_data` | Sai ⇒ publish ghi vào layer ephemeral, **mất sau mỗi restart, không báo lỗi** (S9) |
| `DATAHUB_ALLOW_STAGING_TEST_ISSUER` | `false` | `true` **chỉ** ở staging, `false` ở production | Staging dựng tay mà để `false` sẽ boot **Unhealthy** ("staging license verifier not configured") — đúng nhưng gây mất thời gian nếu không biết trước |

### D4 — Biến chỉ có trong template, và biến chỉ có trong code

Hai nhóm này là nguồn của kiểu lỗi "đặt biến rồi mà không tác dụng".

**Chỉ template đọc, .NET không đọc**: `DATAHUB_PUBLIC_HOST` (Caddy), `TLS_CONTACT_EMAIL`
(ACME), `DATAHUB_API_IMAGE` (Compose), `POSTGRES_DB`/`_USER`/`_PASSWORD` (API **chỉ** thấy
`ConnectionStrings__DataHub`).

**Chỉ code đọc, không có trong template nào**: `DataHub:Channel` (fallback ở
`DataHubRuntimeOptions.cs:67`) và `ConnectionStrings:DataHub` — **dạng dấu hai chấm; trong env
var chỉ dạng gạch dưới đôi mới có tác dụng**.

### D5 — Sinh secret

Theo §7.1 của guide: `openssl rand` **ghi thẳng vào file**, không qua stdout, để agent không
bao giờ thấy giá trị. Cần: `POSTGRES_PASSWORD`, `DATAHUB_DEVICE_TOKEN_SIGNING_KEY`,
`DATAHUB_ENROLLMENT_PEPPER`, `DATAHUB_ADMIN_TOKEN`, và ở staging thêm
`DATAHUB_STAGING_TEST_SIGNING_KEY` (≥ 32 ký tự).

`DATAHUB_ENROLLMENT_PEPPER` **phải là secret khác** với
`DATAHUB_DEVICE_TOKEN_SIGNING_KEY` — `devices.credential_hash` =
`HMAC-SHA256(pepper, token)`, và nó chỉ là **yếu tố thứ hai thật sự** khi hai secret độc lập.

`DATAHUB_DEVICE_TOKEN_LIFETIME_SECONDS` mặc định 86.400, kẹp trong `300..2592000`.

**Điều kiện ra**: `.env.staging` đầy đủ; §7.3 của guide (xác nhận không rò secret) pass;
`git status` **không** thấy file env nào.

---

## Phần 7 — Chặng E: khởi động stack, migration, provision site

**Điều kiện vào**: C ra + D ra.

1. **Build và push image** (§5). `start-stack.ps1` **ép** `DATAHUB_API_IMAGE` phải là
   `...@sha256:<64 hex>` và chạy `--no-build`, rồi so `RepoDigests` sau khi pull. Tag động bị
   từ chối — đây là cố ý. **Lưu digest vào password manager ngay**: không có nó thì Chặng
   Rollback mất đường nhanh nhất.
2. **Khởi động** bằng `start-stack.ps1`. Chạy lại §2.5 để chắc container không mở thêm port.
3. **Áp migration** bằng `apply-migrations.ps1` (hoặc `.sh`). **Migration KHÔNG tự áp lúc boot**
   — không init container, không ENTRYPOINT chain.

   > 🔴 **Đây là bước dễ quên nhất và không có lưới an toàn nào (G12).** Đã kiểm chứng ngày
   > 2026-08-25: `PostgresHealthCheck` **chỉ** gọi `CanConnectAsync`, và
   > `RuntimeConfigurationHealthCheck` **không** đọc `schema_migrations`, `jms_event_policies`
   > hay `retention_policies`. Nghĩa là với một DB **hoàn toàn rỗng**, `/health/ready` vẫn trả
   > **200**, Caddy vẫn route traffic, `docker compose ps` vẫn xanh — và từng endpoint thật gãy
   > ở tầng SQL khi có request đầu tiên. Đừng tin `healthy` để suy ra "đã áp migration"; phải
   > kiểm bằng bước 4. Đây là lý do B16 tồn tại.

4. **Kiểm schema** bằng `run-sql.sh tests/001_core_catalog_assertions.sql` và đếm marker trong
   `schema_migrations` (phải đủ 5, hoặc 6 nếu B13 đã áp). Bước này **thay thế** việc tin vào
   health check.
5. **Provision site** bằng `provision-site.ps1` — gọi `create_datahub_site(site_id::uuid,
   site_code)`, ghi `sites` + `site_fetch_leases` + `site_change_counters` trong **một**
   transaction. **Không idempotent** (`site_code` có UNIQUE) — chạy hai lần là lỗi, không phải
   no-op. Hàm này chỉ tồn tại **sau** migration 001, nên bước 3 không được bỏ.

   > 🔴 **`siteId` phải trùng đúng GUID mà license server gửi cho trạm** trong khối `datahub`
   > (G11). Đây là chỗ khớp nối duy nhất giữa bản ghi Firebase và hàng trong bảng `sites`, và nó
   > **không có kiểm tra tự động nào**. Lệch GUID ⇒ `lease/acquire` trả **404**, mà
   > `DataHubClient` coi 404 là **Unreachable chứ không phải Denied** — nên trạm **vẫn tự coi
   > mình có thể là leader**, tiếp tục gọi `/jms/ingest` (cũng 404), và người dùng chỉ thấy "dữ
   > liệu không lên VPS" mà không có lỗi nào. **Chốt danh sách `(GUID, site_code)` với chủ sở
   > hữu TRƯỚC khi provision**, đừng sinh GUID mới tại chỗ.

   - `siteId` là GUID; site code (`214A02`, lấy từ `middleCode`, chuẩn hoá **in hoa**) là cột
     riêng. Nhầm hai thứ này là S6.
   - Verify bằng câu ở §10.2 **sau khi B2 đã bỏ `s.seats`**.

**Điều kiện ra**: `GET /health/live` và `/health/ready` trả 200 qua Caddy; `schema_migrations`
có đủ 5 (hoặc 6) marker; site đã tồn tại.

---

## Phần 8 — Chặng F: publish seed mặt phẳng điều khiển — **BẮT BUỘC**

**Đây là chặng bị bỏ quên nhiều nhất, và là G4.** Bỏ nó không làm gì hỏng ngay: stack vẫn
healthy, smoke test HTTP vẫn xanh. Hậu quả chỉ hiện ra ở **máy khách ULTRA đầu tiên**, dưới
dạng "khách mua ULTRA mà không có FullStack" — và log chỉ ghi
`[Policy] source=safe-default tier=BASE`, không có error.

Cơ chế: client thử **6 đường** theo thứ tự
`configs/runtime-policy.{tier}.json` → `Urls.RuntimePolicy` →
`manifest/feature-policy.{tier}.json` → `Urls.FeaturePolicy` →
`configs/runtime-policy.json` → `manifest/feature-policy.json`.
Thất bại hết ⇒ `LoadCachedPolicy` ⇒ `RuntimePolicyDocument.SafeDefault("BASE", "safe-default")`.

**Việc làm**: `publish-manifests.{ps1,sh}` publish từ `backend/datahub/seeds/` lên
`PUT /api/v1/admin/manifests/{objectPath}`. Token admin đọc từ `$env:DATAHUB_ADMIN_TOKEN` hoặc
file env, **không bao giờ là tham số dòng lệnh** (bản `.sh` đưa token vào curl qua `--config`
trên stdin bằng `printf` builtin — không fork, không lộ trong `/proc`). Sau PUT, script **đọc
lại bằng GET không xác thực và so ETag** — đó là cách phát hiện reverse proxy cấu hình sai đang
trả cache hoặc trả file khác.

Cần publish tối thiểu:
- `configs/runtime-policy.ultra.json`, `configs/runtime-policy.base.json`,
  `configs/runtime-policy.json`
- `manifest/tier-definitions.json` (`schemaVersion: 2`; ULTRA kế thừa BASE và thêm form
  `FULLSTACK_OPERATION` với `fetchApiAfterAuthToken: true`,
  `launch: AFTER_MAINFORM_SHOWN`)

**Bốn cái bẫy khi soạn seed** — mỗi cái đã từng làm mất thời gian:

1. **Seed phải không có comment và không có dấu phẩy cuối.** `VpsRuntimePolicyService.JsonOpts`
   dung nạp (`ReadCommentHandling = Skip`, `AllowTrailingCommas`), nhưng
   `ManifestStore.TryValidatePayload` dùng `JsonDocument.Parse` mặc định và **từ chối** cả hai.
   Lỗi hiện ra lúc **publish**, không phải ở client.
2. **Google Sheets phải dùng khối typed, không dùng `features["googleSheets.provider"]`.**
   `RuntimeGoogleSheetsPolicy.Provider` mặc định là `"TokenBroker"` (**không rỗng**), và
   `RuntimePolicyApplier:32-34` chỉ đọc map `features` khi giá trị typed **rỗng** — nên khoá
   trong `features` sẽ bị bỏ qua.
3. **Đừng publish `print.*` và `debugCapture.*`.** Khi vắng, chúng lùi về `AppSettings` của
   chính trạm; publish chúng là **ghi đè lựa chọn của kỹ thuật viên ở mỗi lần khởi động**. Các
   seed hiện tại cố ý bỏ nhóm này.
4. **Policy chỉ **thu hẹp**, không **cấp** quyền.** `TierRuntimePolicy.Resolve(document,
   licenseTier)` lấy `entitlement = Resolve(licenseTier)` rồi **AND** từng cờ; cờ thiếu mặc
   định `true`. Không có cách nào mở FullStack cho BASE bằng seed — và đó là chủ ý.

Từ vựng policy **thực sự được đọc** gồm 11 khoá: 6 cổng tier
(`forms.fullStackOperation`, `fullStack.backgroundSync`, `fullStack.inventorySync`,
`fullStack.databaseTracking`, `tabs.tracking`, `tabs.print`) + `googleSheets.enabled`,
`googleSheets.provider`, `print.defaultAutoPrint`, `print.enablePrinterPreflight`,
`debugCapture.enabled`. `tabs.home`, `tabs.dkch`, `tabs.about` được `SafeDefault` **ghi** nhưng
**không ai đọc**.

**Điều kiện ra**: `publish-manifests` báo 201/200 cho từng object, ETag đọc lại **khớp**; và
`GET https://<host>/configs/runtime-policy.ultra.json` từ máy ngoài trả đúng nội dung.

---

## Phần 9 — Chặng G: smoke test và kiểm end-to-end

**Điều kiện vào**: F ra.

1. **`smoke-test.sh`** — 10 bước: provision site mới, phát assertion HMAC, enroll, acquire
   lease, ingest có fence + `Idempotency-Key`, replay cùng key, đọc changes, đọc snapshot (kiểm
   field `snapshot_seq` đúng snake_case), 5 case âm (400/401/401/409/403), release lease. Script
   **không** dùng `set -e` để mọi lỗi đều được báo, không dừng ở lỗi đầu. Cần
   `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true` + `curl`, `jq`, `python3`.
2. **Bảng 21 điểm** ở §11.10 của guide.
3. **SignalR** (§11.9): không token ⇒ hub trả 401. Mất doorbell **không** mất dữ liệu — client
   vẫn lấy đủ bằng `GET /changes?after=`. SignalR là tối ưu độ trễ, không phải điều kiện đúng
   đắn.
4. **Kiểm cách ly channel** (§11.4): device enroll ở staging phải bị **403 `CHANNEL_MISMATCH`**
   khi gọi production. Đây là bài kiểm quan trọng nhất trước cutover.
5. **End-to-end với máy trạm thật** — phần smoke test HTTP **không** phủ:
   - Một máy license **ULTRA** thật: enroll thành công, tab FullStack xuất hiện, background
     sync chạy.
   - Một máy license **BASE** thật: FullStack **không** xuất hiện, không có background
     inventory/database sync. *Nguyên tắc bất biến: BASE không bao giờ chạy background
     inventory/database sync.*
   - Đo **thời gian khởi động** ở cả hai máy để xác nhận B4 đã trị được S4 (treo 90 s).
   - Để một máy chạy **hơn 24 giờ** liên tục để bắt S3 (sync ngừng âm thầm khi gia hạn device
     token thất bại). Không có cách nào rút ngắn bài này ngoài việc hạ
     `DATAHUB_DEVICE_TOKEN_LIFETIME_SECONDS` xuống mức thấp (tối thiểu 300 s) **chỉ ở staging**
     để cưỡng bức vòng gia hạn.

**Ba trần cần biết khi thử tay:**

| Giới hạn | Giá trị | Mã lỗi |
|---|---|---|
| Ingest theo **số item** | `> 200` item | 413 `PAYLOAD_TOO_LARGE` (`IngestRepository.cs:35-36`) |
| Ingest theo **kích thước body** | `> 1 MiB` | 413 (`IngestEndpoints.cs:35-36`) |
| `changes` limit | `> 500` | **400 `BAD_REQUEST`** — từ chối, **không** clamp |
| `snapshot` rows | mặc định 5.000, tối đa 10.000 | ngoài khoảng ⇒ 400 |

Hai nguồn 413 khác nhau: một chunk **đúng luật về số lượng** vẫn có thể quá nặng, vì mỗi
observation mang một bản sao của cả dòng nguồn.

**Điều kiện ra**: `smoke-test.sh` pass toàn bộ kể cả 5 case âm; bảng 21 điểm đủ; hai máy thật
(BASE + ULTRA) hành xử đúng; bài 24 giờ không thấy sync ngừng.

---

## Phần 10 — Chặng H: backup, diễn tập restore, đưa bản sao ra ngoài

**Điều kiện vào**: E ra (có schema và dữ liệu để backup).

Đây là G6 + G7. Hiện repo có `backup-postgres.ps1` (pg_dump custom format, `compress=6`, tên
file `datahub-<UTC>.dump`, dọn temp trong `finally`) và `restore-postgres.ps1`
(`--single-transaction`, `--no-owner`, `--no-privileges`, cần cờ `-AllowExistingData` mới thêm
`--clean --if-exists`). Cả hai **hoàn chỉnh**. Thiếu là ba thứ quanh chúng:

| # | Việc | Vì sao |
|---|---|---|
| **H1** | Commit **file** lịch backup (`cron.d` entry hoặc systemd timer), không phải một dòng lệnh trong tài liệu | Guide §12.2 chỉ *ghi* dòng crontab. VPS mới ⇒ không có backup nào, và không ai nhận ra cho tới lúc cần |
| **H2** | Script mã hoá + đẩy bản sao **ra ngoài VPS** | `backup-postgres.ps1` kết thúc bằng đúng câu *"Encrypt and upload it outside this script."* Backup nằm cùng máy với DB không phải backup |
| **H3** | **Diễn tập restore thật** (§12.3) — hard gate Phase 7 của checklist, hiện chưa tick | Chưa restore được thì chưa có backup. Diễn tập vào một DB/VPS **rác**, không vào staging đang chạy |
| **H4** | Ghi lại **RPO/RTO thực đo** từ H3 vào `BACKEND_DEPLOY_STATUS.md` | Số đo thật là thứ duy nhất dùng được khi có sự cố |

Lưu ý: `restore-postgres.ps1:26` dùng cú pháp ternary của PowerShell 7 ⇒ **PS 5.1 không parse
được**. Trên VPS không sao (`bootstrap-vps.sh` cài PS 7), nhưng trên máy dev Windows mặc định
thì phải chạy bằng `pwsh`, không phải `powershell`.

Nợ kỹ thuật cần ghi rõ, **không** cố vá trong chặng này: không có down migration ⇒ rollback
schema là restore dump trước migration (§13.3); và không có checksum migration ⇒ sửa file
migration đã áp là không phát hiện được.

**Điều kiện ra**: một bản dump được tạo tự động theo lịch; một bản đã restore thành công vào
môi trường rác; RPO/RTO đã ghi.

---

## Phần 11 — Chặng I: quan trắc và cảnh báo

**Điều kiện vào**: G ra.

Hiện **không có gì**: không probe ngoài, không Prometheus (`/metrics` không tồn tại), không
alert. Cả kế hoạch ở trên chỉ có nghĩa nếu có ai đó biết khi nó gãy. Sau khi B3 đã bịt các điểm
mù của health check, cần tối thiểu:

| # | Việc | Chi tiết |
|---|---|---|
| **I1** | Probe uptime ngoài trên `GET /health/live` | Dùng `/health/live` (dependency-free), **không** dùng `/health/ready` — `ready` tiêu budget ingress 600/phút/IP và một probe dày từ một IP có thể tự rate-limit chính mình. Chu kỳ 60 s là an toàn |
| **I2** | Alert khi `/health/ready` **chuyển** sang không-200 | Poll thưa hơn (5 phút) để giữ budget |
| **I3** | Alert hết hạn TLS như **lưới an toàn** | Caddy tự gia hạn, nhưng gia hạn thất bại thầm lặng là mất toàn bộ fleet |
| **I4** | Cảnh báo disk | Retention là hàng rào duy nhất; job retention đứng là bảng phình dần **không có hook quan trắc nào** (S12) |
| **I5** | Thêm hook quan trắc cho retention job: ghi số dòng đã xoá + lần chạy cuối vào một endpoint hoặc bảng đọc được | Vá S12 tận gốc |
| **I6** | Thêm `X-Request-Id` trên response **thành công** và một endpoint `/api/version` trả build-ID | Hiện traceId chỉ có trong body lỗi ⇒ không lần được một request thành công nhưng sai |
| **I7** | Alert cho **enroll thất bại** | Hiện **không có audit entry** cho enroll thất bại. Một đợt enroll thất bại hàng loạt là dấu hiệu của G2/G3 và cần biết trong vài phút, không phải vài tuần |

**Một quy tắc log tuyệt đối, liên quan tới Caddy:**
`DeviceAuthenticationMiddleware.cs:32-42` nhận `?access_token=` **chỉ** trên `/hubs/site` và
**chỉ** khi không có header `Authorization` — vì transport WebSocket của SignalR không luôn đặt
được header. Hệ quả vận hành: **nếu bật access log ở Caddy thì phải không ghi query string cho
`/hubs/site`**, nếu không **bearer token của thiết bị sẽ nằm trong log truy cập**. Caddy mặc
định **không** ghi access log, nên hiện tại an toàn — rủi ro nằm ở lần đầu ai đó bật log để
debug. Ghi điều này vào guide.

**Điều kiện ra**: có ít nhất một alert đến được điện thoại chủ sở hữu; đã thử **cố ý** làm
`/health/ready` đỏ và xác nhận alert tới.

---

## Phần 12 — Chặng J: VPS production và cutover

**Điều kiện vào**: A→I đã ra **hết** trên staging, và staging đã chạy ổn định với máy thật ít
nhất **7 ngày**.

### J1 — Provision VPS production ⛔

**4 vCPU / 8 GB / 80 GB SSD / Ubuntu 24.04 LTS.** Lặp lại C2, C3 với hostname
`datahub-production` và A record riêng.

### J2 — Khác biệt cấu hình so với staging

| Biến | Staging | Production |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Staging` | `Production` |
| `DATAHUB_CHANNEL` | `staging` | `production` |
| `DATAHUB_ALLOW_STAGING_TEST_ISSUER` | `true` | **`false`** |
| `DATAHUB_STAGING_TEST_SIGNING_KEY` | có giá trị | **rỗng** |
| `DATAHUB_LICENSE_ASSERTION_ISSUER` | `autojms-license-staging` | `autojms-license-production` |
| `DATAHUB_LICENSE_ASSERTION_AUDIENCE` | `autojms-datahub-enroll-staging` | `autojms-datahub-enroll-production` |
| `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` | cặp khoá staging | **cặp khoá production, khác** |

**Mọi secret khác đều phải khác** — `POSTGRES_PASSWORD`, `DATAHUB_DEVICE_TOKEN_SIGNING_KEY`,
`DATAHUB_ENROLLMENT_PEPPER`, `DATAHUB_ADMIN_TOKEN`. Dùng lại secret của staging ở production
làm mất toàn bộ ý nghĩa của việc tách hai VPS.

Thứ tự đăng ký validator trong `AddDataHubIdentity`
([`Auth/IdentityServiceCollectionExtensions.cs:20`](../../src/AutoJMS.DataHub.Api/Auth/IdentityServiceCollectionExtensions.cs)):
HMAC staging (chỉ khi bật staging opt-in **và** `channel == "staging"`) → nếu không thì RSA khi
`HasKeyMaterial` → nếu không thì `UnavailableLicenseAssertionValidator`. Nên ở production, để
trống public key = enroll đóng, **không** có đường lùi về HMAC. Đó là fail-closed đúng thiết kế.

### J3 — Trình tự cutover

Theo §15.2 của guide, **bỏ qua §15.3** (dùng chung một VPS — không áp dụng với quyết định 3).

1. Chạy lại E → F → G trên VPS production (**gồm cả Chặng F publish seed** — seed **không**
   copy được từ staging vì nằm trên VPS khác).
2. **Canary** (§15.4): chuyển **một** trạm sang production trước. Xác nhận enroll, lease,
   ingest, doorbell, và tab FullStack đúng theo tier.
3. Kiểm cách ly: trạm cũ còn enroll ở staging phải nhận **403 `CHANNEL_MISMATCH`** khi trỏ vào
   production. Nếu **không** nhận 403 thì `DATAHUB_CHANNEL` đặt sai — **dừng cutover**.
4. Mở rộng theo lô, không mở toàn bộ một lần.
5. Sau khi lô cuối xong: giữ staging **chạy** làm môi trường thử bản cập nhật, đừng tắt.

**Điều kiện ra**: canary chạy ≥ 48 giờ không sự cố; bài 403 `CHANNEL_MISMATCH` pass.

---

## Phần 13 — Rollback

Hiện **không có** `rollback.sh`/`rollback.ps1`. §13 của guide mô tả ba mức, đều là lệnh tay:

| Mức | Cách | Điều kiện tiên quyết |
|---|---|---|
| **Image** (nhanh nhất, không đụng dữ liệu) | `sed` sửa `DATAHUB_API_IMAGE` trong `.env` về digest cũ, chạy lại `start-stack.ps1` | **Digest cũ phải đã được lưu.** Không lưu là không rollback nhanh được — guide khuyên lưu vào password manager |
| **Code vận hành** (Compose/Caddyfile) | `git switch --detach <commit>` rồi `start-stack.ps1` | Repo có trên VPS |
| **Schema** | Restore `pg_dump` trước migration | **Không có down migration.** Phải có dump chụp **trước** khi áp migration |

**Ba việc nên làm để rollback không phụ thuộc trí nhớ:**

- **R1**: viết `rollback.ps1`/`.sh` gói mức Image, đọc digest trước đó từ một file
  `deployed-digests.log` do `start-stack.ps1` **tự ghi thêm**.
- **R2**: `apply-migrations` tự `pg_dump` **trước** khi áp bất kỳ migration mới, ghi vào thư mục
  backup. Đây là điều kiện tiên quyết của mức Schema, hiện đang là kỷ luật con người.
- **R3**: một cạnh sắc chưa được xử lý — với `restart: unless-stopped`, nếu stack **đã bị
  `docker compose down` tường minh** trước khi reboot thì nó **không** tự khởi động lại sau
  reboot. Không có file nào trong repo trị việc này. Cách trị: một systemd unit oneshot gọi
  `docker compose up -d`, hoặc kỷ luật "không bao giờ `down` mà không `up` lại ngay".

---

## Phần 14 — Lịch trình và các cổng chặn

```
A  Chốt nguồn deploy license server        ⛔ chủ sở hữu       [CHẶN TẤT CẢ]
│  └─ ra: verify-license trả khối `datahub` có assertion
▼
B  Vá code trên máy dev (B0…B15)           agent               [B13 cần phép]
│  └─ ra: build 0 error + verify.ps1 ALL GATES PASSED
▼
C  VPS staging: mua, DNS, hardening        ⛔ + agent
│  └─ ra: chỉ 22/80/443 mở, 5432 kín, clock synced
▼
D  Khoá + env                              ⛔ (khoá) + agent
│  └─ ra: public key trên VPS, private key CHỈ ở Render, issuer/audience khớp ký tự
▼
E  Stack + migration + site                agent
│  └─ ra: /health/ready 200, 5–6 marker migration, site tồn tại
▼
F  Publish seed mặt phẳng điều khiển       agent               [BẮT BUỘC]
│  └─ ra: ETag đọc lại khớp; GET runtime-policy.ultra.json từ ngoài đúng
▼
G  Smoke test + máy thật BASE/ULTRA        agent + ⛔
│  └─ ra: smoke pass, 2 máy đúng tier, bài 24h không ngừng sync
▼
H  Backup theo lịch + diễn tập restore     agent + ⛔
│  └─ ra: dump tự động + một lần restore thành công + RPO/RTO đã đo
▼
I  Quan trắc + alert                       agent + ⛔
│  └─ ra: alert tới được điện thoại, đã thử làm đỏ có chủ ý
▼
   [staging chạy với máy thật ≥ 7 ngày]
▼
J  VPS production + canary + cutover       ⛔ + agent
   └─ ra: canary ≥ 48h, 403 CHANNEL_MISMATCH pass
```

**Bảy cổng không được đi tắt, và lý do:**

1. **A trước tất cả** — không có assertion thì không enroll, mọi thứ sau đó không kiểm được.
2. **DNS (C2) trước lần `docker compose up` đầu tiên** — Caddy xin cert ACME ngay khi bind 443
   lần đầu. Domain chưa resolve ⇒ challenge thất bại, và thất bại lặp lại có thể **chạm rate
   limit của Let's Encrypt** và khoá bạn nhiều giờ. Đây là cổng duy nhất mà đi tắt gây thiệt hại
   **không sửa được bằng cách thử lại ngay**.
3. **Migration (E3) + kiểm schema (E4) trước khi mở traffic** — health check **không** biết
   schema rỗng (G12); `healthy` không phải bằng chứng.
4. **`(siteId, site_code)` được chốt trước E5** — provision sai GUID cho ra 404 mà trạm đọc
   thành "Unreachable", nên nó **không tự sửa** (G11).
5. **F trước máy ULTRA đầu tiên** — bỏ F là bán ULTRA mà giao BASE, im lặng.
6. **H3 (restore thật) trước J** — production không có backup đã kiểm là rủi ro không hoàn tác
   được.
7. **I trước J** — production không có alert nghĩa là sự cố được phát hiện bởi khách hàng.

**Một quy tắc thứ tự xuyên chặng**: đừng trỏ máy khách đang chạy sang license server mới trước
khi VPS tương ứng đã qua Chặng F — xem hộp cảnh báo cuối Chặng A.

---

## Phần 15 — Việc chỉ chủ sở hữu quyết được

| # | Việc | Chặn chặng nào |
|---|---|---|
| 1 | **L-1**: chọn A1/A2/A3 cho nguồn deploy license server | A — chặn tất cả |
| 2 | Sinh 2 cặp khoá RSA assertion (staging + production), đặt private ở Render, public ở VPS | D |
| 3 | **Thu hồi Supabase anon key** đang bị bản production phát cho mọi client (L-3) | song song, càng sớm càng tốt |
| 4 | Ghim Render về **1 instance** (JTI cache + rate limiter đều in-memory) | A |
| 5 | Mua 2 VPS + tạo 2 A record | C, J |
| 6 | Xử lý box `dev.jmsauto.online`: dùng lại làm staging hay cho nghỉ | C0 |
| 7 | **Cho phép sửa migration** (B13: `jti_cache` + 3 index) — migration là Protected File | B13 |
| 8 | Cho phép sửa `LicenseApiService.cs` (Protected) để trạm cảnh báo **trước** khi license hết hạn (C1/C6), và để `heartbeat` 5xx không còn là lỗi chí tử (`heartbeat-5xx-fatal`, `LicenseApiService.cs:659`) | song song |
| 9 | Cho phép sửa `Program.cs` nếu B4 (ngân sách fetch policy) cần chạm tới | B4 |
| 10 | Danh sách **cặp `(siteId GUID, site_code)`** thật để provision, khớp với bản ghi Firebase; và backfill `middleCode` (không được để `"0000"`) | E — G11 |
| 10b | Đặt `DATAHUB_API_BASE_URL` = hostname VPS thật cho từng môi trường (G10) | A, J |
| 11 | `VALID_EXE_HASHES` đang rỗng (J-3/H-1) | song song |
| 12 | **L-5/J-2**: tài liệu rủi ro có mục chưa vá đang nằm trong repo **công khai**, và `AutoJMS-API` cũng công khai | quyết định riêng |

---

## Phần 16 — Phụ lục: biến môi trường theo host

Bảng này để tra khi lỗi có dạng "đã đặt biến mà không tác dụng".

### 16.1 License server (Render / off-VPS)

| Biến | Bí mật | Ghi chú |
|---|---|---|
| `JWT_PRIVATE_KEY` / `JWT_PUBLIC_KEY` | ✅ | cặp RS256 cho token phiên |
| `FIREBASE_SERVICE_ACCOUNT_BASE64` (hoặc `_JSON`, `_FILE`, `GOOGLE_APPLICATION_CREDENTIALS`, fallback `./serviceAccountKey.json`) | ✅ | `firebase-credentials.js` nhận 5 nguồn, ưu tiên inline trước file |
| `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` | ✅ | **chỉ ở đây, không bao giờ lên VPS.** Env-var-only ⇒ thấy được qua `docker inspect`/`/proc`; **không có** biến thể `_FILE` |
| `DATAHUB_LICENSE_ASSERTION_ISSUER` | ✖ | **phải đặt tay**; default `autojms-license` **không khớp VPS** |
| `DATAHUB_LICENSE_ASSERTION_AUDIENCE` | ✖ | **phải đặt tay**; default `autojms-datahub-enroll` **không khớp VPS** |
| `DATAHUB_API_BASE_URL` | ✖ | default là placeholder `https://datahub.example.com`, **trả cho client không báo lỗi nếu sai** |
| `DATAHUB_CHANNEL` | ✖ | `production` / `staging` |
| `LICENSE_BILLING_ANCHOR_DAY` | ✖ | `16` |
| `LICENSE_GRACE_DAYS` | ✖ | `7` |
| `VALID_EXE_HASHES` | ✖ | đang rỗng (J-3) |
| `FIREBASE_OPERATION_TIMEOUT_MS` | ✖ | |

Rate limiter hiện có: `limiter` 20/phút (verify-license, logout), `heartbeatLimiter` 120/phút,
`googleSheetsGrantLimiter` 60/phút, `datahubAssertionLimiter` 60/phút, `healthLimiter` 30/phút.
`jtiCache = NodeCache({ stdTTL: 3600 })`. Tất cả **in-memory** ⇒ **1 instance**.

### 16.2 VPS (mỗi VPS một bản riêng)

| Biến | Bí mật | Ai đọc |
|---|---|---|
| `ConnectionStrings__DataHub` | ✅ | **.NET** — chỉ dạng gạch dưới đôi có tác dụng trong env var |
| `DATAHUB_DEVICE_TOKEN_SIGNING_KEY` | ✅ | .NET |
| `DATAHUB_ENROLLMENT_PEPPER` | ✅ | .NET — **phải khác** signing key ở trên |
| `DATAHUB_ADMIN_TOKEN` | ✅ | .NET — thiếu ⇒ health vẫn **Healthy**, mọi PUT manifest **503** (G5) |
| `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` / `_PUBLIC_KEY_PATH` | ✖ | .NET — `_PATH` **ưu tiên hơn** inline |
| `DATAHUB_LICENSE_ASSERTION_ISSUER` / `_AUDIENCE` | ✖ | .NET — **không** được health check kiểm (S11-tương tự) |
| `DATAHUB_DEVICE_TOKEN_ISSUER` / `_AUDIENCE` | ✖ | .NET — **không** được health check kiểm; sai ⇒ mọi token `TOKEN_INVALID` mà host vẫn Healthy (S11) |
| `DATAHUB_DEVICE_TOKEN_LIFETIME_SECONDS` | ✖ | .NET — mặc định 86400, kẹp `300..2592000` |
| `DATAHUB_CHANNEL` | ✖ | .NET — fallback `DataHub:Channel` ở `DataHubRuntimeOptions.cs:67` |
| `DATAHUB_TRUSTED_PROXY_NETWORKS` | ✖ | .NET — **mặc định quá rộng**, phải thu hẹp (D3) |
| `DATAHUB_MANIFEST_ROOT` | ✖ | .NET — phải khớp mount, nếu không publish mất im lặng (D3) |
| `DATAHUB_ALLOW_STAGING_TEST_ISSUER` | ✖ | .NET — `true` **chỉ** staging |
| `DATAHUB_STAGING_TEST_SIGNING_KEY` | ✅ | .NET — chỉ staging, ≥ 32 ký tự |
| `ASPNETCORE_ENVIRONMENT` | ✖ | .NET |
| `DATAHUB_PUBLIC_HOST` | ✖ | **Caddy** — .NET không đọc |
| `TLS_CONTACT_EMAIL` | ✖ | **ACME** — .NET không đọc |
| `DATAHUB_API_IMAGE` | ✖ | **Compose** — phải là `@sha256:` |
| `POSTGRES_DB` / `_USER` / `_PASSWORD` | ✅ | **Compose/Postgres** — .NET **chỉ** thấy `ConnectionStrings__DataHub` |

Chuỗi middleware DataHub (thứ tự có ý nghĩa): `UseExceptionHandler` → `UseForwardedHeaders` →
`IngressRateLimitMiddleware` → `AdminAuthenticationMiddleware` → `DeviceAuthenticationMiddleware`
→ `UseRateLimiter` → `DeviceStatusMiddleware`.
`IngressIpRateLimiter`: ingress **600/phút/IP**, device **240/phút/deviceId**, admin **30/phút/IP**.

---

## Phần 17 — Nguyên tắc bất biến (không thương lượng trong mọi chặng)

1. **BASE không bao giờ** chạy background inventory/database sync.
2. **Không bypass gate tier** để mở FullStack cho BASE. Luôn dùng `_tierPolicy.EnableXxx`, không
   hardcode `if (CurrentTier == "ULTRA")`.
3. **Nửa private của khoá assertion không bao giờ có mặt trên VPS.**
4. **Policy chỉ thu hẹp, không cấp quyền** — seed không thể mở quyền vượt license.
5. **Không bao giờ push khi build fail.** Không force push, không rewrite history.
6. **Không commit** `.env`, service account key, `*.pfx`, `*.pem`, hay bất kỳ file token/khoá.
   Log phải che token dạng `first4...last4`.
7. **Tab `ABOUT` luôn là tab cuối** trong bộ tab UI.
8. Trước khi L-1 (Chặng A) xong, mọi kết luận **"đã vá"** trong
   [`FULLSTACK_BACKEND_RISK_REVIEW.md`](FULLSTACK_BACKEND_RISK_REVIEW.md) chỉ đúng với **repo**,
   **không** đúng với **production**. Mọi báo cáo trạng thái license server phải nói rõ đang nói
   về repo hay về production.
