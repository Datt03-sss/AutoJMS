# Manual Setup Backend Để AutoJMS Chạy Được

Ngày cập nhật: 2026-08-23

Manual này dùng cho backend hiện tại trong `D:\v1.2605.2(new-test)\backend`.

Mục tiêu cuối cùng:

- Render license server chạy được `/health`, `/api/verify-license`, `/api/heartbeat`.
- Firebase có license test để app đăng nhập.
- DataHub API trên VPS chạy được: `/health/ready`, `/api/v1/devices/enroll`, và các route `/api/v1/sites/...`.
- AutoJMS build được và đăng nhập bằng license test.

Không ghi secret thật vào repo. Không commit `.env`, Firebase service account, `DATAHUB_ADMIN_TOKEN`, `DATAHUB_DEVICE_TOKEN_SIGNING_KEY`, `DATAHUB_ENROLLMENT_PEPPER`, JWT private key, hoặc token production.

## 1. Kiến Trúc Backend

```text
AutoJMS.exe
  -> Render server /api/verify-license
      -> Firebase Realtime Database: Licenses, sessions
      -> trả về JWT + tier + apiBaseUrl + licenseAssertion
  -> DataHub API POST /api/v1/devices/enroll (đổi assertion lấy device token)
  -> DataHub API manifest/config/hash/tier/selector-update JSON
  -> DataHub API /api/v1/sites/{siteId}/... : ingest, changes, snapshot, lease
  -> DataHub hub WS /hubs/site : doorbell realtime
  -> Render server /api/heartbeat
      -> Firebase sessions
      -> gia hạn device token trước hạn
```

Vai trò từng dịch vụ:

| Dịch vụ | Vai trò |
|---|---|
| Firebase | License, HWID, session, tier, middleCode |
| Render | API verify license, heartbeat, logout, cấp JWT, ký license assertion |
| DataHub API (VPS) | Enroll thiết bị, ingest JMS, change feed, lease, SignalR hub, manifest JSON |
| DataHub PostgreSQL | 12 bảng vận hành, chỉ nghe trên private Docker network |
| GitHub Releases | Velopack binaries, không dùng DataHub để chứa `.nupkg` |

Không có BaaS nào phía sau DataHub: không project ref, không storage bucket, không PostgREST,
không RPC gọi từ client, không RLS. Mọi lời gọi đi qua một endpoint tường minh trong
`src/AutoJMS.DataHub.Api` và được xác thực bằng device token.

## 2. Thông Tin Backend Hiện Tại

```text
DataHub public host:
https://dev.jmsauto.online

Render production URL:
https://autojms-api.onrender.com

Firebase RTDB:
https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/

VPS: Ubuntu 24.04, Docker Compose, container `caddy` + `api` + `postgres`
Thư mục triển khai: /opt/autojms-datahub
```

Source chính:

```text
backend/render-license-server/server.js
backend/render-license-server/package.json
backend/render-license-server/env.template
backend/render.yaml
backend/datahub/docker-compose.yml
backend/datahub/Caddyfile
backend/datahub/migrations/
backend/datahub/scripts/
backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md
src/AutoJMS.DataHub.Api/
backend/BACKEND_DEPLOY_STATUS.md
```

## 3. Công Cụ Cần Có

Kiểm tra trên máy deploy/dev:

```powershell
node -v
npm -v
dotnet --info
ssh -V
git --version
```

Trên VPS:

```bash
docker --version
docker compose version
```

Yêu cầu:

| Tool | Mục đích |
|---|---|
| Node.js >= 20 | Chạy Render license server |
| npm | Cài dependency backend |
| .NET SDK 10 (workload Windows) | Build AutoJMS và `AutoJMS.DataHub.Api` |
| SSH tới VPS | Chạy `bin/dc.sh`, `bin/apply-migrations.sh` |
| Docker + Docker Compose trên VPS | Chạy stack `caddy`/`api`/`postgres` |
| Render dashboard | Deploy license server |
| Firebase console access | Tạo service account và license test |

Không có CLI riêng cho DataHub. Migration và vận hành đều qua `docker compose`, bọc bởi các
script trong `backend/datahub/scripts/`.

## 4. Secret Cần Chuẩn Bị

