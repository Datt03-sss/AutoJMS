# Manual Setup Backend Để AutoJMS Chạy Được

Ngày cập nhật: 2026-06-12

Manual này dùng cho backend hiện tại trong `D:\v1.2605.2(new-test)\backend`.

Mục tiêu cuối cùng:

- Render license server chạy được `/health`, `/api/verify-license`, `/api/heartbeat`.
- Firebase có license test để app đăng nhập.
- Supabase có schema/RPC/storage manifest đúng.
- AutoJMS build được và đăng nhập bằng license test.

Không ghi secret thật vào repo. Không commit `.env`, Firebase service account, Supabase service-role key, JWT private key, hoặc token production.

## 1. Kiến Trúc Backend

```text
AutoJMS.exe
  -> Render server /api/verify-license
      -> Firebase Realtime Database: Licenses, sessions
      -> trả về JWT + tier + Supabase config
  -> Supabase Storage: manifest/config/hash/tier/selector-update JSON
  -> Supabase PostgreSQL/RPC: waybills, inventory sync lease, tracking rows
  -> Render server /api/heartbeat
      -> Firebase sessions
```

Vai trò từng dịch vụ:

| Dịch vụ | Vai trò |
|---|---|
| Firebase | License, HWID, session, tier, middleCode |
| Render | API verify license, heartbeat, logout, cấp JWT |
| Supabase Storage | Manifest/config/hash/tier/selector-update JSON |
| Supabase PostgreSQL | Waybill database và RPC cho ULTRA sync |
| GitHub Releases | Velopack binaries, không dùng Supabase để chứa `.nupkg` |

## 2. Thông Tin Backend Hiện Tại

```text
Supabase project ref: bnsnnrlwfzxemmizknwy
Supabase project URL: https://bnsnnrlwfzxemmizknwy.supabase.co
Supabase public bucket: autojms-modules
Supabase storage base:
https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules

Render production URL:
https://autojms-api.onrender.com

Firebase RTDB:
https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/
```

Source chính:

```text
backend/render-license-server/server.js
backend/render-license-server/package.json
backend/render-license-server/.env.example
backend/render.yaml
backend/supabase/migrations/
backend/BACKEND_DEPLOY_STATUS.md
```

## 3. Công Cụ Cần Có

Kiểm tra trên máy deploy/dev:

```powershell
node -v
npm -v
dotnet --info
supabase --version
git --version
```

Yêu cầu:

| Tool | Mục đích |
|---|---|
| Node.js >= 20 | Chạy Render license server |
| npm | Cài dependency backend |
| .NET SDK 8 hoặc SDK mới có workload Windows | Build AutoJMS |
| Supabase CLI | Apply/check migration |
| Render dashboard hoặc Render API/CLI | Deploy server |
| Firebase console access | Tạo service account và license test |

## 4. Secret Cần Chuẩn Bị

Không đặt các giá trị này vào tài liệu hoặc git.

| Secret | Lấy ở đâu | Dùng cho |
|---|---|---|
| `JWT_PRIVATE_KEY` | Tự tạo RSA private key | Render ký license JWT |
| `JWT_PUBLIC_KEY` | Từ RSA private key | Render verify/chuẩn public key |
| `FIREBASE_SERVICE_ACCOUNT_JSON` | Firebase Console | Render Admin SDK đọc/ghi RTDB |
| `SUPABASE_ANON_KEY` | Supabase dashboard, Project Settings, API | Render trả về app để dùng RPC theo anon policy |
| Supabase service-role key | Supabase dashboard | Chỉ dùng thủ công/server-side khi upload/admin, không trả về client |

Nếu cần tạo JWT key pair bằng OpenSSL:

```powershell
openssl genrsa -out jwt_private.pem 2048
openssl rsa -in jwt_private.pem -pubout -out jwt_public.pem
```

Khi nhập vào Render env, có thể dán nguyên PEM nhiều dòng hoặc dùng dạng escaped `\n`. `server.js` đã normalize `\n`.

## 5. Setup Supabase

### 5.1. Login Và Link Project

```powershell
cd D:\v1.2605.2(new-test)\backend\supabase
supabase login
supabase link --project-ref bnsnnrlwfzxemmizknwy
```

Nếu project đã link, kiểm tra:

```powershell
supabase migration list --linked
```

Kết quả đúng phải có local/remote khớp:

```text
202606110001 | 202606110001
202606110002 | 202606110002
```

### 5.2. Apply Migration Nếu Chưa Có

```powershell
cd D:\v1.2605.2(new-test)\backend\supabase
supabase db push
```

Migration cần có:

```text
backend/supabase/migrations/202606110001_autojms_bootstrap.sql
backend/supabase/migrations/202606110002_tighten_autojms_privileges.sql
```

Schema/RPC kỳ vọng:

