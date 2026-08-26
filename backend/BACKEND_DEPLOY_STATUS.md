# AutoJMS Backend Deploy Status

Date: 2026-08-23 · Revised: **2026-08-26**

> ## Deploy source decision — L-1 resolved 2026-08-26: **option A1**
>
> The Render Web Service is to be pointed at the **monorepo** `Datt03-sss/AutoJMS`, building and
> running from `backend/render-license-server`, driven by the blueprint at **`render.yaml` in the
> repository root**.
>
> **The blueprint moved, and the location is the whole point.** Render discovers a Blueprint only
> as `render.yaml` at the ROOT of the connected repository — there is no setting for an alternate
> path. The `backend/render.yaml` listed further down this file was therefore *never read by
> Render*; it described a deployment that could not happen. That file is now a pointer stub. Use
> [`../render.yaml`](../render.yaml).
>
> **Not yet executed.** Until the owner performs the seven dashboard steps A1-a…A1-g in
> [`docs/agent/BACKEND_BUILD_AND_VPS_DEPLOY_PLAN.vi.md`](../docs/agent/BACKEND_BUILD_AND_VPS_DEPLOY_PLAN.vi.md)
> §3.4, Render production still serves `Datt03-sss/AutoJMS-API` (`server.js` ≈895 lines), which
> cannot issue a DataHub assertion at all. Anything below describing the license server describes
> the **repo**, not production.
>
> Two blueprint values worth knowing about before applying it:
> - `numInstances: 1` is a **correctness** constraint, not a capacity one. The JTI replay cache
>   (`server.js:290`) and every rate-limit store are per-process; a second instance is a second
>   replay window and a second rate-limit budget.
> - `DATAHUB_API_BASE_URL` is now `sync: false`. It used to be an inline
>   `https://datahub.example.com`, which meant every blueprint sync **overwrote** whatever real
>   hostname was set in the dashboard — a recurring fault, not a one-off omission.

## Completed

- DataHub stack deployed to the VPS at `/opt/autojms-datahub`: containers `caddy`, `api`,
  `postgres` on Ubuntu 24.04. Public host `https://dev.jmsauto.online`, TLS issued by
  Let's Encrypt through Caddy.
- `GET /health/live` and `GET /health/ready` return 200 through Caddy.
- All five forward-only migrations applied and recorded in `schema_migrations`:
  - `001_core.sql`
  - `002_seed_policies.sql`
  - `003_seed_retention.sql`
  - `004_projection_slot_payloads.sql`
  - `005_change_retention_floor.sql`
- Twelve tables exist; `create_datahub_site(...)` is the only SQL function in `public`.
- PostgreSQL is not published to the host; only ports 22/80/443 are open.
- `smoke-test.sh` passes against staging, including the five negative cases.
- Operational scripts deployed to `/opt/autojms-datahub/bin/`: `dc.sh`, `apply-migrations.sh`,
  `run-sql.sh`, `smoke-test.sh`, `_datahub-common.sh`.
- **Manifest write path exists and is reachable.** `PUT /api/v1/admin/manifests/{**objectPath}` is
  mapped at `src/AutoJMS.DataHub.Api/Endpoints/ManifestEndpoints.cs:34` and documented at
  `backend/datahub/openapi/datahub-v1.yaml:476`. `/configs/*` is served by Kestrel through the
  `Caddyfile` catch-all `reverse_proxy api:8080`, not by a static-file handler — no static handler is
  needed or wanted. Seed manifests were published and their ETags verified on 26-08.
- **VPS hardening applied**, per the operator's deploy report of 26-08: UFW limited to 22/80/443,
  SSH key-only, `fail2ban`, `unattended-upgrades`, NTP. Applied and reported by the VPS operator;
  not independently re-verified from this repo.
- Render server source has a runnable Node project:
  - `backend/render-license-server/package.json`
  - `backend/render-license-server/package-lock.json`
  - `backend/render-license-server/env.template`
  - ~~`backend/render.yaml`~~ → **`render.yaml` at the repository root** (see the L-1 box above;
    `backend/render.yaml` is now a pointer stub and was never readable by Render)
- Render server supports:
  - `.env` loading for local development.
  - Firebase Admin credential from JSON env, base64 env, credential path, or local fallback file.
  - Signing a short-lived RS256 license assertion and returning it with the API base URL, so the
    station can enroll itself. Render never holds a device token.