Không đặt các giá trị này vào tài liệu hoặc git.

| Secret | Lấy ở đâu | Dùng cho |
|---|---|---|
| `JWT_PRIVATE_KEY` | Tự tạo RSA private key | Render ký license JWT |
| `JWT_PUBLIC_KEY` | Từ RSA private key | Render verify/chuẩn public key |
| `FIREBASE_SERVICE_ACCOUNT_JSON` | Firebase Console | Render Admin SDK đọc/ghi RTDB |
| `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` | Tự tạo RSA private key | Render ký license assertion cho enroll |
| `DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY` | Từ private key trên | VPS verify assertion |
| `DATAHUB_DEVICE_TOKEN_SIGNING_KEY` | Tự sinh, 32+ byte random | VPS ký device token (HMAC) |
| `DATAHUB_ENROLLMENT_PEPPER` | Tự sinh, 32+ byte random | VPS băm device secret khi enroll |
| `DATAHUB_ADMIN_TOKEN` | Tự sinh, chỉ đặt trên VPS | Route admin server-side, không bao giờ trả về client |
| `POSTGRES_PASSWORD` | Tự sinh | Container `postgres`, chỉ trong `.env.production` trên VPS |

Render **không** giữ device token. Render giữ khoá ký assertion và phát assertion ngắn hạn cho
từng lần kích hoạt; máy trạm tự đổi assertion đó lấy device token của riêng nó.

Nếu cần tạo JWT key pair bằng OpenSSL:

```powershell
openssl genrsa -out jwt_private.pem 2048
openssl rsa -in jwt_private.pem -pubout -out jwt_public.pem
```

Khi nhập vào Render env, có thể dán nguyên PEM nhiều dòng hoặc dùng dạng escaped `\n`. `server.js` đã normalize `\n`.

## 5. Setup DataHub

### 5.1. Đưa Stack Lên VPS

Lần đầu chạy `backend/datahub/deploy/bootstrap-vps.sh`; chi tiết trong
`backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md`. Sau đó mọi thao tác đều từ
`/opt/autojms-datahub`:

```bash
cd /opt/autojms-datahub
./bin/dc.sh --env-file .env.production ps
./bin/dc.sh --env-file .env.production logs --tail 50 api
```

`--env-file` bắt buộc. `docker-compose.yml` cố tình không có khoá `env_file:`, thiếu nó thì mọi
`${VAR:?}` sẽ fail ngay — dùng `bin/dc.sh` để không bao giờ quên.

Kiểm tra stack sống:

```bash
curl -fsS https://dev.jmsauto.online/health/live
curl -fsS https://dev.jmsauto.online/health/ready
```

### 5.2. Apply Migration Nếu Chưa Có

```bash
cd /opt/autojms-datahub
./bin/apply-migrations.sh --env-file .env.production
```

Migration là forward-only, chạy theo thứ tự tên file:

```text
backend/datahub/migrations/001_core.sql
backend/datahub/migrations/002_seed_policies.sql
backend/datahub/migrations/003_seed_retention.sql
backend/datahub/migrations/004_projection_slot_payloads.sql
backend/datahub/migrations/005_change_retention_floor.sql
```

Mỗi file tự ghi marker của nó vào `schema_migrations` bên trong transaction của chính nó, nên
một lần chạy dở dang không thể tự nhận là đã xong. Không có đường rollback: sai thì thêm file
mới đánh số tiếp, tuyệt đối không sửa file đã apply.

Schema kỳ vọng — 12 bảng, đúng 1 hàm SQL:

| Loại | Tên |
|---|---|
| Table | `sites` |
| Table | `devices` |
| Table | `waybill_scan_events` |
| Table | `waybill_projections` |
| Table | `dashboard_changes` |
| Table | `site_change_counters` |
| Table | `site_fetch_leases` |
| Table | `idempotency_records` |
| Table | `audit_logs` |
| Table | `jms_event_policies` |
| Table | `retention_policies` |
| Table | `schema_migrations` |
| Function | `create_datahub_site(...)` — helper provisioning, không phải RPC cho client |

Kiểm tra:

```bash
./bin/run-sql.sh --env-file .env.production /dev/stdin <<'SQL'
select version from schema_migrations order by version;
select tablename from pg_tables where schemaname = 'public' order by tablename;
SQL
```

