# Backend Architecture

> **Scope split.** This document covers the **license / update plane** only:
> Render license server, Firebase license state, VPS config manifests, GitHub Releases.
>
> The **data plane** (waybill ingest, projections, realtime) is a separate VPS deployment
> and is documented in [datahub-backend-design.vi.md](./datahub-backend-design.vi.md)
> (REST + SignalR + PostgreSQL + Caddy, as built in `src/AutoJMS.DataHub.Api`).
> Diagrams: [datahub-backend-diagrams.md](./datahub-backend-diagrams.md).
> Deployment steps: [backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md](../../backend/datahub/deploy/VPS_DEPLOY_GUIDE.vi.md).
>
> The two planes share exactly one interface: a **signed license assertion** carrying
> `channel`, `site_codes`, `seats`, `token_version`, `exp`, and an optional `datahub_url`.
> DataHub never reads Firebase and has no notion of tier.

## Current Verified Baseline

Backend responsibilities verified from current files:

- Render server: `backend/render-license-server/server.js`.
- Firebase: license/session/tier state used by the Render server via Firebase Admin SDK.
- DataHub API (`dev.jmsauto.online`): data plane plus the public manifest/config/hash/tier/selector-update JSON.
- DataHub PostgreSQL: private to the API container network; `DataHubClient` reaches it only through REST endpoints.
- GitHub Releases: Velopack binary assets.

Important mismatches to preserve in audit:

- `server.js` returns `license.modulePolicy`, but `LicenseApiService` parses root `modulePolicy`. Effective module policy behavior is `NEED VERIFY`.
- Resolved 2026-08-23: `DataHubClient` calls REST endpoints only. The schema is `backend/datahub/migrations/001_core.sql` … `005_change_retention_floor.sql`; the old `datahub-migration.sql` is not the source of truth.
- Checked-in hash manifest sample has a shape mismatch with `HashManifest.cs` expectation.

Use this baseline if older content below conflicts.

## Service Overview

```
┌────────────────────────────────────────────────────────────────────┐
│                      AUTOJMS BACKEND                               │
│                                                                    │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐      │
│  │  JMS Website │    │  License     │    │  DataHub   │      │
│  │  jtexpress.vn │    │  Server      │    │  API (VPS)  │      │
│  └──────┬───────┘    └──────┬───────┘    └──────┬───────┘      │
│         │                   │                   │                  │
│         │ WebView2         │ HTTP/REST         │ HTTPS            │
│         │                 │                   │                  │
│         └────────┬────────┘                 │                  │
│                  │                              │                  │
│                  ▼                              ▼                  │
│         ┌──────────────────┐        ┌────────────────┐         │
│         │  AUTOJMS CLIENT   │        │   Firebase    │         │
│         │                  │        │   (License    │         │
│         │  JmsApiClient   │        │    Data)      │         │
│         └──────────────────┘        └────────────────┘         │
└────────────────────────────────────────────────────────────────┘
```

## License Server (Render)

**URL**: https://autojms-api.onrender.com

**Technology**: Node.js + Express

### API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/verify-license` | POST | Verify license key, issue JWT |
| `/api/heartbeat` | POST | Maintain session, receive commands |
| `/api/logout` | POST | Invalidate session |

### Verify License Flow

```
Client                                      Server
  │                                            │
  │  POST /api/verify-license                │
  │  {licenseKey, hwid, exeHash}             │
  │───────────────────────────────────────────▶│
  │                                            │
  │  1. Read Firebase Licenses/{key}           │
  │  2. Validate status, HWID                 │
  │  3. Create session                       │
  │  4. Sign JWT (RS256, 60min)              │
  │  5. Return {payload: JWT, datahub: {...}}│
  │◀───────────────────────────────────────────│
  │                                            │
  │  Validate JWT locally                      │
  │  (RS256 with hardcoded public key)        │
```

### Heartbeat Flow

```
Client                                      Server
  │                                            │
  │  POST /api/heartbeat                      │
  │  Authorization: Bearer {JWT}              │
  │───────────────────────────────────────────▶│
  │                                            │
  │  1. Validate JWT                         │
  │  2. Check session exists                 │
  │  3. Update lastPing                     │
  │  4. Issue new JWT                       │
  │  5. Return {action: "continue", payload: newJWT}│
  │◀───────────────────────────────────────────│
  │                                            │
  │  OR                                        │
  │                                            │
  │  Return {action: "kill", reason: ...}   │
```

## Firebase (License Data)

**URL**: https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app

### Database Schema

```
Licenses/
  {license-key}/
    status: "active" | "revoked"
    tier: "BASE" | "ULTRA"
    hwid: "<machine-hash>" | null
    activatedAt: <timestamp>
    skipHashCheck: true | false
    modulePolicy/
      autoUpdate: boolean
      silentUpdate: boolean
      applyOnNextStartup: boolean
    dataSpreadsheetId: string
    updateChannel: "stable" | "beta"

Sessions/
  {session-id}/
    licenseKey: string
    hwid: string
    tier: string
    status: "active"
    appVersion: string
    ip: string
    createdAt: timestamp
    lastPing: timestamp
```