- Firebase operation timeout through `FIREBASE_OPERATION_TIMEOUT_MS`.
- Firebase health endpoint: `/health/firebase`.
- Desktop app builds successfully with SignalR client and SQLCipher-backed local databases.

## Current Verification

Commands that passed:

```powershell
cd D:\v1.2605.2(new-test)\backend\render-license-server
npm install
npm run check

cd D:\v1.2605.2(new-test)
dotnet build .\AutoJMS.slnx -c Release
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
Invoke-RestMethod "https://autojms-api.onrender.com/health"
Invoke-RestMethod "https://dev.jmsauto.online/health/ready"
```

On the VPS:

```bash
cd /opt/autojms-datahub
./bin/dc.sh --env-file .env.production ps
./bin/apply-migrations.sh --env-file .env.production
./bin/smoke-test.sh --env-file .env.staging --base https://dev.jmsauto.online
```

## Not Completed

- **Backup that has never been restored.** The operator's 26-08 report records a `backup-postgres.ps1`
  dry run that produced a dump file. That is not the gate: §12.3 of the deploy plan (item H3) asks for a
  **restore** into a scratch database with a measured RPO/RTO. No scheduled backup is committed either
  (H1), and there is no off-VPS encrypted copy (H2). Until a restore has actually run, treat the backup
  as absent.
- **No recorded rollback digest.** The running image was built on the VPS as `autojms-datahub-api:local`,
  so there is no registry digest to roll back to and `start-stack.ps1` was bypassed — Cổng 3 of the
  checklist is not met. A redeploy currently has no previous-known-good reference.
- **Missing endpoints.** No `notes` / `checks` / `tasks` routes, so those FullStackForm panels
  remain local-only.
- **`DeviceIdentity.Role`** is carried through enrollment but never enforced.
- Render production deployment cannot be completed from this local machine because these
  credentials are not present: `RENDER_API_KEY`, Render service ID,
  `JWT_PRIVATE_KEY`, `JWT_PUBLIC_KEY`, `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY`, and the Firebase
  Admin service account credential. The CLI itself is no longer missing — it is installed
  repo-local and pinned at v2.24.0 (`tools/render-cli/`) — but it is unauthenticated, and it
  cannot perform any of A1-a…A1-g; those stay dashboard operations.

## Required Render Environment

Set these on Render before deploying `backend/render-license-server`:

```text
JWT_PRIVATE_KEY=<RS256 private key PEM>
JWT_PUBLIC_KEY=<RS256 public key PEM>
FIREBASE_DATABASE_URL=https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/
FIREBASE_SERVICE_ACCOUNT_BASE64=<base64 Firebase Admin service account JSON>
# or FIREBASE_SERVICE_ACCOUNT_JSON=<Firebase Admin service account JSON>
# or GOOGLE_APPLICATION_CREDENTIALS=<secret file path>
DATAHUB_API_BASE_URL=https://dev.jmsauto.online
DATAHUB_MANIFEST_BASE_URL=https://dev.jmsauto.online
DATAHUB_CHANNEL=production
DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY=<RS256 private key PEM>
DATAHUB_LICENSE_ASSERTION_ISSUER=autojms-license
DATAHUB_LICENSE_ASSERTION_AUDIENCE=autojms-datahub-enroll
DATAHUB_LICENSE_ASSERTION_TTL_SECONDS=300
DATAHUB_DEFAULT_SEATS=3
FIREBASE_OPERATION_TIMEOUT_MS=8000
DEFAULT_UPDATE_CHANNEL=stable
VALID_EXE_HASHES=<optional comma-separated hashes>
```

`DATAHUB_ADMIN_TOKEN` must never be set on Render. It exists only in `.env.production` on the
VPS.

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
   - `datahub.baseUrl`
   - `datahub.apiBaseUrl`
   - `datahub.licenseAssertion`
   - `datahub.manifests`
5. The client exchanges that assertion at `POST https://dev.jmsauto.online/api/v1/devices/enroll`
   and receives a `deviceToken` plus a `siteId`.
6. Launch the built `AutoJMS.exe` and log in with a controlled license.
7. Confirm BASE has no background inventory/database sync.
8. Confirm ULTRA can open `FullStackOperationForm`, read `/api/v1/sites/{siteId}/changes`, and
   connect to `/hubs/site`.