`run-sql.sh` nhận **file**, không nhận chuỗi SQL trực tiếp; nó chấp nhận `/dev/stdin` nên
heredoc chạy được mà không cần tạo file tạm.

### 5.3. Kiểm Tra Manifest JSON Công Khai

Client đọc control-plane JSON từ `DATAHUB_MANIFEST_BASE_URL`, mặc định là
`DATAHUB_API_BASE_URL`. Các file cần trả HTTP `200`:

```text
manifest/app-manifest.json
manifest/hash-manifest.json
manifest/tier-definitions.json
manifest/version-latest.json
configs/public-config.json
configs/runtime-policy.json
configs/runtime-policy.base.json
configs/runtime-policy.ultra.json
selector-updates/runtime-config.json
selector-updates/selector-update-manifest.json
```

Lệnh test:

```powershell
$base = "https://dev.jmsauto.online"
$paths = @(
  "manifest/app-manifest.json",
  "manifest/hash-manifest.json",
  "manifest/tier-definitions.json",
  "manifest/version-latest.json",
  "configs/public-config.json",
  "configs/runtime-policy.json",
  "configs/runtime-policy.base.json",
  "configs/runtime-policy.ultra.json",
  "selector-updates/runtime-config.json",
  "selector-updates/selector-update-manifest.json"
)

foreach ($p in $paths) {
  $code = & curl.exe -L -s -o NUL -w "%{http_code}" "$base/$p"
  "$code $p"
}
```

Tất cả phải là:

```text
200 <path>
```

> **Đang thiếu.** `release/build-release.ps1 -Upload` đẩy các file này bằng
> `PUT {base}/api/v1/admin/manifests/{objectPath}`, nhưng route admin đó chưa tồn tại trong
> `src/AutoJMS.DataHub.Api`, không có trong `openapi/datahub-v1.yaml`, và `Caddyfile` cũng chưa
> có handler static file — nên `-Upload` hiện trả 404. Trước khi endpoint được bổ sung, publish
> thủ công trên VPS. Không "chữa" bằng cách trỏ client sang bucket của bên thứ ba.

### 5.4. Kiểm Tra Luồng Enroll Và Device Token

App không bao giờ nhận device token từ config. Nó phải tự enroll bằng license assertion do
Render ký. Admin token không bao giờ nằm trong app.

```powershell
$base = "https://dev.jmsauto.online"

# 1. Lấy assertion từ Render bằng access token của phiên đang chạy
$assertion = (Invoke-RestMethod -Method Post "https://autojms-api.onrender.com/api/datahub/license-assertion" `
  -Headers @{ Authorization = "Bearer <access token>" } `
  -ContentType "application/json" -Body "{}").licenseAssertion

# 2. Đổi assertion lấy device token.
#    Assertion đi ở header Authorization, KHÔNG nằm trong body.
$enroll = Invoke-RestMethod -Method Post "$base/api/v1/devices/enroll" `
  -Headers @{ Authorization = "Bearer $assertion" } `
  -ContentType "application/json" `
  -Body (@{ siteCode = "214A02"; deviceName = "MANUAL-TEST"; role = "operator" } | ConvertTo-Json)

