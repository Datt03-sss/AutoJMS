# AutoJMS Backend Deploy Status

Date: 2026-06-11

## Completed

- Supabase project linked: `bnsnnrlwfzxemmizknwy`.
- Supabase bucket ready: `autojms-modules`.
- Supabase migrations applied and local/remote history match:
  - `202606110001_autojms_bootstrap.sql`
  - `202606110002_tighten_autojms_privileges.sql`
- Public Supabase JSON files return HTTP 200:
  - `manifest/app-manifest.json`
  - `manifest/hash-manifest.json`
  - `manifest/tier-definitions.json`
  - `manifest/version-latest.json`
  - `configs/public-config.json`
  - `configs/runtime-policy.json`
  - `configs/runtime-policy.base.json`
  - `configs/runtime-policy.ultra.json`
  - `selector-updates/runtime-config.json`
  - `selector-updates/selector-update-manifest.json`
- Render server source has a runnable Node project:
  - `backend/render-license-server/package.json`
  - `backend/render-license-server/package-lock.json`
  - `backend/render-license-server/.env.example`
  - `backend/render.yaml`
- Render server supports:
  - `.env` loading for local development.
  - Firebase Admin credential from JSON env, base64 env, credential path, or local fallback file.
  - Supabase project URL and anon key returned to the desktop client.
- Firebase operation timeout through `FIREBASE_OPERATION_TIMEOUT_MS`.
- Firebase health endpoint: `/health/firebase`.
- Desktop app builds successfully:
  - `src/AutoJMS/bin/Debug/net8.0-windows/win-x64/AutoJMS.exe`

## Current Verification

Commands that passed locally:

```powershell
cd D:\v1.2605.2(new-test)\backend\render-license-server
npm install
npm run check

cd D:\v1.2605.2(new-test)\backend\supabase
supabase migration list --linked

cd D:\v1.2605.2(new-test)
dotnet build .\src\AutoJMS\AutoJMS.csproj -c Debug --no-restore /clp:Summary
Invoke-RestMethod "https://autojms-api.onrender.com/health"
```

## Not Completed From This Machine

Render production deployment cannot be completed from this local machine because these credentials/tools are not present:

- Render CLI or `RENDER_API_KEY`.
- Render service ID.
- `JWT_PRIVATE_KEY`.
- `JWT_PUBLIC_KEY`.
- Firebase Admin service account credential.

## Required Render Environment

Set these on Render before deploying `backend/render-license-server`:

```text
JWT_PRIVATE_KEY=<RS256 private key PEM>
JWT_PUBLIC_KEY=<RS256 public key PEM>
FIREBASE_DATABASE_URL=https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/
FIREBASE_SERVICE_ACCOUNT_BASE64=<base64 Firebase Admin service account JSON>
# or FIREBASE_SERVICE_ACCOUNT_JSON=<Firebase Admin service account JSON>
# or GOOGLE_APPLICATION_CREDENTIALS=<secret file path>
SUPABASE_PROJECT_URL=https://bnsnnrlwfzxemmizknwy.supabase.co
SUPABASE_BASE_URL=https://bnsnnrlwfzxemmizknwy.supabase.co/storage/v1/object/public/autojms-modules
SUPABASE_ANON_KEY=<Supabase anon key, never service_role>
FIREBASE_OPERATION_TIMEOUT_MS=8000
DEFAULT_UPDATE_CHANNEL=stable
VALID_EXE_HASHES=<optional comma-separated hashes>
```

## Final Acceptance Test

After deploying Render:

1. `GET https://autojms-api.onrender.com/health` returns JSON `ok: true`.
2. `GET https://autojms-api.onrender.com/health/firebase` returns JSON `ok: true` or JSON 503 in under 10 seconds.
3. `POST /api/verify-license` with a fake well-formed key returns `404` JSON with `error: "LICENSE_NOT_FOUND"` quickly.
4. `POST /api/verify-license` with a controlled active Firebase license returns:
   - `payload`
   - `sid`
   - `tier`
   - `middleCode`
   - `supabase.baseUrl`
   - `supabase.projectUrl`
   - `supabase.anonKey`
   - `supabase.manifests`
5. Launch `src/AutoJMS/bin/Debug/net8.0-windows/win-x64/AutoJMS.exe`.
6. Login with a controlled license.
7. Confirm BASE has no background inventory/database sync.
8. Confirm ULTRA can open `FullStackOperationForm` and use Supabase-backed sync paths.
