# Backend Structure

## Current Verified Baseline

Verified 2026-08-23 from `backend/render-license-server/server.js`, `src/AutoJMS/Data/DataHubClient.cs`,
`src/AutoJMS/Updates/VpsManifestService.cs`, `src/AutoJMS.DataHub.Api/Endpoints/`, and
`backend/datahub/migrations/`.

Backend roles:

- Render server: Node/Express license API; also signs the enrollment assertion.
- Firebase Realtime Database: license/session storage behind Render server.
- DataHub API: ASP.NET Core net10.0 on the VPS behind Caddy at `https://dev.jmsauto.online`.
  Owns enrollment, JMS ingest, change feed, lease, the SignalR hub, and the manifest JSON.
- DataHub PostgreSQL: Postgres in Docker on the private compose network. No public port, no
  client access — only the API talks to it.
- GitHub Releases: large Velopack binary assets.

Render endpoints in code:

- `GET /health`
- `POST /api/verify-license`
- `POST /api/heartbeat`
- `POST /api/logout`

Current `server.js` response shape:

- `payload`: RS256 JWT.
- `sid`: session id.
- `license`: contains status, tier, middleCode, skipHashCheck, modulePolicy.
- `cfg`: contains dataSpreadsheetId and updateChannel.
- `datahub`: contains baseUrl and manifest URLs.

Client parsing caveat:

- `LicenseApiService` parses tier from root `tier` or nested `license.tier`.
- `LicenseApiService` currently looks for `modulePolicy` at root, while `server.js` returns it under `license.modulePolicy`. Treat module update policy parsing as `NEED VERIFY`.

DataHub database access (resolved — the old `NEED VERIFY` no longer applies):

- `DataHubClient` calls **REST endpoints only**, never SQL functions. Verified call sites:
  `/api/v1/sites/{siteId}/lease/{acquire,renew,release}`, `/jms/ingest`, `/jms/observations`,
  `/changes`, `/projections/snapshot`.
- Schema lives in `backend/datahub/migrations/` (`001_core.sql` … `005_change_retention_floor.sql`),
  forward-only, each file recording its own `schema_migrations` marker inside its own transaction.
- Exactly one SQL function exists: `create_datahub_site(...)`, a provisioning helper called by
  `scripts/provision-site.ps1`. It is not client-callable. Do not add client-callable SQL
  functions — new behaviour belongs in an endpoint that can be authenticated and audited.

Security caveat:

- Firebase service-account material is held on Render only, never in this repo. If a
  `service_account.json` or `config-key.json` ever appears in the working tree, it is a mistake:
  do not commit it, and rotate the key if it left the trusted machine.

The older sections below are retained as reference. If they conflict with this baseline, use this baseline.

## Overview

AutoJMS uses four backend services:

| Service | Technology | Purpose |
|---------|-----------|---------|
| Render License Server | Node.js/Express | License verify, heartbeat, enrollment assertion |
| Firebase Realtime DB | Firebase Admin SDK | License data storage |
| DataHub API | ASP.NET Core net10.0 + Caddy | Enrollment, ingest, changes, lease, hub, manifests |
| DataHub PostgreSQL | PostgreSQL in Docker | Waybill projection and operational tables |

## Render License Server (server.js)

**Location**: `backend/render-license-server/server.js`
**Hosted at**: https://autojms-api.onrender.com
**Runtime**: Node.js with Express

### Environment Variables Required

```bash
JWT_PRIVATE_KEY                          # RS256 private key (PEM) for the session token
JWT_PUBLIC_KEY                           # RS256 public key (PEM)
DATAHUB_API_BASE_URL                     # https://dev.jmsauto.online
DATAHUB_CHANNEL                          # "production" | "staging"
DATAHUB_DEFAULT_SEATS                    # seat cap handed to enrollment
DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY    # RS256 PEM — signs the enrollment assertion
DATAHUB_LICENSE_ASSERTION_ISSUER         # must match the API's expected issuer
DATAHUB_LICENSE_ASSERTION_AUDIENCE       # must match the API's expected audience
DATAHUB_LICENSE_ASSERTION_TTL_SECONDS    # 300
FIREBASE_DATABASE_URL                    # Realtime DB URL
FIREBASE_SERVICE_ACCOUNT_BASE64          # or _JSON / _FILE
DEFAULT_UPDATE_CHANNEL                   # "stable" or "beta"
PORT                                     # 3000 (default)
VALID_EXE_HASHES                         # Comma-separated allowed exe hashes
```