$enroll.siteId
$enroll.deviceToken.Substring(0,4) + "..." + $enroll.deviceToken.Substring($enroll.deviceToken.Length - 4)
```

Response `201`: `deviceId`, `siteId`, `siteCode`, `channel`, `tokenType`, `deviceToken`,
`tokenVersion`, `expiresAt`. `role` hiện chỉ chấp nhận `operator`.

Đọc thử change feed bằng device token vừa nhận:

```powershell
$headers = @{ Authorization = "Bearer $($enroll.deviceToken)" }
Invoke-RestMethod "$base/api/v1/sites/$($enroll.siteId)/changes?sinceSeq=0&limit=1" -Headers $headers
```

Ba loại credential không thay thế cho nhau: access token RS256 (Render), license assertion
(một mục đích duy nhất là enroll), device token HMAC (mọi route `/api/v1/sites/...` và hub).
Dùng nhầm loại nào cũng chỉ nhận `401` mà không có thông báo rõ.

`deviceName` phải ổn định giữa các lần chạy — enrollment idempotent theo `(site_id, name)`, tên
đổi mỗi lần khởi động sẽ ăn hết seat rồi trả `409 SEAT_LIMIT_REACHED`.

Sau khi test xong, thu hồi thiết bị test:

```bash
./bin/run-sql.sh --env-file .env.production /dev/stdin <<'SQL'
update devices set status = 'revoked' where name = 'MANUAL-TEST';
SQL
```

## 6. Setup Firebase

### 6.1. Firebase Realtime Database

Render server dùng Firebase Admin SDK để đọc/ghi:

```text
Licenses/{licenseKey}
sessions/{sessionId}
```

Desktop app không kết nối Firebase trực tiếp.

### 6.2. Tạo Firebase Admin Service Account

Trong Firebase Console:

1. Project Settings.
2. Service accounts.
3. Generate new private key.
4. Lưu JSON ở nơi an toàn.
5. Không commit file JSON vào repo.

Khuyến nghị dùng Render env:

```text
FIREBASE_SERVICE_ACCOUNT_JSON=<toàn bộ JSON service account>
```

Hoặc base64:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("serviceAccountKey.json"))
```

Rồi đặt:

```text
FIREBASE_SERVICE_ACCOUNT_BASE64=<base64 JSON>
```

### 6.3. Tạo License Test

Trong Firebase Realtime Database, tạo node:

```text
Licenses/<LICENSE_TEST_KEY>
```

Ví dụ license BASE:

```json
{
  "createdAt": "2026-06-12 00:00",
  "status": "active",
  "tier": "BASE",
  "hwid": "",
  "middleCode": "0000",
  "skipHashCheck": true,
  "modulePolicy": {
    "autoUpdate": true,
    "silentUpdate": true,
    "applyOnNextStartup": true
  },
  "dataSpreadsheetId": "",
  "updateChannel": "stable"
}
```

Ví dụ license ULTRA:

```json
{
  "createdAt": "2026-06-12 00:00",
  "status": "active",
  "tier": "ULTRA",
  "hwid": "",
  "middleCode": "214A02",
  "skipHashCheck": true,
  "modulePolicy": {
    "autoUpdate": true,
    "silentUpdate": true,
    "applyOnNextStartup": true
  },
  "dataSpreadsheetId": "",
  "updateChannel": "stable"
}
```

Ghi chú:

- `hwid` để rỗng cho lần đăng nhập đầu, server sẽ bind vào máy đầu tiên.
- Muốn reset máy, xóa/rỗng `hwid` và xóa session liên quan.
- `tier` chỉ dùng `BASE` hoặc `ULTRA`.
- BASE không được chạy background inventory/database sync.

## 7. Setup Render License Server

### 7.1. Cài Dependency Local

```powershell
cd D:\v1.2605.2(new-test)\backend\render-license-server
npm install
npm run check
```

`npm run check` phải không in lỗi.

### 7.2. Tạo `.env` Local Nếu Muốn Chạy Server Trên Máy

```powershell
cd D:\v1.2605.2(new-test)\backend\render-license-server
copy env.template .env
notepad .env
```

Điền:

```text
PORT=3000
FIREBASE_OPERATION_TIMEOUT_MS=8000

JWT_PRIVATE_KEY=<RS256 private key PEM>
JWT_PUBLIC_KEY=<RS256 public key PEM>

FIREBASE_DATABASE_URL=https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/
FIREBASE_SERVICE_ACCOUNT_BASE64=<base64 Firebase Admin service account JSON>
# or FIREBASE_SERVICE_ACCOUNT_JSON=<Firebase Admin service account JSON>

DATAHUB_API_BASE_URL=https://dev.jmsauto.online
DATAHUB_MANIFEST_BASE_URL=https://dev.jmsauto.online
DATAHUB_CHANNEL=staging
DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY=<RS256 private key PEM>
DATAHUB_LICENSE_ASSERTION_ISSUER=autojms-license
DATAHUB_LICENSE_ASSERTION_AUDIENCE=autojms-datahub-enroll
DATAHUB_LICENSE_ASSERTION_TTL_SECONDS=300
DATAHUB_DEFAULT_SEATS=3

DEFAULT_UPDATE_CHANNEL=stable
VALID_EXE_HASHES=
```

