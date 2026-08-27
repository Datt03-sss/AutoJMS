# Kế hoạch xây dựng backend AutoJMS và triển khai từng bước lên VPS

Ngày lập: 2026-08-25 · Sửa lần 2: **2026-08-26** · Nhánh: `main` · Trạng thái: **Chặng A đã chốt
và đã chuẩn bị xong phía repo; chưa cutover trên dashboard Render**

**Có gì mới ở bản 2026-08-26** — chủ sở hữu đã chốt **L-1 = A1**: trỏ Render Web Service về
monorepo `AutoJMS`, thư mục `backend/render-license-server`, blueprint `render.yaml`. Đó là
mục chặn số một của cả kế hoạch, nên bản này viết lại Phần 3 thành **runbook thi hành** thay
vì bảng lựa chọn, và cập nhật mọi mục đã lệch:

| Mục | Trước | Nay |
|---|---|---|
| G1 / L-1 | ⛔ chờ chủ sở hữu chọn | ✅ **đã chốt A1** — xem Phần 3 |
| Blueprint | `backend/render.yaml` | **`render.yaml` ở gốc repo** — Render *không đọc* file ở thư mục con (Phần 3.0) |
| G8 / B1 | `ApiProblemDetails.cs:33` hardcode | ✅ **đã vá** — đọc `DATAHUB_PUBLIC_HOST` |
| G12 / B16 | health check không kiểm schema | ✅ **đã có sẵn từ trước** — `PostgresDataSource.CanConnectAsync` |
| S4 / B4 | treo 90 s, "cần xin phép `Program.cs`" | ✅ **đã vá không cần đụng `Program.cs`** — ngân sách 8 s trong `VpsRuntimePolicyService` |
| B13 `CONCURRENTLY` | runner ép `--single-transaction` cho mọi file | ✅ **runner đã nhận `*_notx.sql`** — đường số 2 trong ô cảnh báo nay khả dụng |
| License server | 115 test / 9 file · `server.js` 1.250 dòng | **123 test / 10 file · `server.js` 1.550 dòng**; graceful shutdown đã có (`8291ced`) |

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
| Migration | 5 file forward-only 001–005 + `apply-migrations.{ps1,sh}` idempotent, mỗi file `--single-transaction` + `ON_ERROR_STOP=1`, đọc lại version marker và ném lỗi nếu không ghi được. **Từ 2026-08-26**: file `*_notx.sql` hoặc có dòng `-- no-transaction` được áp **ngoài** transaction, mở đường cho `CREATE INDEX CONCURRENTLY` (xem ô cảnh báo ở B13) |
| Ops script | `start-stack.ps1` (ép `@sha256`, `--no-build`, so digest sau pull), `provision-site.ps1`, `backup-postgres.ps1`, `restore-postgres.ps1`, `publish-manifests.{ps1,sh}`, `smoke-test.sh` (10 bước + 5 case âm), `deployment-static-smoke.ps1` (20+ check tĩnh) |
| Mặt phẳng điều khiển | `PUT /api/v1/admin/manifests/{**objectPath}` **đã có** (xem 0.2), `ManifestStore` giới hạn 1 MiB, ETag = SHA-256 hex |
| Xác thực assertion | `RsaLicenseAssertionValidator` hoàn chỉnh (`v1rs256`, RSA ≥ 2048, PKCS#1 v1.5 / SHA-256, từ chối khoá private) |
| License server | **123 test / 10 file**, `license-expiry.js` theo mốc ngày 16, blueprint đầy đủ ở **`render.yaml` gốc repo** (xem 3.0), `firebase-credentials.js` nhận 5 nguồn credential, graceful shutdown (`8291ced`) |
| Health check schema | `PostgresDataSource.CanConnectAsync` **đã** kiểm ≥12 bảng lõi + `EXISTS` cả 5 marker migration (`001_core`…`005_change_retention_floor`) + có dòng trong `jms_event_policies` và `retention_policies` — G12/B16 **đã xong từ trước**, không phải việc còn lại |

### 2.2 Chặn go-live — phải xong trước khi mở cho khách

| # | Khoảng trống | Vì sao chặn | Ai làm |
|---|---|---|---|
| ~~**G1**~~ ✅ | ~~Chưa chốt **nguồn deploy** của license server (mục L-1)~~ **ĐÃ CHỐT 2026-08-26: A1** | Render production đang chạy repo `AutoJMS-API` (HEAD `c6f05433`, `server.js` 895 dòng), **không** phải `backend/render-license-server/` (**1.550** dòng). Bản đang chạy **thiếu** `issueDataHubAssertion`, khối `datahub` trong response, `seats`, `tokenVersion` — nghĩa là **không phát được assertion**, nghĩa là **không enroll được**, nghĩa là toàn bộ VPS vô dụng. Blueprint đã sẵn sàng ở `render.yaml` gốc repo; **còn lại là 7 thao tác dashboard ⛔ (bảng A1-a…A1-g, mục 3.4)** | ⛔ chủ sở hữu (thi hành) |
| **G2** | `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` (hoặc `_PATH`) chưa có trên VPS | API nạp `UnavailableLicenseAssertionValidator`; `POST /devices/enroll` trả **503** fail-closed | ⛔ chủ sở hữu sinh cặp khoá |
| **G3** ⚠️ | `_ISSUER`/`_AUDIENCE` phải **khớp từng ký tự** giữa hai host | Default trong code license server là `autojms-license` / `autojms-datahub-enroll`; template production của VPS đòi `autojms-license-production` / `autojms-datahub-enroll-production`. Lệch ⇒ assertion ký đúng vẫn bị từ chối `LICENSE_ASSERTION_INVALID`, trạm chỉ báo "enroll thất bại". **A1 khép mục này cho kênh production**: blueprint gốc và `env.production.template` đã khớp từng ký tự (3.4 mục 3). Rủi ro còn lại là **áp blueprint hụt** rồi rơi về default không hậu tố | agent (đã đối chiếu) + ⛔ chủ sở hữu (áp blueprint) |
| **G4** | Seed mặt phẳng điều khiển chưa publish | Fetch policy đi 6 đường, thất bại hết ⇒ `RuntimePolicyDocument.SafeDefault("BASE")`. Máy ULTRA **chạy như BASE**, log chỉ ghi `[Policy] source=safe-default tier=BASE`, **không có error nào** | agent chạy script sau khi có `DATAHUB_ADMIN_TOKEN` |
| **G5** | `DATAHUB_ADMIN_TOKEN` không có ⇒ health check vẫn báo **Healthy**, chỉ kèm một dòng ghi chú text | Mọi `PUT` manifest trả 503 ⇒ cả fleet **âm thầm** không bao giờ nhận policy hay bản cập nhật | agent (sửa health check) |
| **G6** | Không có backup theo lịch | Guide §12.2 chỉ *ghi* dòng crontab; repo **không** có `cron.d`, không systemd timer, không crontab file. VPS mới ⇒ **không có backup nào** | agent (thêm file) |
| **G7** | Chưa diễn tập restore | `restore-postgres.ps1` có, nhưng chưa ai chạy thật. Backup chưa restore được là backup chưa tồn tại | agent + ⛔ xác nhận |
| ~~**G8**~~ ✅ | ~~`ApiProblemDetails.cs:32` hardcode `https://datahub.example.com/problems/...`~~ **ĐÃ VÁ 2026-08-26** | Domain giả rò ra mọi response lỗi; và vì hai VPS có hai domain nên giá trị này **phải là cấu hình**, không phải hằng. Nay đọc `DATAHUB_PUBLIC_HOST`; bỏ trống hoặc trỏ tên RFC 2606 ⇒ phát URI **tương đối** `/problems/...` (RFC 7807 §3.1 cho phép, và nó tự phân giải về đúng host mà client vừa gọi) | ✅ agent |
| ~~**G9**~~ ✅ | ~~Guide §10.2 dùng câu verify `s.seats` — **cột này không tồn tại**~~ **ĐÃ VÁ 2026-08-26 (B2)** | Thực tế còn hỏng hơn: `sites` có khoá chính tên `id`, nên `s.site_id` cũng không tồn tại và cả hai `USING (site_id)` đều lỗi. `seats` chỉ sống trong JWT assertion (`LicenseAssertionIdentity.Seats`), không lưu DB | agent |
| **G10** | `DATAHUB_API_BASE_URL` trên license server vẫn là placeholder `https://datahub.example.com` | Kể cả sau khi A và G2 xong: license server trả URL này cho client trong khối `datahub`, **không báo lỗi**. `DataHubClient` được `Configure` với token thật rồi gọi một domain không resolve ⇒ mọi request timeout. Trạm không biết mình đang gọi sai chỗ. **Nửa repo đã vá**: blueprint gốc đổi biến này sang `sync: false`, nên nó thôi **ghi đè** giá trị dashboard mỗi lần sync — đó mới là lý do lỗi này tái phát chứ không phải một lần quên (3.2) | ⛔ chủ sở hữu (điền ở bước A1-b) |
| **G11** | `siteId` khi provision trên VPS **phải trùng đúng GUID** mà license server gửi trong khối `datahub` | Lệch GUID ⇒ `POST /sites/{siteId}/lease/acquire` trả **404**. `DataHubClient` coi 404 là **Unreachable**, *không* phải Denied — nên trạm **vẫn tự coi mình có thể là leader** và tiếp tục gọi `/jms/ingest` (cũng 404). Mọi write thất bại, không có thông báo | ⛔ (chốt danh sách GUID) + agent |
| ~~**G12**~~ ✅ | ~~Health check **không** kiểm schema — stack báo Healthy với DB **rỗng**~~ **MỤC NÀY ĐÃ SAI KHI VIẾT** | Kiểm chứng lại 2026-08-26 tại [`src/AutoJMS.DataHub.Api/Infrastructure/PostgresDataSource.cs`](../../src/AutoJMS.DataHub.Api/Infrastructure/PostgresDataSource.cs): `CanConnectAsync` **không** chỉ mở kết nối — nó chạy một câu SQL đếm ≥12 bảng lõi, `EXISTS` cả 5 marker `schema_migrations`, và `EXISTS` dòng trong `jms_event_policies` + `retention_policies`. Lưới an toàn cho bước migration thủ công **đã có**. B16 do đó là **việc không tồn tại** | ✅ không phải làm |

### 2.3 Rủi ro thầm lặng — không chặn boot, nhưng gây "chạy mà sai"

Điểm chung của cả nhóm: **hệ thống báo khoẻ, người dùng không thấy lỗi, dữ liệu vẫn sai hoặc
ngừng chảy.** Đây là nhóm đắt nhất khi bỏ qua.

| # | Hiện tượng người dùng thấy | Nguyên nhân thật |
|---|---|---|
| S1 | Tab FullStack trống | enroll thất bại ⇒ `HasCredentials=false` ⇒ mọi đường DataHub thành no-op. **Không phân biệt được** với `siteId` sai hay VPS chết. Và **không có retry/backoff** — DataHub tắt cả session |
| S2 | Máy ULTRA thiếu FullStack, thiếu background sync | Policy 404 cả 6 đường ⇒ `SafeDefault('BASE')`. Triệu chứng **y hệt** license BASE |
| S3 | Sync ngừng ~24 h sau khi bật máy | Gia hạn device token thất bại ⇒ giữ token cũ đã hết hạn ⇒ 401 mọi call. Không có thông báo |
| ~~S4~~ ✅ | ~~App treo tới 90 giây lúc khởi động~~ **ĐÃ VÁ 2026-08-26** | Fetch runtime policy đi 6 URL **tuần tự** (`VpsRuntimePolicyService.cs:95-103`), mỗi URL timeout 15 s (`Updates/VpsManifestService.cs:29`), và lời gọi bọc ngoài chạy **đồng bộ trên luồng UI** bằng `.GetAwaiter().GetResult()` — chỗ đó là [`Program.cs:356-359`](../../src/AutoJMS/Program.cs), **không** phải trong `VpsRuntimePolicyService` như báo cáo rủi ro ghi. Phân biệt này quyết định ai vá được: `Program.cs` là **Protected File**, còn `VpsRuntimePolicyService.cs` thì không — nên đặt **ngân sách tổng 8 s** vào chính service là vá được **mà không cần xin phép** (chi tiết B4) |
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
- ~~**`backend/backend-schema-dump.sql` là file rỗng**~~ **ĐÃ VÁ 2026-08-26 (B9)** — dump thật đã
  sinh từ DB đã áp 001–005 trên VPS. Vẫn **không có checksum**: dump là ảnh chụp một thời điểm,
  không phải cơ chế phát hiện migration bị sửa sau khi áp.
- **Không có CI**: repo **không có** `.github/`. `eng/harness/verify.ps1` chỉ chạy local. Build
  và push image hoàn toàn thủ công.
- **Không có script deploy tổng, không có script rollback**: một lần deploy sạch cần operator
  chạy 4–6 lệnh riêng theo đúng thứ tự. Rollback là chuỗi lệnh tay trong §13, và **digest image
  cũ phải do operator tự lưu** — không lưu là không rollback nhanh được.
- **Không có nơi lưu secret ngoài file `.env`**: không Vault, không Docker secret, không rotation
  tự động, không audit truy cập.
- **Không có quan trắc uptime**: không probe ngoài, không Prometheus (`/metrics` không tồn tại),
  không alert. Guide §14.5 chỉ đề nghị "xem lịch mỗi tuần".
- **License server**: ~~không graceful shutdown~~ (**đã có từ `8291ced`**), không
  structured logging, JTI replay cache là `NodeCache` **in-memory** (restart là xoá ⇒ access
  token phát trước đó **replay được** ở `/api/heartbeat` trong phần còn lại của 60 phút; hai
  instance cũng vậy), rate limiter dùng store in-memory (hai instance hoặc rolling restart là
  vượt được budget). **Đây chính là lý do blueprint ghim `numInstances: 1`** (3.1) — ràng buộc
  đúng đắn, không phải trần dung lượng. `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` là
  **env-var-only**, thấy được qua `docker inspect` và `/proc` — **không có** biến thể
  `_PRIVATE_KEY_FILE`.
- **`DATAHUB_API_BASE_URL` mặc định là placeholder `https://datahub.example.com`** và được trả
  về cho client **không báo lỗi** nếu sai. Blueprint nay để `sync: false` nên **thôi ghi đè**
  giá trị thật ở mỗi lần sync (3.2); phần "code vẫn nhận placeholder mà không kêu" thì **chưa
  vá** — xem đề xuất Opt-3 ở Phần 18.
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

## Phần 3 — Chặng A: cutover license server sang monorepo (A1) ⛔

**Đã chốt ngày 2026-08-26**: **A1** — trỏ Render Web Service về monorepo `AutoJMS`, thư mục
`backend/render-license-server`, dùng blueprint `render.yaml`. Mục L-1 khép lại. A2 (duy trì
song song hai bản) và A3 (service mới chạy song song) **không** chọn; lý do và điều kiện lật
lại ghi ở 3.5.

**Điều kiện vào**: không có.

### 3.0 Phát hiện quyết định: blueprint **phải** nằm ở gốc repo

Render tìm Blueprint **chỉ** ở `render.yaml` tại **gốc** repository đã kết nối. Không có tuỳ
chọn nào để trỏ sang đường dẫn khác. Bản `backend/render.yaml` vốn có trong repo vì vậy
**chưa bao giờ được Render đọc** — nó mô tả một cách triển khai không thể xảy ra.

Đây không phải chi tiết vụn. Nó có nghĩa: câu "A1 = dùng `backend/render.yaml` làm blueprint"
ở bản kế hoạch trước là **một chỉ dẫn không thi hành được**. Nội dung file phần lớn đã đúng
(nó đã có `rootDir: backend/render-license-server`), nhưng vị trí thì sai, nên A1 sẽ lặng lẽ
biến thành "deploy bằng cấu hình gõ tay trên dashboard" — đúng thứ mà A1 sinh ra để loại bỏ.

Đã xử lý trong repo (2026-08-26):

- **[`render.yaml`](../../render.yaml) ở gốc** — blueprint thật, `rootDir:
  backend/render-license-server`.
- **[`backend/render.yaml`](../../backend/render.yaml)** — rút thành stub chỉ có comment trỏ
  sang file gốc. Không xoá: `BACKEND_DEPLOY_STATUS.md` và vài báo cáo audit còn dẫn đường dẫn
  này, một file rỗng nghĩa "đã dời" đọc rõ hơn một đường dẫn 404.

### 3.1 Blueprint gốc khác `backend/render.yaml` cũ ở những gì

| Khoá | Trước | Nay | Vì sao |
|---|---|---|---|
| Vị trí file | `backend/render.yaml` | **gốc repo** | Render chỉ đọc ở gốc (3.0) |
| `numInstances` | không đặt | **`1`** | `jtiCache = NodeCache({stdTTL:3600})` (`server.js:290`) và mọi store của `express-rate-limit` đều **in-process**. Instance thứ hai là **cửa sổ chống replay thứ hai** và **budget rate-limit thứ hai**, không phải khả năng chịu tải. Đây là ràng buộc **đúng đắn**, không phải ràng buộc dung lượng |
| `buildFilter` | không có | `paths: [backend/render-license-server/**]` | `rootDir` giới hạn *chỗ build*, **không** giới hạn *cái gì kích hoạt deploy*. Thiếu nó, khi bật `autoDeploy` thì mỗi commit WinForms cũng redeploy license server |
| `NODE_VERSION` | không đặt | `"22"` | `package.json` chỉ đòi `>=20`; không ghim là để Render tự chọn, tức bản Node có thể đổi dưới chân mình giữa hai lần deploy |
| `DATAHUB_API_BASE_URL` | inline `https://datahub.example.com` | **`sync: false`** | Xem 3.2 — đây là phần quan trọng nhất của bảng |
| `healthCheckPath` | `/health` | `/health` (giữ) | `/health` không phụ thuộc dependency và **không** qua rate limiter (`server.js:573`), khác `/health/firebase` (nằm sau `healthLimiter`, 30/phút). Để Render poll `/health/firebase` là tự ăn budget của chính mình |

### 3.2 `sync: false` cho `DATAHUB_API_BASE_URL` — vá G10 tận gốc

Bản cũ ghi thẳng `DATAHUB_API_BASE_URL: https://datahub.example.com` vào blueprint. Điều đó
**không chỉ** là "chưa điền giá trị thật": mỗi lần sync blueprint, Render **ghi đè** giá trị
placeholder này lên đúng cái hostname thật mà chủ sở hữu đã đặt trên dashboard. Nghĩa là G10
không phải một lần quên — nó là một lỗi **tái phát theo mỗi lần deploy**.

`sync: false` khiến Render **hỏi** giá trị khi apply blueprint và **thôi đè** lên giá trị đã
có. Đó cũng là cách mô tả đúng bản chất biến này: nó **khác nhau giữa staging và production**,
nên không có giá trị nào trong repo là đúng cho cả hai.

Agent **không** điền hostname thật vào file — đó là giá trị vận hành, và điền sẵn một giá trị
sai lại đưa chính lỗi này quay về.

### 3.3 Vùng (region) — cửa sổ chỉ mở đúng một lần

Blueprint **cố ý không** đặt `region`. Lý do phải nói rõ vì nó bất đối xứng:

- Render mặc định **Oregon**. Đường nóng của license server là Firebase RTDB ở
  **`asia-southeast1`** (xem `FIREBASE_DATABASE_URL`). Chỉ riêng `verify-license` đã có **một
  lần đọc license, một lần ghi session và một lượt quét dọn session cũ** — nên khoảng cách đó
  bị trả **nhiều lần cho mỗi lần mở app** (Oregon→Singapore ≈170–200 ms RTT, so với vài
  mili-giây đến vài chục nếu cùng vùng).
- **Region của một service không đổi được sau khi tạo.** Nên với A1 — *trỏ lại service đang
  có* — thêm `region: singapore` vào blueprint là vô nghĩa: service đã tồn tại, khoá region
  đã đóng. Đặt nó vào file chỉ tạo ảo giác đã tối ưu.
- Muốn ở gần Firebase thì phải **tạo service mới** ở Singapore, tức là quay về A3. Đó là một
  đánh đổi độc lập với A1, và đã ghi ở mục 9 của Phần 15 để chủ sở hữu quyết riêng.

### 3.4 Runbook thi hành

**Phần agent đã làm xong trong repo** (không cần chủ sở hữu):

1. ✅ `render.yaml` ở gốc, `rootDir: backend/render-license-server`, `numInstances: 1`,
   `buildFilter`, `NODE_VERSION`, `healthCheckPath: /health`, `autoDeploy: false`.
2. ✅ `backend/render.yaml` thành stub trỏ đường.
3. ✅ Đối chiếu G3: [`backend/datahub/env.production.template`](../../backend/datahub/env.production.template)
   ghi `DATAHUB_LICENSE_ASSERTION_ISSUER=autojms-license-production`,
   `_AUDIENCE=autojms-datahub-enroll-production`, `DATAHUB_CHANNEL=production` — **khớp từng ký
   tự** với blueprint. Nghĩa là A1 tự nó khép G3 cho kênh production. (Cẩn thận: default trong
   `server.js` là `autojms-license` / `autojms-datahub-enroll` **không có hậu tố** — hai giá
   trị đó **không** khớp VPS. Chúng chỉ an toàn khi blueprint được áp thật.)
4. ✅ 123 test / 10 file xanh sau khi đổi blueprint.

**Phần ⛔ chỉ chủ sở hữu làm được, trên dashboard Render** — agent không có và không được có
quyền đăng nhập, đặt secret hay bấm nút deploy:

| # | Thao tác | Ghi chú |
|---|---|---|
| A1-a | Trỏ Web Service đang chạy sang repo `Datt03-sss/AutoJMS`, nhánh `main` | Sau bước này Render sẽ thấy `render.yaml` ở gốc |
| A1-b | Apply blueprint; khi Render hỏi, điền các biến `sync: false` | 6 biến: `JWT_PRIVATE_KEY`, `JWT_PUBLIC_KEY`, `FIREBASE_SERVICE_ACCOUNT_BASE64`, `VALID_EXE_HASHES`, `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY`, **`DATAHUB_API_BASE_URL`** |
| A1-c | Upload lại secret file `googleSheetsServiceAccount.json` vào `/etc/secrets/` | Secret file **không** đi theo blueprint |
| A1-d | Xác nhận `Instances = 1` sau khi apply | Xem 3.1 |
| A1-e | **Thu hồi Supabase anon key** mà bản production cũ đang phát cho mọi client (L-3) | Bản cũ còn `DEFAULT_SUPABASE_PROJECT_REF`. Cutover **không** thu hồi giúp: key đã phát ra rồi |
| A1-f | Chốt một phiên bản `express` / `firebase-admin` | Repo ghim `^4.19.2`/`^12.7.0`; bản đang chạy dùng `^5.2.1`/`^13.8.0`. A1 sẽ **hạ** major của express — phải chủ ý, không phải tình cờ |
| A1-g | Giữ `autoDeploy: false` cho tới khi qua Chặng F | Xem ô cảnh báo thứ tự cuối Phần 3 |

**Điều kiện ra**: `GET <license-url>/health` trả 200; `POST /api/verify-license` với một
license ULTRA thật trả response **có khối `datahub`** chứa `assertion` và `apiBaseUrl`, và
`apiBaseUrl` **không** còn là `datahub.example.com`. Chưa đạt điều này thì **không sang Chặng
B** — mọi thứ dưới đây phụ thuộc vào assertion.

### 3.5 Khi nào phải lật lại quyết định

A1 hạ major `express` (4 → 5) và thay toàn bộ bề mặt `server.js` 895 → 1.550 dòng trong **một
lần deploy**, trên chính service mà máy khách đang gọi. Nếu bước A1-b phát hiện có nhiều trạm
đang chạy production thật và không chịu được một cửa sổ lỗi, thì đường lùi đúng là **A3**:
dựng service Render mới từ cùng blueprint này (khi đó **được** đặt `region: singapore` —
xem 3.3), chạy song song, cutover bằng `LICENSE_API_BASE_URL` phía client. Blueprint ở gốc
repo dùng lại được nguyên vẹn cho A3; chỉ có thao tác dashboard là khác.

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
| ~~**B0**~~ ✅ | ~~Cập nhật `BACKEND_DEPLOY_STATUS.md`: đánh dấu manifest endpoint **đã có**~~ **XONG 2026-08-26** | `backend/BACKEND_DEPLOY_STATUS.md` | Mục "Not Completed" cũ sai **cả ba** vế: route có ở `Endpoints/ManifestEndpoints.cs:34`, openapi có ở `datahub-v1.yaml:476`, và `Caddyfile` **không cần** static handler vì `/configs/*` do Kestrel phục vụ qua catch-all `reverse_proxy api:8080` (đã kiểm bằng header `Server: Kestrel` trên host thật). Thay hai gạch đầu dòng đã hết hạn bằng hai lỗ hổng **còn thật**: backup chưa từng restore (H3) và không có digest để rollback (Cổng 3) |
| ~~**B1**~~ ✅ | ~~Đưa base URI của problem type thành **cấu hình**~~ **XONG 2026-08-26** | `Infrastructure/ApiProblemDetails.cs:32` + `Configuration/DataHubRuntimeOptions.cs` + `src/AutoJMS.DataHub.Api/Program.cs:23` + `docker-compose.yml` | Hai VPS = hai domain, nên không thể là hằng số (G8). **Ba chi tiết đáng ghi**: (a) `ApiProblemWriter` để **static** chứ không DI, vì `WriteAsync` được gọi từ middleware và 24 chỗ endpoint, một số chạy trước khi có DI scope; (b) `NormalizePublicHost` **loại tên RFC 2606** (`.example.com`, `.invalid`, `.test`, `.localhost`) — thiếu chốt này thì lỗi chỉ **dời chỗ** từ hằng C# sang file config, vì cả hai template đều ship sẵn `DATAHUB_PUBLIC_HOST=datahub.example.com`; (c) **compose trước đây chỉ truyền biến này cho service `caddy`**, không cho `api` — không thêm dòng pass-through thì cả thay đổi này vô tác dụng |
| ~~**B2**~~ ✅ | ~~Sửa câu verify ở guide §10.2: bỏ `s.seats`~~ **XONG 2026-08-26** | `backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md:518` | `seats` chỉ có trong JWT, không có trong DB (G9) — nhưng câu này hỏng **nặng hơn G9 mô tả**: khoá chính của `sites` tên là `id`, không phải `site_id`, nên `s.site_id` cũng không tồn tại và **cả hai** `JOIN ... USING (site_id)` đều lỗi. Đã viết lại thành `s.id AS site_id` + `ON l.site_id = s.id`, kèm ghi chú để không ai "sửa lại" |
| ~~**B3**~~ ✅ | ~~Health check phải **Degraded/Unhealthy** khi thiếu `DATAHUB_ADMIN_TOKEN`, và phải kiểm cả 4 biến issuer/audience + `DATAHUB_TRUSTED_PROXY_NETWORKS` + `DATAHUB_MANIFEST_ROOT` có ghi được thật~~ **XONG 2026-08-26** | `Health/RuntimeConfigurationHealthCheck.cs` + `Manifests/IManifestRootProbe.cs` (mới) + `Configuration/DataHubRuntimeOptions.cs` + `src/AutoJMS.DataHub.Api/Program.cs:167` | Vá G5, S9, S10, S11 cùng lúc. **Bốn chi tiết đáng ghi**: (a) `Degraded` trước đây map sang **503**, nên "Degraded khi thiếu `DATAHUB_ADMIN_TOKEN`" nguyên văn sẽ **hạ cả host** — mà compose gate service `caddy` vào chính endpoint này, nên một lần `up` mới sẽ không bao giờ mở được site. Đã đổi `Degraded` → **200** trước; an toàn vì trước thay đổi này **không check nào** từng trả Degraded; (b) kiểm "4 biến issuer/audience có rỗng không" là **no-op**: `FromConfiguration` dùng `FirstNonEmpty(..., "autojms-license")` nên chúng không bao giờ rỗng. Câu hỏi trả lời được là **có được đặt hay không** — nay ghi lại trong `DefaultedIdentityVariables`. Production mà để mặc định ⇒ Unhealthy (G3); staging ⇒ Degraded, vì dưới test issuer cả hai đầu cùng mặc định nên vẫn khớp, siết chặt sẽ giết host staging đang chạy; (c) `ManifestRoot` **cố ý không** `CreateDirectory` — tạo bù sẽ biến một `DATAHUB_MANIFEST_ROOT` gõ sai thành thư mục trông khoẻ mà chẳng phục vụ gì; thiếu gốc ⇒ Unhealthy (mọi `/configs/*` 404 ⇒ cả fleet rơi về `SafeDefault("BASE")`), có gốc mà không ghi được ⇒ Degraded (chỉ mất đường publish); (d) tách `IManifestRootProbe` theo đúng khuôn `IDataHubDatabaseProbe` để bảng quyết định test được mà không cần chmod |
| ~~**B4**~~ ✅ | ~~Đặt **ngân sách tổng** cho fetch runtime policy~~ **XONG 2026-08-26 — ngân sách 8 s** | `src/AutoJMS/Policies/VpsRuntimePolicyService.cs` | Vá S4. **Không cần đụng `Program.cs`**: ngân sách nằm trong service (linked CTS bọc **chỉ** vòng dò mạng — cache và safe-default cố ý nằm ngoài, huỷ chúng theo đồng hồ mạng là biến sự cố mạng thành sự cố khởi động). **Vì sao 8 s chứ không phải 3–5 s như báo cáo rủi ro đề xuất**: hạ trần là **nhường** thêm ca cho fallback, mà đáy fallback là `SafeDefault("BASE")` — nó tắt FullStack, background sync, inventory sync, database tracking cho cả license ULTRA hợp lệ, **không ghi một dòng error nào**. Hạ quá tay là đổi "app treo 90 s" (dễ thấy) lấy "khách ULTRA thỉnh thoảng mất tính năng" (đắt hơn nhiều). VPS khoẻ trả manifest dưới 1 s nên 8 s vẫn đủ cho trọn 6 đường. **Chỉ hạ tiếp sau khi `SafeDefault` thôi hạ cấp tier.** Kèm theo: phải tự kiểm `IsCancellationRequested` trong vòng lặp vì `FetchStringAsync` nuốt **mọi** exception kể cả `OperationCanceledException` rồi trả `null`, nên token huỷ không nổi lên được — thiếu chốt đó thì log của "hết ngân sách" trông **y hệt** "cả 6 đường đều 404" |
| ~~**B5**~~ ✅ | ~~Enroll thất bại: retry có backoff + **hiện lỗi cho người dùng**, tách rõ "sai siteId" / "hết seat" / "VPS không phản hồi"~~ **XONG 2026-08-26** | `src/AutoJMS/Licensing/LicenseApiService.cs` (Protected — chủ sở hữu cho phép riêng lần này) | Vá S1, S8. **Retry cố ý KHÔNG áp cho mọi lỗi**: `IsRetryableEnrollStatus` chỉ nhận 5xx / 408 / 429 / lỗi transport. 401 `ASSERTION_INVALID`, 403 `CHANNEL_MISMATCH`, 404, 409 `SEAT_LIMIT_REACHED` là **quyết định**, không phải nhiễu — cùng assertion, cùng site, cùng số seat thì một giây sau vẫn ra đúng câu trả lời đó, nên retry chúng chỉ làm đường activation chậm thêm ~3 s để tới đúng lời từ chối cũ. Chi tiết đáng ghi: `catch (OperationCanceledException)` phải có `when (ct.IsCancellationRequested)`, vì timeout 30 s của `HttpClient` nổi lên dưới dạng `TaskCanceledException` — thiếu filter đó thì ca **đáng retry nhất** lại là ca duy nhất bị rethrow |
| ~~**B6**~~ ✅ | ~~Gia hạn device token thất bại: nâng mức log lên Error và báo UI~~ **XONG 2026-08-26** | `src/AutoJMS/Licensing/LicenseApiService.cs` (`RenewDataHubDeviceTokenIfNeededAsync` + `HeartbeatSupervisor.ReportDataHubRenewal`) | Vá S3. Hàm đổi `Task<bool>` → `Task<DataHubRenewOutcome>` vì `false` trước đây **gộp** "chưa đến hạn" với "gia hạn thất bại" — không phân biệt được máy đang khoẻ và máy thất bại mỗi 2 phút. Báo có **latch**: heartbeat chạy mỗi 2 phút suốt cửa sổ renew, không latch thì một sự cố thành 30 dòng Error/giờ; latch mở lại khi gia hạn thành công để sự cố tái phát vẫn được báo |
| ~~**B7**~~ ✅ | ~~`snapshot truncated=true` ⇒ báo UI, không chỉ log~~ **XONG 2026-08-26** | `src/AutoJMS/Data/DataHubClient.cs` + `src/AutoJMS/FullStack/Services/DataHubSyncService.cs` | Vá S5. **Không thể `RaiseStatus` tại chỗ phát hiện**: `RunCycleSafeAsync` kết mỗi vòng bằng `RaiseStatus("Cloud sync OK…")`, nên status dựng từ trong `PullWaybillsAsync` bị ghi đè vài millisecond sau. Truncation vì thế thành **trạng thái dính** (`DataHubChangePage.Truncated` → `_snapshotTruncated`) gộp vào câu status cuối vòng, **ưu tiên cao hơn** thông báo outbox. Kèm theo: `ReadSnapshotAsync` (đường sau `GetActiveWaybillsAsync` và danh sách tracking due) trước đây **không kiểm** `truncated` — nay kiểm cả hai đường |
| ~~**B8**~~ ✅ | ~~`TryGetSiteId()` thất bại ⇒ log **Error** kèm giá trị nhận được (che bớt), không phải Debug~~ **XONG 2026-08-26** | `src/AutoJMS/Data/DataHubClient.cs` (`TryGetSiteId` + `MaskConfigValue`) | Vá S6. **Tách hai tình huống** trước đây log giống nhau: `_siteId` **rỗng** là trạng thái bình thường của máy tier BASE và hàm này chạy ~9 lần mỗi vòng sync ⇒ giữ Debug (nâng lên Error là lấp log bằng một non-event); `_siteId` **có giá trị nhưng không parse được GUID** là config drift thật ⇒ Error, che `first2…last2`, latch một lần, latch reset trong `Configure` để giá trị mới vẫn được phán xét lại |
| ~~**B9**~~ ✅ | ~~Sinh `backend/backend-schema-dump.sql` thật từ một DB đã áp 001–005~~ **XONG 2026-08-26** | `backend/backend-schema-dump.sql` (574 dòng, 12 bảng, 6 index) | Dump lấy từ DB thật trên VPS (Postgres 16 Alpine, đã áp 001–005). Đã lược các chỉ thị `\restrict`/`\unrestrict` của `pg_dump` để file mở được bằng `psql` bản cũ hơn |
| ~~**B10**~~ ✅ | ~~Thêm `.github/workflows/verify.yml` gọi đúng các bước của `verify.ps1`~~ **XONG 2026-08-26** | `.github/workflows/verify.yml` (mới) | Không có CI là mọi gate đều là gate danh dự. Workflow **gọi** `verify.ps1` chứ không chép lại các bước, để CI và máy dev không thể lệch nhau. `windows-latest` là bắt buộc chứ không phải sở thích: `AutoJMS.slnx` có `src/AutoJMS` target `net8.0-windows/win-x64`. Cài **cả** SDK 8.0.x và 10.0.x vì app là net8.0 còn DataHub API là net10.0. ~~**Còn nợ**: `eng/harness/verify.ps1` in nhãn `Tests: PASS (or WARNING: no tests)` ngay cả khi test **đã chạy và pass**; và license server Node chưa có job `npm run check`~~ **CẢ HAI XONG 2026-08-27**: nhãn sai là do `test.ps1` **trả cùng exit 0** cho "test pass" và "không tìm thấy test project" — nay nhánh cảnh báo `exit 2` và `verify.ps1` rẽ ba chiều, nên một lần chạy pass thật không còn bị báo là chạy rỗng. Node gate thành **bước harness** `eng/harness/test-node.ps1` (`npm run check` + `npm test`) chứ **không** phải job riêng trong workflow — chính `verify.yml` phát biểu bất biến "harness là định nghĩa duy nhất của *đã verify*", nên một job chỉ chạy trên runner sẽ làm CI nghiêm hơn máy dev, đúng thứ nó tồn tại để ngăn. Workflow chỉ thêm `actions/setup-node@v4` với `node-version: 22` (khớp `node:22-alpine` của Dockerfile — test trên major khác major Render đang chạy là gate xanh cho runtime chưa từng được test) |
| ~~**B11**~~ ✅ | ~~Tách điều kiện READ khỏi điều kiện FETCH: "License DataHub hợp lệ ⇒ được ĐỌC", không phụ thuộc token JMS~~ **XONG 2026-08-26** | `src/AutoJMS/Forms/FullStackOperation.cs` (`StartLocalRuntimeAsync` mới + `RunSyncAsync`) | Mục §8 của `datahub-deployment-options.md`. Nguyên nhân gốc: `StartRealtimeRuntimeAsync` **gộp** việc ĐỌC (`LoadDataAndRefreshViewsAsync`, `StartCloudSync`) với việc FETCH (`_autoRefreshTimer`, `_leaderTierTimer`) sau **một** cổng `AuthStateService.IsAuthenticated` ⇒ máy có license ULTRA + device token hợp lệ nhưng chưa đăng nhập JMS mở form ra **grid rỗng** trong khi dữ liệu nằm sẵn trong SQLite local. Nay hai nửa có hai latch riêng (`_isLocalRuntimeStarted` / `_isRealtimeStarted`). **Cố ý giữ nguyên**: máy không có token JMS **không được** giành lease (leader không kéo được JMS thì bỏ đói cả bưu cục) — nó đi đường follower `PullAllAsync`; `LeaderTierTickAsync` và `OpportunisticWaybillRefreshAsync` vẫn gác token JMS vì chúng fetch từ JMS thật |
| ~~**B12**~~ ✅ | ~~Job night-purge phát **tombstone** (`operation='delete'`) trước khi hard-delete; retention tombstone ≥ cửa sổ offline dài nhất (đề xuất 30–90 ngày)~~ **XONG 2026-08-26** | `Infrastructure/RetentionRepository.cs` (`DeleteProjectionsAsync` mới) + `Configuration/DataHubRuntimeOptions.cs` + `Services/RetentionHostedService.cs` + `src/AutoJMS/Data/DataHubClient.cs` + `src/AutoJMS/FullStack/Services/DataHubSyncService.cs` | Vá S7. **Lệch quan trọng nhất so với đề bài: "hard-delete dữ liệu cũ" mà tombstone phải đi trước KHÔNG TỒN TẠI.** `RunOnceAsync` xoá `idempotency_records`, `dashboard_changes`, `waybill_scan_events`, `audit_logs` — **chưa bao giờ** xoá `waybill_projections`, và `003_seed_retention.sql` không seed policy nào cho bảng đó. S7 vì vậy là lỗ hổng **tiềm ẩn**, không phải đang xảy ra. Bản vá **dựng cả cơ chế** thay vì chèn tombstone vào một chỗ trống: `DeleteProjectionsAsync` phát tombstone **rồi** mới xoá projection, **trong một câu lệnh** — data-modifying CTE của PostgreSQL luôn chạy đến hết, nên không có thứ tự lỗi nào commit được cái xoá mà thiếu cái báo; và `DELETE` lấy `inserted` làm driver nên **không đường nào** xoá một hàng mà không có tombstone. **Cố ý opt-in**: không seed policy ⇒ part này không tìm thấy gì và không đổi hành vi cho đến khi operator `INSERT` một dòng — "cũ" không đồng nghĩa với "đã bị xoá", một mặc định theo tuổi sẽ bảo cả fleet bỏ đi lịch sử chúng vẫn đang dùng. **Ràng buộc cho operator trước khi bật**: projection sinh từ `waybill_scan_events`, nên đồng hồ projection **ngắn hơn** đồng hồ event sẽ để một lần re-ingest dựng lại đúng hàng vừa báo xoá. **Nửa client là bắt buộc, không phải tuỳ chọn**: `PullWaybillChangesAsync` trước đây **bỏ qua** `operation` và merge mọi change thành upsert, nên một tombstone (body chỉ có key) sẽ ghi hàng rỗng lên hàng tốt — nay tách sang `DataHubChangePage.DeletedWaybillNos` và áp `DELETE FROM fs_waybills` **bất chấp `_hasLease`**, vì tombstone do retention phát chứ không phải echo của chính máy này: leader giữ hàng lại sẽ push nó lên lần sau và **hồi sinh** waybill cho cả bưu cục. **Giá phải trả, chấp nhận có ý thức**: `dashboard_changes` chỉ prune được **tiền tố liên tục**, nên một tombstone còn sống ghim toàn bộ feed phía sau nó của site đó suốt 30–90 ngày — đổi lấy điều này vì một change thường bị mất còn cứu được bằng snapshot, còn một lần xoá thì không. **Không cần migration**: `ck_dashboard_changes_operation` đã cho `'delete'`, `retention_policies.table_name` không có CHECK allow-list (allow-list nằm trong C#) |
| ~~**B13**~~ ✅ | ~~Migration `006`: bảng `jti_cache`/revocation + index `dashboard_changes(site_id, change_at)` + index `waybill_scan_events(site_id, event_occurred_at)` + index phục vụ retention `audit_logs`~~ **XONG 2026-08-26 — chủ sở hữu đã cho phép** | `backend/datahub/migrations/006_revocation_and_retention_indexes.sql` (mới) | **Bảng tên `revoked_device_credentials`, KHÔNG phải `jti_cache`** — đây là lệch có chủ ý: device token của DataHub **không có jti** để cache. `HmacDeviceTokenService` ký `{deviceId, siteId, channel, role, tokenVersion, expiresAt, issuer, audience}` và không gì khác, nên cột `jti` sẽ **không đường code nào điền được** — một bảng trông như chống replay mà về cấu trúc không thể chống. `jtiCache` trong kế hoạch thuộc **license server Node** (`server.js:293`), tiến trình khác, format token khác. Thứ DataHub thật sự thu hồi được là `credential_hash` mà `DeviceAuthenticationMiddleware` đã tính mỗi request. **Ba index**: `dashboard_changes(site_id, change_at, change_seq)` (cột thứ ba để aggregate của `DeleteChangesAsync` trả index-only); `waybill_scan_events(site_id, event_occurred_at)` (index cũ dẫn đầu bằng `waybill_no` nên vô dụng với vị từ không nêu waybill); `audit_logs(at, id)` (retention quét **liên site**, `ix_audit_logs_site_at` dẫn đầu `site_id` không dùng được). **Chạy trong transaction, KHÔNG `CONCURRENTLY`** — chấp nhận được **chỉ vì** ba bảng hiện rỗng-đến-nhỏ; áp lại lên site có lịch sử thật phải chuyển sang dạng `_notx`. **Chỉ là schema**: chưa đường code nào đọc bảng này, `TouchActiveAsync` thêm `AND NOT EXISTS` ở một thay đổi sau |
| ~~**B16**~~ ❌ | ~~Thêm sub-check schema cho `/health/ready`~~ **VIỆC KHÔNG TỒN TẠI** | — | `PostgresDataSource.CanConnectAsync` đã làm đúng việc này từ trước (xem G12). Giữ dòng này để không ai "vá" lại lần nữa |
| ~~**B17**~~ ✅ | ~~Ép server-side `DeviceIdentity.Role` (hiện enroll ghi `'operator'` nhưng **không endpoint nào kiểm**)~~ **XONG 2026-08-26** | `Auth/AuthContracts.cs` (`DeviceRoles`, `DeviceCapability`, `DeviceRolePolicy`, `Evaluate` 4 tham số) + `Auth/DeviceAuthenticationMiddleware.cs` + `SyncEndpoints.cs` / `LeaseEndpoints.cs` / `IngestEndpoints.cs` | **Ràng buộc quyết định hình dạng bản vá**: `EnrollmentEndpoints.AllowedEnrollmentRoles = { "operator" }`, nên **cả fleet** là `operator` — gác quyền ghi vào `leader`/`admin` sẽ **khoá sạch mọi thiết bị**. Luật ép được là **allow-list đóng**: role không nằm trong `DeviceRoles.All` ⇒ 403, kiểm trong `DeviceAuthenticationMiddleware` vì `SiteHub.OnConnectedAsync` không có route handler để gác. Thứ tự cố ý: sai site báo trước sai role, để log không mách nước rằng enrollment của chính máy đó hỏng |
| ~~**B14**~~ ✅ | ~~`statement_timeout` cho `IngestPipeline`, `LeaseRepository`, `EnrollmentRepository`, `DeviceRepository`~~ **XONG 2026-08-26** | `Configuration/DataHubRuntimeOptions.cs` + `Infrastructure/PostgresDataSource.cs` (`BuildConnectionString`) + `Infrastructure/IngestRepository.cs` | Một query treo ở lease là fencing sai. **Phát hiện đáng ghi: `SET LOCAL statement_timeout = '60s'` của `IngestRepository` chưa bao giờ nổ được** — `CommandTimeout` mặc định của Npgsql 8.0.6 là 30 s, nên client bỏ đi trước server 30 s. Deadline nay đặt bằng **startup option** của PostgreSQL (`Options=-c statement_timeout=…`) để sống qua `DISCARD ALL` khi connection trả về pool, áp cho **mọi** repository chứ không riêng ingest; `Options` do operator cấu hình được **nối sau cùng** (PostgreSQL áp `-c` theo thứ tự, cái sau thắng) để override không cần sửa code; `CommandTimeout = seconds + 5` để **server** huỷ trước (57014, backend chết và nhả lock) chứ không phải Npgsql bỏ đi khi query vẫn đang chạy. Giá trị 0 bị clamp về Minimum — đó là giá trị duy nhất PostgreSQL đọc thành "không deadline", tức biến chốt an toàn thành phản diện của nó |
| ~~**B15**~~ ✅ | ~~License server: graceful shutdown (`server.close()` khi SIGTERM/SIGINT) + structured logging + `/api/version`~~ **XONG 2026-08-26** | `backend/render-license-server/server.js` + `test/version.test.js` + `test/shutdown.test.js` (mới) + `test/helpers/harness.js` | **Handler SIGTERM đã có từ trước** — nên việc thật không phải "thêm graceful shutdown" mà là **vá bốn lỗ trong cái đã có**: (a) không có SIGINT, nên `node server.js` bị Ctrl+C vẫn chết giữa lúc ghi session; (b) không có timeout ép thoát — `server.close()` chờ **mọi** connection, và một keep-alive socket rỗi cũng tính là connection, nên callback có thể không bao giờ nổ và Render SIGKILL lúc 30 s (nay có `closeIdleConnections()` + hạn ép thoát 10 s, `unref()` để bộ đếm không tự giữ event loop sống); (c) không idempotent — signal thứ hai gọi `close()` trên server đang đóng ⇒ `ERR_SERVER_NOT_RUNNING` ⇒ **báo một lần deploy sạch thành thất bại**; (d) exit code cứng 0, nên platform không phân biệt được đóng sạch với ép thoát. `SHUTDOWN_TIMEOUT_MS=0` bị clamp về 1000 — đúng lý do như `statement_timeout=0` ở B14. `createShutdownHandler` **export ra** để test được: chuỗi này là hành vi mức process, gửi signal thật cho test runner sẽ giết cả lần chạy. `/api/version` **và** `/health/version` cùng payload (monitor đã scope `/health/*` không cần allow-list mới), anonymous + `healthLimiter` vì smoke test chạy **trước khi** có token — đánh đổi có ý thức: repo public nên commit hash live là thông tin công khai; gác sau admin token là một dòng nếu chủ sở hữu muốn. Logging đã chuyển toàn bộ đường license-verification và session-lifecycle sang `logEvent` JSON một dòng (Render biến field JSON thành filter được, template string thì chỉ grep được), `sessionId` ghi đầy đủ **có chủ ý** vì nó là khoá join giữa `session.created` / `heartbeat` / `session.closed` và bản thân không phải credential. **Kết quả: `npm test` 123 → 139 pass, 0 fail; `npm run check` pass** |

> ✅ **`CREATE INDEX CONCURRENTLY` trong B13 — đã mở đường (2026-08-26).** Cách đúng để thêm
> index vào bảng đang chạy production là `CONCURRENTLY` (không lock bảng), nhưng `CONCURRENTLY`
> **không chạy được trong transaction block**, còn `apply-migrations.{ps1,sh}` áp **mọi** file
> bằng `--single-transaction` — nên một migration `006` viết bằng `CONCURRENTLY` sẽ thất bại với
> *"CREATE INDEX CONCURRENTLY cannot run inside a transaction block"*.
>
> **Đã thi hành đường số 2**: cả hai runner nay nhận hai cách khai báo opt-out —
> tên file kết thúc `_notx` (ví dụ `006_dashboard_changes_time_index_notx.sql`), **hoặc** một
> dòng `-- no-transaction` trong file. Hậu tố thấy được khi `ls`; dòng marker thấy được khi
> review. Hai runner dùng **cùng một quy tắc**, đã kiểm chéo bằng fixture để một file không thể
> nguyên tử ở host này mà không nguyên tử ở host kia.
>
> **Cái giá — đọc trước khi dùng.** Bỏ transaction là bỏ tính nguyên tử: hỏng giữa chừng thì các
> câu lệnh trước đó **đã áp**, còn version marker **chưa ghi**, nên runner ném lỗi và lần chạy
> sau bắt đầu lại từ đầu file. File `_notx` vì vậy **bắt buộc** idempotent theo từng câu lệnh
> (`IF NOT EXISTS` ở mọi đối tượng). Có một bẫy mà riêng idempotent **không** cứu được: một
> `CREATE INDEX CONCURRENTLY` thất bại để lại **index INVALID**, và `IF NOT EXISTS` sau đó thấy
> tên đã tồn tại nên **bỏ qua vĩnh viễn**. Gỡ bằng tay trước khi chạy lại:
> `SELECT indexrelid::regclass FROM pg_index WHERE NOT indisvalid;` rồi `DROP INDEX CONCURRENTLY <tên>;`
>
> **Vẫn nên cân nhắc đường số 3** (index thường, chấp nhận lock) nếu B13 làm **trước khi có dữ
> liệu thật**: hai VPS đều mới, bảng còn nhỏ, lock vài chục mili-giây là không đáng kể — và như
> vậy giữ được tính nguyên tử. Đường số 2 tồn tại cho lần thêm index **thứ hai**, khi bảng đã
> lớn. Đó cũng là lý do nên làm B13 **sớm**.
>
> Ngoài ra, trong lúc sửa runner phát hiện một lỗi độc lập: `Invoke-Psql` trong
> `apply-migrations.ps1` **nhận tham số `$InputFile` rồi bỏ qua nó** ở nhánh không dùng compose,
> nên chế độ `-DatabaseUrl` giao cho psql **không file, không `--command`** — psql rơi về đọc
> stdin. Nhánh host-psql do đó **chưa bao giờ áp được một migration nào**; chỉ nhánh container
> từng chạy thật. Đã vá bằng `--file`.

**Thứ tự khuyến nghị** — **hàng đợi Chặng B nay rỗng** (2026-08-26). B0…B17 đã xong hoặc đã
được ghi nhận là việc không tồn tại (B16). Không còn hạng mục nào chờ.

**Điều kiện ra**: `dotnet build AutoJMS.slnx -c Release` 0 error, `verify.ps1` **ALL GATES
PASSED**, và B13 đã được cho phép hoặc đã được ghi nhận là hoãn có ý thức. — **Đã đạt cả ba**
(build 0 warning/0 error, verify ALL GATES PASSED, B13 đã được chủ sở hữu cho phép và đã áp
trên VPS).

> ✅ **Hai việc còn nợ do chính Chặng B sinh ra — đã đóng cả hai (26/08/2026).**
>
> 1. **`backend/backend-schema-dump.sql` đã cũ** (dump ở B9 lấy từ DB áp 001–005, thiếu
>    `revoked_device_credentials` và 4 index của `006`, trong khi header của chính file yêu cầu
>    cập nhật dòng "Reflects" mỗi khi có migration mới). → **Đã sinh lại cho 001..006** ở commit
>    `7f4497f`.
> 2. **SQL mới của B12 chưa từng chạy trên PostgreSQL thật** — rủi ro nghiêm trọng vì một lỗi cú
>    pháp ở đây **không** làm sập API: `RetentionHostedService` bắt mọi exception và log
>    `LogWarning`, nên hậu quả là **retention âm thầm ngừng chạy**. → **Đã đo
>    `EXPLAIN (ANALYZE, BUFFERS)` cả 3 câu trên container PostgreSQL 16 Alpine của staging**:
>
>    | Câu lệnh | Buffers | Thời gian |
>    |---|---|---|
>    | Fast-exit probe (`retention_policies`) | `shared hit=1` | 0.062 ms — 0 hàng, **không chạm** `waybill_projections` |
>    | `candidatesSql` (`DeleteProjectionsAsync`) | `shared hit=15` | 0.355 ms |
>    | Bộ lọc `CASE` tombstone (`DeleteChangesAsync`) | `shared hit=3` | 0.233 ms |
>
>    Số liệu và bối cảnh hạ tầng: [backend/vps/VPS_STATUS_REPORT.md](../../backend/vps/VPS_STATUS_REPORT.md)
>    (bản đã che — repo này PUBLIC; xem [rule 09](../../.agent/rules/09-cross-agent-collaboration.md)).

### Bật xoá projection của B12 (tombstone) — công thức cho operator

Cơ chế tombstone đã có trong code nhưng **ngủ**: `003_seed_retention.sql` không seed policy nào
cho `waybill_projections`, nên `DeleteProjectionsAsync` không tìm thấy hàng nào và không đổi hành
vi. Nó chỉ chạy khi operator tự `INSERT` một dòng policy:

```sql
-- Một site cụ thể (khuyến nghị: bật thử một site trước)
INSERT INTO retention_policies (site_id, table_name, clock_column, delete_after)
VALUES ('<site-uuid>', 'waybill_projections', 'updated_at', interval '180 days')
ON CONFLICT (site_id, table_name) WHERE site_id IS NOT NULL
DO UPDATE SET delete_after = EXCLUDED.delete_after;

-- Hoặc toàn hệ thống (site_id NULL = policy mặc định)
INSERT INTO retention_policies (site_id, table_name, clock_column, delete_after)
VALUES (NULL, 'waybill_projections', 'updated_at', interval '180 days')
ON CONFLICT (table_name) WHERE site_id IS NULL
DO UPDATE SET delete_after = EXCLUDED.delete_after;
```

Hai chi tiết dễ sai trong chính hai câu trên: `clock_column` là `NOT NULL` nên **bắt buộc** phải
điền (bỏ trống ⇒ lỗi NOT NULL), nhưng `DeleteProjectionsAsync` **không đọc** nó — cột đồng hồ của
projection **hardcode** là `updated_at` trong C# (tên bảng/cột không bao giờ được nội suy từ dữ
liệu policy, đó là allow-list chống injection), nên điền `'updated_at'` để dữ liệu khớp hành vi
thật chứ không phải để code dùng. Và hai unique index của bảng là **partial**
(`WHERE site_id IS NULL` / `WHERE site_id IS NOT NULL`), nên `ON CONFLICT` **phải** kèm đúng
predicate đó, ngược lại PostgreSQL không suy ra được arbiter index và câu lệnh thất bại.

**Ba ràng buộc phải kiểm trước khi INSERT — vi phạm cái nào cũng làm mất dữ liệu âm thầm:**

1. **`delete_after` của `waybill_projections` KHÔNG được ngắn hơn của `waybill_scan_events`**
   (mặc định seed là 60 ngày). Projection sinh **từ** scan event: nếu event còn sống mà projection
   đã bị xoá, một lần re-ingest sẽ dựng lại đúng hàng vừa báo là đã xoá — client nhận tombstone
   rồi nhận lại upsert, và không log nào nói rằng nó vừa xoá oan.
2. **`delete_after` phải dài hơn cửa sổ offline dài nhất trừ đi `DATAHUB_TOMBSTONE_RETENTION_DAYS`
   (mặc định 90).** Máy offline lâu hơn cửa sổ tombstone quay lại sẽ không thấy thông báo xoá và
   giữ hàng trong SQLite mãi mãi — đúng cái bug mà tombstone tồn tại để vá.
3. **Site phải có dòng trong `site_change_counters`.** Không có counter thì không cấp được
   `change_seq`, và code **cố ý giữ nguyên projection** trong trường hợp đó thay vì xoá không kèm
   thông báo.

Sau khi bật, theo dõi log `DataHub retention removed … {Projections} projections ({Tombstones}
tombstones published)`: **`Tombstones` nhỏ hơn `Projections`** là tín hiệu có site mất hàng mà
không thông báo được — dừng lại và điều tra, đừng chờ ca sau.

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

**Chỉ template đọc, .NET không đọc**: `TLS_CONTACT_EMAIL` (ACME), `DATAHUB_API_IMAGE`
(Compose), `POSTGRES_DB`/`_USER`/`_PASSWORD` (API **chỉ** thấy `ConnectionStrings__DataHub`).

> ⚠️ **`DATAHUB_PUBLIC_HOST` đã rời khỏi danh sách trên từ 2026-08-26.** Trước đây nó chỉ đến
> service `caddy`; nay compose truyền **cả** cho service `api`, và .NET đọc nó qua
> `DataHubRuntimeOptions.PublicHost` để dựng `type` URI của response lỗi (B1). Hệ quả thực tế:
> đổi giá trị này nay **cần restart service `api`**, không chỉ reload Caddy. Nó vẫn **không**
> tham gia bất kỳ quyết định routing hay tin cậy nào — chỉ là chuỗi hiển thị. Bỏ trống hoàn
> toàn hợp lệ: API phát URI tương đối `/problems/...`.

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
| **H3** ⏳ | **Diễn tập restore thật** (§12.3) — hard gate Phase 7 của checklist | **Antigravity báo cáo 2026-08-26 đã chạy trọn vòng** `backup-postgres.ps1` → `restore-postgres.ps1` → `apply-migrations.ps1` → `smoke-test.sh` (24/24 PASS) trên VPS. Chưa gạch bỏ dòng này vì **trong repo không có bằng chứng nào của lần diễn tập đó** — không log, không số đo, không tick trong `DEPLOY_EXECUTION_CHECKLIST.vi.md`. Gate chỉ tự chứng minh được khi H4 xong. Diễn tập vào một DB/VPS **rác**, không vào staging đang chạy |
| **H4** | Ghi lại **RPO/RTO thực đo** từ H3 vào `BACKEND_DEPLOY_STATUS.md` | Số đo thật là thứ duy nhất dùng được khi có sự cố — và hiện là thứ **duy nhất** biến báo cáo diễn tập ở H3 thành bằng chứng kiểm được |

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
B  Vá code trên máy dev (B0…B17)           agent               ✅ XONG 2026-08-26
│  └─ ra: build 0 error + verify.ps1 ALL GATES PASSED — đã đạt; B13 đã được phép và đã áp
│     (hai món nợ đã đóng: dump sinh lại cho 001..006 ở 7f4497f; 3 câu SQL của B12 đã EXPLAIN
│      trên PostgreSQL staging — không còn hạng mục nào chờ)
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
| ~~1~~ ✅ | ~~**L-1**: chọn A1/A2/A3~~ **ĐÃ CHỐT A1 ngày 2026-08-26.** Còn lại là **thi hành**: 7 thao tác dashboard A1-a…A1-g (mục 3.4) | A — chặn tất cả |
| 2 | Sinh 2 cặp khoá RSA assertion (staging + production), đặt private ở Render, public ở VPS | D |
| 3 | **Thu hồi Supabase anon key** đang bị bản production phát cho mọi client (L-3) | song song, càng sớm càng tốt |
| 4 | Ghim Render về **1 instance** (JTI cache + rate limiter đều in-memory) | A |
| 5 | Mua 2 VPS + tạo 2 A record | C, J |
| 6 | Xử lý box `dev.jmsauto.online`: dùng lại làm staging hay cho nghỉ | C0 |
| ~~7~~ ✅ | ~~**Cho phép sửa migration** (B13: `jti_cache` + 3 index) — migration là Protected File~~ **ĐÃ CHO PHÉP 2026-08-26**, đã tạo `006_revocation_and_retention_indexes.sql`. Còn lại là **Antigravity áp trên VPS** | — |
| 8 ⏳ | Cho phép sửa `LicenseApiService.cs` (Protected) để trạm cảnh báo **trước** khi license hết hạn (C1/C6), và để `heartbeat` 5xx không còn là lỗi chí tử (`heartbeat-5xx-fatal`) | song song — **cho phép 2026-08-26 chỉ giới hạn trong B5/B6** (enroll retry + báo gia hạn thất bại). C1/C6 và `heartbeat-5xx-fatal` **vẫn cần cho phép riêng**; lỗi `_fatalRetryCount = 0` đặt **trước** dòng log cũng còn nằm trong đó |
| ~~9~~ ✅ | ~~Cho phép sửa `Program.cs` cho B4~~ **KHÔNG CẦN NỮA** — ngân sách 8 s đặt trọn trong `VpsRuntimePolicyService.cs` (không Protected). `Program.cs:356-359` giữ nguyên | — |
| 9b | **Sửa `SafeDefault` để nó thôi hạ cấp tier** (`VpsRuntimePolicyService.cs:86` + `RuntimePolicyDocument.cs:88-98`): hiện một license ULTRA hợp lệ rơi vào fallback sẽ chạy như BASE, **không một dòng error**. Nay cấp thiết hơn vì nó **chặn việc hạ tiếp ngân sách fetch xuống 3–5 s** (xem B4) | S2, B4 giai đoạn 2 |
| 9c | **Region của Render**: service hiện ở Oregon, Firebase RTDB ở `asia-southeast1` — mỗi `verify-license` trả giá RTT xuyên Thái Bình Dương vài lần. **Region không đổi được sau khi tạo service**, nên muốn sửa phải dựng service mới (= A3). Quyết định riêng, độc lập với A1 (mục 3.3) | — |
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
| `DATAHUB_LICENSE_ASSERTION_ISSUER` | ✖ | blueprint gốc đặt **inline** `autojms-license-production`; default trong code là `autojms-license` — **không khớp VPS** |
| `DATAHUB_LICENSE_ASSERTION_AUDIENCE` | ✖ | blueprint gốc đặt **inline** `autojms-datahub-enroll-production`; default trong code là `autojms-datahub-enroll` — **không khớp VPS** |
| `DATAHUB_API_BASE_URL` | ✖ | **`sync: false`** trong blueprint gốc ⇒ Render hỏi khi apply và **thôi ghi đè** giá trị dashboard. Trước đây blueprint ghi đè bằng placeholder `https://datahub.example.com` ở **mỗi** lần sync. Code vẫn **trả cho client không báo lỗi nếu sai** (3.2) |
| `NODE_VERSION` | ✖ | `"22"` — ghim trong blueprint; `package.json` chỉ đòi `>=20` |
| `DATAHUB_CHANNEL` | ✖ | `production` / `staging` |
| `LICENSE_BILLING_ANCHOR_DAY` | ✖ | `16` |
| `LICENSE_GRACE_DAYS` | ✖ | `7` |
| `VALID_EXE_HASHES` | ✖ | đang rỗng (J-3) |
| `FIREBASE_OPERATION_TIMEOUT_MS` | ✖ | |

Rate limiter hiện có: `limiter` 20/phút (verify-license, logout), `heartbeatLimiter` 120/phút,
`googleSheetsGrantLimiter` 60/phút, `datahubAssertionLimiter` 60/phút, `healthLimiter` 30/phút.
`jtiCache = NodeCache({ stdTTL: 3600 })` (`server.js:290`). Tất cả **in-memory** ⇒ **1 instance**,
đã ghim bằng `numInstances: 1` trong blueprint.

`healthCheckPath` của Render trỏ `/health` (`server.js:573`) — **không** phụ thuộc dependency và
**không** nằm sau limiter nào. Đừng đổi sang `/health/firebase`: đường đó nằm sau
`healthLimiter` 30/phút, để nền tảng poll vào đó là tự ăn budget của chính mình.

Secret file `googleSheetsServiceAccount.json` (`/etc/secrets/`) **không** đi theo blueprint —
phải upload lại bằng tay sau khi đổi repo (bước A1-c).

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
| `DATAHUB_PUBLIC_HOST` | ✖ | **Caddy + .NET** (từ 2026-08-26) — .NET dùng nó **chỉ** để dựng `type` URI của response lỗi. Đổi giá trị ⇒ phải restart service `api`, không chỉ reload Caddy. Bỏ trống hoặc để tên RFC 2606 ⇒ URI tương đối, hợp lệ (D4) |
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
8. **Chốt L-1 không phải là thi hành L-1.** Chừng nào 7 thao tác dashboard A1-a…A1-g (mục 3.4)
   chưa chạy xong, mọi kết luận **"đã vá"** — trong
   [`FULLSTACK_BACKEND_RISK_REVIEW.md`](FULLSTACK_BACKEND_RISK_REVIEW.md), trong tài liệu này,
   hay trong bất kỳ báo cáo nào — vẫn chỉ đúng với **repo**, **không** đúng với **production**.
   Render production vẫn đang chạy repo `AutoJMS-API` cho tới lúc bước A1-a thực sự được bấm.
   Mọi báo cáo trạng thái license server phải nói rõ đang nói về repo hay về production.

   Hệ quả cụ thể của quy tắc này ở bản 2026-08-26: `render.yaml` ở gốc repo là **điều kiện cần**
   để A1 thi hành được, **không** phải bằng chứng A1 đã thi hành.

---

## Phần 18 — Sáu gói tối ưu từ báo cáo rủi ro: đã kiểm chứng lại từng cái

Báo cáo `backend_vps_deploy_risk_and_optimization_report.md` (do Antigravity soạn, vai trò
Advisor) đề xuất 6 gói. **Mọi khẳng định trong đó đã được đọc lại từ file thật trước khi hành
động** — quy tắc này tồn tại vì gói S4 dưới đây chỉ đúng một nửa, và nửa sai lại là nửa quyết
định ai được phép vá.

| Gói | Khẳng định của báo cáo | Kiểm chứng | Xử lý |
|---|---|---|---|
| **Opt-1** | Thiếu script deploy tổng ⇒ viết `deploy-datahub.ps1` | ✅ đúng | **Hoãn có ý thức** — xem dưới |
| **Opt-2** | Runner ép `--single-transaction` nên `CONCURRENTLY` không viết được (`apply-migrations.ps1:111`, `.sh:67`) | ✅ **đúng từng dòng** | ✅ **Đã làm** — `*_notx.sql` + `-- no-transaction` |
| **Opt-3** | `ApiProblemDetails.cs:32` hardcode `datahub.example.com` | ✅ đúng (kế hoạch cũ ghi `:33`, lệch 1 dòng) | ✅ **Đã làm** (B1) |
| **Opt-4** | Không có backup theo lịch ⇒ systemd timer + đồng bộ rclone ra ngoài | ✅ đúng | **Hoãn có ý thức** |
| **Opt-5** | Không có `rollback-stack.ps1` | ✅ đúng | **Hoãn có ý thức** |
| **Opt-6** | `DATAHUB_TRUSTED_PROXY_NETWORKS` mặc định quá rộng | ✅ đúng (đã là S10/D3) | **Giá trị vận hành** — chủ sở hữu đặt theo từng host |

### 18.1 Một khẳng định sai, và vì sao nó quan trọng

Báo cáo viết `VpsRuntimePolicyService` "duyệt tuần tự 6 đường link bằng
`.GetAwaiter().GetResult()`". Đọc file: service **hoàn toàn `async`/`await`**; lời gọi chặn nằm
ở [`src/AutoJMS/Program.cs:356-359`](../../src/AutoJMS/Program.cs). **Số học 6 × 15 = 90 giây
thì đúng**, chỉ vị trí là sai.

Sai lệch này không vô hại: nếu tin theo báo cáo, kết luận sẽ là "phải xin phép sửa Protected
File `Program.cs`" và mục B4 tiếp tục nằm chờ. Đọc file thật cho ra kết luận ngược — ngân sách
đặt được **trọn vẹn** trong `VpsRuntimePolicyService.cs` (không Protected), nên B4 vá được ngay.
**Một khoảng trống đã tự mở ra chỉ nhờ việc kiểm lại nguồn.**

### 18.2 Vì sao Opt-1, Opt-4, Opt-5 hoãn chứ không làm

Cả ba đều là script **vận hành trên VPS**, mà theo quyết định số 2 của Phần 0 thì **VPS chưa tồn
tại**. Viết bây giờ là sinh ra mã không ai chạy được, không ai test được, và — nguy hiểm hơn —
mã **trông như đã sẵn sàng**. Một `rollback-stack.ps1` chưa từng chạy thật còn tệ hơn không có
script rollback, vì nó được tin vào đúng lúc căng thẳng nhất.

Riêng **Opt-5 còn thiếu tiền đề kỹ thuật**: rollback cần biết digest **trước đó** là gì, nhưng
`start-stack.ps1:43` chỉ **in** digest ra console, không ghi xuống file nào. Không có
`deployed-digests.log` thì `rollback-stack.ps1` không có gì để lùi về. Thứ tự đúng là: (a)
`start-stack.ps1` ghi lại digest mỗi lần deploy → (b) mới viết script rollback. Làm ngược lại
là viết vỏ rỗng.

**Điều kiện để mở khoá cả ba**: VPS staging đã lên và đã qua Chặng E. Khi đó chúng thuộc Chặng H
(Opt-4) và Chặng 13/Rollback (Opt-1, Opt-5), và **chạy thật được ngay trong lần viết đầu tiên** —
đó mới là lúc chúng có giá trị.

### 18.3 Ba việc phát sinh trong lúc kiểm chứng, không có trong báo cáo

1. **Blueprint chưa bao giờ được Render đọc** (`backend/render.yaml` không nằm ở gốc) — mục 3.0.
   Đây là phát hiện đắt nhất của vòng này: nó biến A1 từ "một chỉ thị" thành "một chỉ thị thi
   hành được".
2. **`DATAHUB_API_BASE_URL` bị blueprint ghi đè lại mỗi lần sync**, chứ không chỉ "chưa điền" —
   mục 3.2. G10 vì thế là lỗi **tái phát**, không phải lỗi một lần.
3. **`Invoke-Psql` bỏ qua `$InputFile` ở nhánh `-DatabaseUrl`** nên chế độ host-psql của
   `apply-migrations.ps1` chưa từng áp được migration nào — xem ô cảnh báo ở B13. Lỗi này chỉ lộ
   ra vì phải đọc kỹ hàm đó để thêm `_notx`.