Render never holds `DATAHUB_ADMIN_TOKEN` or a device token. It signs a short-lived assertion; the
client exchanges that assertion for a device token at `POST /api/v1/devices/enroll`.

### API Endpoints

#### POST /api/verify-license

Verify a license key and issue a JWT session token.

**Request:**
```json
{
  "licenseKey": "XXXX-XXXX-XXXX-XXXX",
  "hwid": "computed-hwid",
  "exeHash": "sha256-of-exe",
  "appVersion": "1.26.6"
}
```

**Response (success):**
```json
{
  "payload": "<jwt-token>",
  "sid": "<session-id>",
  "license": {
    "status": "active",
    "tier": "ULTRA",
    "middleCode": "0000",
    "skipHashCheck": true,
    "modulePolicy": {
      "autoUpdate": false,
      "silentUpdate": true,
      "applyOnNextStartup": true
    }
  },
  "cfg": {
    "dataSpreadsheetId": "",
    "updateChannel": "stable"
  },
  "datahub": {
    "baseUrl": "https://dev.jmsauto.online",
    "siteCode": "214A02",
    "licenseAssertion": "v1rs256....",
    "assertionExpiresAt": 1755900000,
    "manifests": {
      "versionLatest": ".../manifest/version-latest.json",
      "hashManifest": ".../manifest/hash-manifest.json",
      "tierDefinitions": ".../manifest/tier-definitions.json"
    }
  }
}
```

`licenseAssertion` lives ~300 s and is valid for `POST /api/v1/devices/enroll` and nothing else.
`siteCode` — not `siteId` — is what enrollment matches on.

#### POST /api/heartbeat

Maintain session and receive server commands.

**Request Header:** `Authorization: Bearer <jwt-token>`

**Response:**
```json
{ "action": "continue", "payload": "<new-jwt>" }
```
OR
```json
{ "action": "kill", "reason": "<revocation-reason>" }
```

#### POST /api/logout

Invalidate a session.

```json
{ "sid": "<session-id>" }
```

### JWT Token Structure

| Claim | Description |
|-------|-------------|
| key | License key |
| hwid | Hardware ID |
| sid | Session ID |
| tier | BASE or ULTRA |
| jti | Unique token ID (for replay prevention) |
| iss | "autojms-license-server" |
| aud | "autojms-desktop-client" |
| exp | 60 minutes |

## Firebase Realtime Database

**Location**: `backend/firebase/config-key.example.json` (bản mẫu tracked; bản thật
`backend/firebase/config-key.json` bị `.gitignore` theo luật `*-key.json` và chỉ nằm
ở máy owner). Schema có chú thích: `backend/firebase/license-key-schema.txt`.
**Database URL**: https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app

### Database Schema

```
Licenses/
  <license-key>/
    status: "active" | "revoked" | "expired"
    tier: "BASE" | "ULTRA"
    hwid: "<hardware-id>" | null
    activatedAt: "<ISO-8601 +07:00>"   # bản ghi cũ là epoch ms
    expiresAt: "<ISO-8601 +07:00>"     # vắng = vĩnh viễn
    middleCode: "<code>"
    skipHashCheck: true | false
    modulePolicy/
      autoUpdate: true | false
      silentUpdate: true | false
      applyOnNextStartup: true | false
    dataSpreadsheetId: "<id>" | ""
    updateChannel: "stable" | "beta"

sessions/
  <session-id>/
    licenseKey: "<key>"
    hwid: "<hwid>"
    tier: "BASE" | "ULTRA"
    status: "active"
    appVersion: "<version>"
    ip: "<client-ip>"
    createdAt: <timestamp>
    lastPing: <timestamp>
```

## DataHub API

**Base URL**: `https://dev.jmsauto.online` (Caddy → `api:8080` on the compose network)
**Contract**: `backend/datahub/openapi/datahub-v1.yaml`
**Source**: `src/AutoJMS.DataHub.Api/`

### Routes

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

## DataHub PostgreSQL

Postgres in Docker, private compose network only. No published host port, no direct client
access. Schema in `backend/datahub/migrations/`, twelve tables:

`sites`, `devices`, `waybill_scan_events`, `waybill_projections`, `dashboard_changes`,
`site_change_counters`, `site_fetch_leases`, `idempotency_records`, `audit_logs`,
`jms_event_policies`, `retention_policies`, `schema_migrations`.