Chạy local:

```powershell
npm start
```

Test health:

```powershell
Invoke-RestMethod http://localhost:3000/health
```

Nếu app cần trỏ sang local backend:

```powershell
$env:AUTOJMS_LICENSE_API_BASE_URL = "http://localhost:3000"
dotnet run --project D:\v1.2605.2(new-test)\src\AutoJMS\AutoJMS.csproj -c Debug --no-restore
```

Nếu chạy bằng file exe đã build:

```powershell
$env:AUTOJMS_LICENSE_API_BASE_URL = "http://localhost:3000"
& "D:\v1.2605.2(new-test)\src\AutoJMS\bin\Debug\net8.0-windows\win-x64\AutoJMS.exe"
```

### 7.3. Deploy Render Production

Có file blueprint mẫu:

```text
D:\v1.2605.2(new-test)\backend\render.yaml
```

Thiết lập Render:

| Field | Giá trị |
|---|---|
| Runtime | Node |
| Root directory | `backend/render-license-server` |
| Build command | `npm ci` |
| Start command | `npm start` |
| Health check path | `/health` |

Env cần set trên Render:

```text
NODE_ENV=production
FIREBASE_OPERATION_TIMEOUT_MS=8000
FIREBASE_DATABASE_URL=https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/
DATAHUB_API_BASE_URL=https://dev.jmsauto.online
DATAHUB_MANIFEST_BASE_URL=https://dev.jmsauto.online
DATAHUB_CHANNEL=production
DATAHUB_LICENSE_ASSERTION_ISSUER=autojms-license
DATAHUB_LICENSE_ASSERTION_AUDIENCE=autojms-datahub-enroll
DATAHUB_LICENSE_ASSERTION_TTL_SECONDS=300
DATAHUB_DEFAULT_SEATS=3
DEFAULT_UPDATE_CHANNEL=stable

JWT_PRIVATE_KEY=<secret>
JWT_PUBLIC_KEY=<secret>
FIREBASE_SERVICE_ACCOUNT_BASE64=<secret>
DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY=<secret>
VALID_EXE_HASHES=<optional>
```

`DATAHUB_ADMIN_TOKEN` **không** được đặt trên Render. Nó chỉ tồn tại trong `.env.production`
trên VPS. Render chỉ giữ khoá ký assertion; nếu thiếu khoá đó thì không có assertion nào được
phát và enrollment đóng lại — đây là mặc định an toàn, không phải lỗi ngầm.

`DATAHUB_LICENSE_ASSERTION_ISSUER` / `_AUDIENCE` / `DATAHUB_CHANNEL` phải khớp chính xác với
`DATAHUB_LICENSE_ASSERTION_ISSUER` / `_AUDIENCE` / `DataHub__Channel` trên VPS, nếu lệch thì
enroll trả `401` mà không có thông báo hữu ích.

Sau deploy:

```powershell
Invoke-RestMethod "https://autojms-api.onrender.com/health"
Invoke-RestMethod "https://autojms-api.onrender.com/health/firebase"
```

Kết quả đúng:

```json
{
  "ok": true,
  "service": "autojms-license-server"
}
```

## 8. Test API License

### 8.1. Test Request Thiếu Dữ Liệu

Lệnh này phải trả lỗi JSON nhanh:

```powershell
try {
  Invoke-WebRequest `
    -Method Post `
    -Uri "https://autojms-api.onrender.com/api/verify-license" `
    -ContentType "application/json" `
    -Body "{}" `
    -TimeoutSec 20
} catch {
  $_.Exception.Response.StatusCode.value__
}
```

Kỳ vọng:

```text
400
```

### 8.2. Test License Fake

License fake phải trả JSON lỗi nghiệp vụ nhanh, không timeout:

```powershell
$body = @{
  licenseKey = "FAKE_LICENSE_FOR_BACKEND_TEST"
  hwid = "FAKE_HWID_FOR_BACKEND_TEST"
  exeHash = "fake"
  appVersion = "debug"
} | ConvertTo-Json -Compress