## DataHub (Data Plane + Manifest Control Plane)

**URL**: https://dev.jmsauto.online
**Source**: `src/AutoJMS.DataHub.Api/` · **Contract**: `backend/datahub/openapi/datahub-v1.yaml`
**Schema**: `backend/datahub/migrations/001_core.sql` … `005_change_retention_floor.sql`

ASP.NET Core net10.0 behind Caddy, with PostgreSQL in Docker on the private compose network.
Clients never touch the database — every read and write goes through an authenticated endpoint.
There are no client-callable SQL functions, no row-level security, and no object store.

Full design: [datahub-backend-design.vi.md](./datahub-backend-design.vi.md).
Diagrams: [datahub-backend-diagrams.md](./datahub-backend-diagrams.md).

### HTTP Surface

| Route | Method | Auth |
|---|---|---|
| `/health/live`, `/health/ready` | GET | none |
| `/api/v1/devices/enroll` | POST | license assertion in `Authorization: Bearer` |
| `/api/v1/sites/{siteId}/jms/ingest` | POST | device token |
| `/api/v1/sites/{siteId}/jms/observations` | POST | device token |
| `/api/v1/sites/{siteId}/lease/{acquire,renew,release}` | POST | device token |
| `/api/v1/sites/{siteId}/changes` | GET | device token |
| `/api/v1/sites/{siteId}/projections/snapshot` | GET | device token |
| `/hubs/site` | WS | device token |

Adding a route means editing `Endpoints/`, the OpenAPI file, and `scripts/smoke-test.sh`. A route
present in only one of the three is a bug waiting to ship.

### PostgreSQL Tables

Twelve tables, all created by the forward-only migrations:

| Table | Role |
|---|---|
| `sites` | One row per office; `site_code` is what enrollment matches on |
| `devices` | Enrolled stations; `status` is `active` \| `revoked` \| `disabled` |
| `waybill_scan_events` | Append-only event log from `/jms/ingest` |
| `waybill_projections` | Current-state table read by `/projections/snapshot` |
| `dashboard_changes` | Change feed rows read by `/changes` |
| `site_change_counters` | Monotonic `change_seq` per site |
| `site_fetch_leases` | Single-leader election for JMS fetching |
| `idempotency_records` | Backs the `Idempotency-Key` header on ingest |
| `audit_logs` | Enrollment and admin actions |
| `jms_event_policies` | Which JMS operations map to which projection transitions |
| `retention_policies` | Per-table retention windows |
| `schema_migrations` | Applied-migration markers |

The only SQL function is `create_datahub_site(...)`, a provisioning helper invoked by
`scripts/provision-site.ps1`. It is not reachable from a client.

### Manifest Control Plane

Small JSON served over plain HTTP from `DATAHUB_MANIFEST_BASE_URL` (defaults to
`DATAHUB_API_BASE_URL`):

```
https://dev.jmsauto.online/
├── manifest/
│   ├── app-manifest.json
│   ├── version-latest.json      ← Version control plane
│   ├── hash-manifest.json       ← DLL hashes
│   └── tier-definitions.json    ← Tier definitions
├── selector-updates/
│   ├── runtime-config.json
│   └── selector-update-manifest.json
└── configs/
    ├── public-config.json
    ├── runtime-policy.json
    ├── runtime-policy.base.json
    └── runtime-policy.ultra.json
```

> **Open gap.** `PUT /api/v1/admin/manifests/{objectPath}` — the publish route
> `release/build-release.ps1 -Upload` targets — is not implemented, is absent from the OpenAPI
> file, and has no `Caddyfile` handler. Publishing returns 404; place the JSON on the VPS by hand
> until the endpoint lands.

## GitHub Releases (Binaries)

**Repository**: Datt03-sss/AutoJMS-Update

### Assets

| Asset | Purpose | Size |
|-------|---------|------|
| RELEASES | Velopack index | ~1KB |
| AutoJMS.nupkg | Package | ~100MB |
| *Setup.exe | Installer | ~100MB |

### Binary Split Strategy

| Content | Host | Reason |
|---------|------|--------|
| Large binaries (.nupkg, Setup.exe) | GitHub Releases | Velopack reads them from there directly |
| Small manifests (JSON) | DataHub API | Control plane carries JSON only |

## Security Architecture

### License JWT

```
Algorithm: RS256
Issuer: autojms-license-server
Audience: autojms-desktop-client
Lifetime: 60 minutes
```

Client validates with hardcoded public key.

### HWID Lock

License bound to hardware:
- SMBIOS UUID
- Physical disk serial
- Machine GUID

### DataHub Device Token

Read via `AUTOJMS_DATAHUB_DEVICE_TOKEN`; never compiled into the binary.
Sent as `Bearer` to `/api/v1/sites/{siteId}/...`. The VPS API decides what the token may reach —
the client has no direct database access at all.

### JMS AuthToken

32-char hex from JMS web session.
Not transmitted to license server.