`waybill_projections` is the current-state table the desktop app reads through
`/projections/snapshot`; `waybill_scan_events` is the append-only event log behind it.

## Manifest Control Plane

Small JSON served over HTTP from `DATAHUB_MANIFEST_BASE_URL`, which defaults to
`DATAHUB_API_BASE_URL`. Plain HTTP resources — no object store, no bucket, no vendor CLI.

```
https://dev.jmsauto.online/
├── manifest/
│   ├── app-manifest.json
│   ├── version-latest.json      # Control plane (which version/channel/provider)
│   ├── hash-manifest.json       # DLL hashes per version
│   └── tier-definitions.json    # Tier definitions
├── selector-updates/
│   ├── runtime-config.json
│   └── selector-update-manifest.json
└── configs/
    ├── public-config.json
    ├── runtime-policy.json
    ├── runtime-policy.base.json
    └── runtime-policy.ultra.json
```

Binaries never go here — `.nupkg`, `RELEASES`, and `Setup.exe` belong in GitHub Releases.

### manifest/version-latest.json

```json
{
  "schemaVersion": 1,
  "updatedAt": "2026-05-26T00:00:00+07:00",
  "channels": {
    "stable": {
      "version": "1.26.6",
      "displayVersion": "1.26.6",
      "internalBuild": "1.26.6.0",
      "velopackChannel": "stable",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
      "githubRepoUrl": "https://github.com/Datt03-sss/AutoJMS-Update",
      "tag": "v1.26.6-Release",
      "prerelease": false,
      "manualOnly": true,
      "mandatory": false
    },
    "beta": {
      "version": "1.26.6-beta.1",
      "displayVersion": "1.26.6 beta 1",
      "internalBuild": "1.26.6.1",
      "velopackChannel": "beta",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
      "githubRepoUrl": "https://github.com/Datt03-sss/AutoJMS-Update",
      "tag": "v1.26.6-beta.1-Release",
      "prerelease": true,
      "manualOnly": true,
      "mandatory": false
    }
  }
}
```

### manifest/hash-manifest.json

```json
{
  "schemaVersion": 1,
  "updatedAt": "2026-05-26T00:00:00+07:00",
  "versions": {
    "1.26.6": {
      "files": {
        "AutoJMS.dll": "<sha256-hash>"
      }
    }
  }
}
```

## GitHub Releases

**Repository**: Datt03-sss/AutoJMS-Update

### Release Tags

| Tag | Prerelease | Channel |
|-----|------------|---------|
| v1.26.6-Release | No | stable |
| v1.26.6-beta.1-Release | Yes | beta |

### Release Assets

| Asset | Description | Size |
|-------|-------------|------|
| AutoJMS-stable-Setup.exe | Velopack installer | ~100MB |
| AutoJMS.nupkg | Velopack package | ~100MB |
| RELEASES | Velopack index | ~1KB |

## Security Notes

### JWT Validation (Client)

The client validates the license JWT using:
- RS256 algorithm
- Hardcoded public key in `LicenseApiService.cs`
- Issuer: "autojms-license-server"
- Audience: "autojms-desktop-client"
- Clock skew: 2 minutes

### DataHub Device Token

Nothing is hardcoded. `DataHubClient.Configure(baseUrl, deviceToken, siteId)` receives the token
at runtime from the enrollment response, with an `AUTOJMS_DATAHUB_DEVICE_TOKEN` environment
override for local testing only. The token is HMAC-signed by the API, scoped to one site, and
expires in 24 h. Every log line masks it via `TokenRedactor.MaskToken` as `first4...last4`.

Three credentials exist and are never interchangeable — using the wrong one returns a bare `401`:

| Credential | Issued by | Lifetime | Valid for |
|---|---|---|---|
| Access token (RS256 JWT) | Render | 60 min | Render `/api/heartbeat`, `/api/datahub/license-assertion` |
| License assertion (`v1rs256.…`) | Render | 300 s | `POST /api/v1/devices/enroll`, nothing else |
| Device token (HMAC) | DataHub enroll | 24 h | every `/api/v1/sites/...` route and the hub |

### JMS AuthToken

The JMS session token (32-char hex) is:
- Stored in WebView2 localStorage
- Captured via request header interception
- Persisted in AutoJMS.json (LastAuthToken)
- Never transmitted to license server