try {
  Invoke-WebRequest `
    -Method Post `
    -Uri "https://autojms-api.onrender.com/api/verify-license" `
    -ContentType "application/json" `
    -Body $body `
    -TimeoutSec 20
} catch {
  $status = $_.Exception.Response.StatusCode.value__
  $stream = $_.Exception.Response.GetResponseStream()
  $reader = [System.IO.StreamReader]::new($stream)
  "STATUS=$status"
  "BODY=$($reader.ReadToEnd())"
}
```

Kỳ vọng:

```text
STATUS=404
BODY={"success":false,"error":"LICENSE_NOT_FOUND","message":"License key not found."}
```

Nếu request timeout, Render đang không đọc được Firebase hoặc bản deploy chưa có timeout mới.

### 8.3. Test License Thật

Thay bằng license test đã tạo trong Firebase:

```powershell
$body = @{
  licenseKey = "<LICENSE_TEST_KEY>"
  hwid = "MANUAL_TEST_HWID_001"
  exeHash = "debug"
  appVersion = "debug"
} | ConvertTo-Json -Compress

Invoke-RestMethod `
  -Method Post `
  -Uri "https://autojms-api.onrender.com/api/verify-license" `
  -ContentType "application/json" `
  -Body $body `
  -TimeoutSec 30
```

Response đúng phải có:

```text
payload
sid
tier
middleCode
datahub.baseUrl
datahub.apiBaseUrl
datahub.device enrollment token
datahub.manifests
```

Không paste response thật có `payload` hoặc `device enrollment token` vào chat/log public.

## 9. Build Và Chạy App

### 9.1. Build Debug

```powershell
cd D:\v1.2605.2(new-test)
dotnet build .\src\AutoJMS\AutoJMS.csproj -c Debug --no-restore /clp:Summary
```

Kết quả đúng:

```text
Build succeeded.
0 Error(s)
```

Binary:

```text
D:\v1.2605.2(new-test)\src\AutoJMS\bin\Debug\net8.0-windows\win-x64\AutoJMS.exe
```

### 9.2. Chạy App Với Render Production

```powershell
& "D:\v1.2605.2(new-test)\src\AutoJMS\bin\Debug\net8.0-windows\win-x64\AutoJMS.exe"
```

Đăng nhập bằng license test trong Firebase.

### 9.3. Chạy App Với Local Render Server

Terminal 1:

```powershell
cd D:\v1.2605.2(new-test)\backend\render-license-server
npm start
```

Terminal 2:

```powershell
$env:AUTOJMS_LICENSE_API_BASE_URL = "http://localhost:3000"
& "D:\v1.2605.2(new-test)\src\AutoJMS\bin\Debug\net8.0-windows\win-x64\AutoJMS.exe"
```

## 10. Checklist App Chạy Đúng

### BASE License

Kỳ vọng:

- Login thành công.
- Có tabs: HOME, DKCH, TRACKING, PRINT, ABOUT.
- ABOUT là tab cuối.
- Không tự chạy inventory sync/database sync nền.
- Gõ `DASH` không mở `FullStackOperationForm`.

### ULTRA License

Kỳ vọng:

- Login thành công.
- Tabs BASE vẫn giữ nguyên.
- ABOUT vẫn là tab cuối.
- Gõ `DASH` ở HOME URL bar mở `FullStackOperationForm`.
- Background sync chỉ chạy nếu `TierRuntimePolicy` cho phép.
- Enroll thành công, `DataHubClient` có device token và siteId hợp lệ.
- Hub `/hubs/site` kết nối được; mất hub thì tụt về polling `/changes` chứ không mất dữ liệu.

## 11. Troubleshooting

| Triệu chứng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| `/health` OK nhưng `/api/verify-license` timeout | Render không đọc được Firebase hoặc chưa deploy bản timeout mới | Kiểm tra `FIREBASE_SERVICE_ACCOUNT_BASE64`, `FIREBASE_DATABASE_URL`, redeploy |
| App chạy offline, không có device token | Render không trả `datahub.licenseAssertion` | Set `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` trên Render |
| App login fail vì JWT invalid | `JWT_PUBLIC_KEY` trong app/server không khớp private key đang ký | Dùng đúng key pair; nếu đổi public key server-side cần đảm bảo client verify tương thích |
| License bị báo đang dùng máy khác | Firebase `hwid` đã bind máy khác | Reset `Licenses/<key>/hwid` và xóa session liên quan |
| BASE chạy background sync | Tier policy sai hoặc license/tier manifest sai | Kiểm tra `tier-definitions.json`, `runtime-policy*.json`, log policy |
| Enroll trả `401 ASSERTION_INVALID` | Issuer/audience/channel lệch giữa Render và VPS, hoặc lệch đồng hồ quá TTL | So `DATAHUB_LICENSE_ASSERTION_*` và `DATAHUB_CHANNEL` hai phía |
| Enroll trả `409 SEAT_LIMIT_REACHED` | `deviceName` đổi mỗi lần chạy nên ăn hết seat | Giữ tên ổn định theo `MachineName` + hwid; chỉ tăng `seats` khi thực sự cần |
| `/api/v1/sites/...` trả `401` | Device token hết hạn hoặc `tokenVersion` của site đã bị nâng | Để `HeartbeatSupervisor` gia hạn; enroll lại nếu device đã bị revoke |
| Manifest 404 | File chưa từng được publish, hoặc sai `DATAHUB_MANIFEST_BASE_URL` | Curl thẳng URL công khai; nhớ `-Upload` hiện đang hỏng |
| `${VAR:?}` fail khi `docker compose` | Quên `--env-file` | Dùng `./bin/dc.sh`, script luôn truyền sẵn |
| Render deploy fail ở `npm ci` | `package-lock.json` thiếu/không khớp | Chạy `npm install` local, commit lockfile |

## 12. Quy Tắc Không Được Phá

- Không log full JWT, JMS AuthToken, Firebase credential, license assertion, device token. Che `first4...last4`.
- Không đưa `DATAHUB_ADMIN_TOKEN` vào desktop client, vào Render, hay vào bất kỳ JSON công khai nào.
- Không bật `DATAHUB_ALLOW_STAGING_TEST_ISSUER` trên production — bật là ai cũng tự ký được assertion.
- Không publish `.nupkg`, setup exe, private key qua manifest control plane. Binary nằm ở GitHub Releases.
- Không mở cổng PostgreSQL ra host. Database chỉ nghe trên private Docker network.
- Không để BASE chạy background inventory/database sync.
- Không mở GitHub page khi update; Velopack tự xử lý qua GitHub Releases.
- Không truy cập WebView2 ngoài UI thread.
- Không sửa logic HOME/DKCH/TRACKING/PRINT/ABOUT chỉ để setup backend.

## 13. Lệnh Kiểm Tra Nhanh Toàn Bộ

```powershell
# Render server syntax
cd D:\v1.2605.2(new-test)\backend\render-license-server
npm run check

# DataHub stack + migrations (trên VPS)
ssh datahub-root "cd /opt/autojms-datahub && ./bin/dc.sh --env-file .env.production ps"
ssh datahub-root "cd /opt/autojms-datahub && ./bin/apply-migrations.sh --env-file .env.production"

# DataHub health
Invoke-RestMethod "https://dev.jmsauto.online/health/ready"

# DataHub public files
$base = "https://dev.jmsauto.online"
$paths = @(
  "manifest/app-manifest.json",
  "manifest/hash-manifest.json",
  "manifest/tier-definitions.json",
  "manifest/version-latest.json",
  "configs/public-config.json",
  "configs/runtime-policy.json",
  "configs/runtime-policy.base.json",
  "configs/runtime-policy.ultra.json",
  "selector-updates/runtime-config.json",
  "selector-updates/selector-update-manifest.json"
)
foreach ($p in $paths) {
  $code = & curl.exe -L -s -o NUL -w "%{http_code}" "$base/$p"
  "$code $p"
}

# Render health
Invoke-RestMethod "https://autojms-api.onrender.com/health"

# App build
cd D:\v1.2605.2(new-test)
dotnet build .\src\AutoJMS\AutoJMS.csproj -c Debug --no-restore /clp:Summary
```

Manual hoàn tất khi:

- DataHub migration match.
- Tất cả public JSON trả `200`.
- Render `/health` OK.
- Fake license trả JSON lỗi nhanh.
- License test thật trả payload/session/tier/DataHub config.
- AutoJMS build thành công và đăng nhập được bằng license test.