| Loại | Tên |
|---|---|
| Table | `public.waybills` |
| Table | `public.inventory_sync_leases` |
| Table | `public.app_modules` |
| Table | `public.app_manifest` |
| Table | `public.app_configs` |
| RPC | `try_acquire_inventory_lease` |
| RPC | `refresh_inventory_lease` |
| RPC | `release_inventory_lease` |
| RPC | `complete_inventory_sync` |
| RPC | `upsert_new_waybills` |
| RPC | `merge_waybill_tracking_rows` |

### 5.3. Kiểm Tra Storage Public JSON

Bucket public:

```text
autojms-modules
```

Các file cần trả HTTP `200`:

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
$base = "https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules"
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

### 5.4. Kiểm Tra Anon Key Không Bị Sai

Dùng anon key, không dùng service-role key trong app.

```powershell
$anon = "<SUPABASE_ANON_KEY>"
$headers = @{
  apikey = $anon
  Authorization = "Bearer $anon"
}
$base = "https://bnsnnrlwfzxemmizknwy.supabase.co"

Invoke-WebRequest "$base/rest/v1/waybills?select=waybill_no&limit=1" -Headers $headers
```

Test RPC:

```powershell
$body = @{ p_waybills = @("TEST_MANUAL_001") } | ConvertTo-Json
Invoke-WebRequest -Method Post "$base/rest/v1/rpc/upsert_new_waybills" `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body
```

Sau test, xóa row test bằng Supabase SQL editor hoặc CLI admin:

```sql
delete from public.waybills where waybill_no = 'TEST_MANUAL_001';
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
copy .env.example .env
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

SUPABASE_PROJECT_URL=https://bnsnnrlwfzxemmizknwy.supabase.co
SUPABASE_BASE_URL=https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules
SUPABASE_ANON_KEY=<Supabase anon key, never service_role>

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
SUPABASE_PROJECT_URL=https://bnsnnrlwfzxemmizknwy.supabase.co
SUPABASE_BASE_URL=https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules
DEFAULT_UPDATE_CHANNEL=stable

JWT_PRIVATE_KEY=<secret>
JWT_PUBLIC_KEY=<secret>
FIREBASE_SERVICE_ACCOUNT_BASE64=<secret>
SUPABASE_ANON_KEY=<secret anon key>
VALID_EXE_HASHES=<optional>
```

Không set `SUPABASE_ANON_KEY` bằng service-role key.

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
supabase.baseUrl
supabase.projectUrl
supabase.anonKey
supabase.manifests
```

Không paste response thật có `payload` hoặc `anonKey` vào chat/log public.

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
- Supabase RPC hoạt động khi FullStack/ULTRA sync cần dùng.

## 11. Troubleshooting

| Triệu chứng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| `/health` OK nhưng `/api/verify-license` timeout | Render không đọc được Firebase hoặc chưa deploy bản timeout mới | Kiểm tra `FIREBASE_SERVICE_ACCOUNT_BASE64`, `FIREBASE_DATABASE_URL`, redeploy |
| App báo `Supabase anon key is not configured` | Render không trả `supabase.anonKey` | Set `SUPABASE_ANON_KEY` trên Render |
| App login fail vì JWT invalid | `JWT_PUBLIC_KEY` trong app/server không khớp private key đang ký | Dùng đúng key pair; nếu đổi public key server-side cần đảm bảo client verify tương thích |
| License bị báo đang dùng máy khác | Firebase `hwid` đã bind máy khác | Reset `Licenses/<key>/hwid` và xóa session liên quan |
| BASE chạy background sync | Tier policy sai hoặc license/tier manifest sai | Kiểm tra `tier-definitions.json`, `runtime-policy*.json`, log policy |
| Supabase RPC 401/403 | RLS/grant/anon key sai | Kiểm tra migration `202606110002`, anon key, RPC grants |
| Manifest 404 | Bucket/path sai | Kiểm tra `SUPABASE_BASE_URL` và public storage files |
| Render deploy fail ở `npm ci` | `package-lock.json` thiếu/không khớp | Chạy `npm install` local, commit lockfile |

## 12. Quy Tắc Không Được Phá

- Không log full JWT, JMS AuthToken, Firebase credential, Supabase key.
- Không dùng Supabase service-role key trong desktop client.
- Không upload `.nupkg`, setup exe, private key lên Supabase Storage.
- Không để BASE chạy background inventory/database sync.
- Không mở GitHub page khi update; Velopack tự xử lý qua GitHub Releases.
- Không truy cập WebView2 ngoài UI thread.
- Không sửa logic HOME/DKCH/TRACKING/PRINT/ABOUT chỉ để setup backend.

## 13. Lệnh Kiểm Tra Nhanh Toàn Bộ

```powershell
# Render server syntax
cd D:\v1.2605.2(new-test)\backend\render-license-server
npm run check

# Supabase migrations
cd D:\v1.2605.2(new-test)\backend\supabase
supabase migration list --linked

# Supabase public files
$base = "https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules"
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

- Supabase migration match.
- Tất cả public JSON trả `200`.
- Render `/health` OK.
- Fake license trả JSON lỗi nhanh.
- License test thật trả payload/session/tier/Supabase config.
- AutoJMS build thành công và đăng nhập được bằng license test.
