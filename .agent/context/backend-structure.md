# Backend Structure

## Current Verified Baseline

Verified from `backend/render-license-server/server.js`, `SupabaseDbService.cs`, `SupabaseManifestService.cs`, and storage manifest examples.

Backend roles:

- Render server: Node/Express license API.
- Firebase Realtime Database: license/session storage behind Render server.
- Supabase PostgreSQL: waybill database accessed by `SupabaseDbService`.
- Supabase Storage: public control-plane manifests/config/hash/tier/selector-update files.
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
- `supabase`: contains baseUrl and manifest URLs.

Client parsing caveat:

- `LicenseApiService` parses tier from root `tier` or nested `license.tier`.
- `LicenseApiService` currently looks for `modulePolicy` at root, while `server.js` returns it under `license.modulePolicy`. Treat module update policy parsing as `NEED VERIFY`.

Supabase database caveat:

- `SupabaseDbService` calls RPCs: `try_acquire_inventory_lease`, `refresh_inventory_lease`, `release_inventory_lease`, `complete_inventory_sync`, `upsert_new_waybills`, and `merge_waybill_tracking_rows`.
- The checked-in `supabase-migration.sql` only defines module/config/license tables and does not define these waybill RPCs. Database bootstrap SQL is incomplete or stored elsewhere: `NEED VERIFY`.

Security caveat:

- `service_account.json` exists in the workspace and contains Google service account material. Treat as sensitive and rotate if it left the trusted machine.

The older sections below are retained as reference. If they conflict with this baseline, use this baseline.

## Overview

AutoJMS uses four backend services:

| Service | Technology | Purpose |
|---------|-----------|---------|
| Render License Server | Node.js/Express | License verify, heartbeat |
| Firebase Realtime DB | Firebase Admin SDK | License data storage |
| Supabase PostgreSQL | PostgreSQL | Waybill tracking database |
| Supabase Storage | S3-compatible | Manifest/config file storage |

## Render License Server (server.js)

**Location**: `backend/render-license-server/server.js`
**Hosted at**: https://autojms-api.onrender.com
**Runtime**: Node.js with Express

### Environment Variables Required

```bash
JWT_PRIVATE_KEY       # RS256 private key (PEM)
JWT_PUBLIC_KEY        # RS256 public key (PEM)
SUPABASE_BASE_URL     # Supabase project URL
DEFAULT_UPDATE_CHANNEL # "stable" or "beta"
PORT                  # 3000 (default)
VALID_EXE_HASHES      # Comma-separated allowed exe hashes
```

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
  "supabase": {
    "baseUrl": "https://valmbajjpkjccqslsuou.supabase.co",
    "manifests": {
      "versionLatest": ".../manifest/version-latest.json",
      "hashManifest": ".../manifest/hash-manifest.json",
      "tierDefinitions": ".../manifest/tier-definitions.json"
    }
  }
}
```

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

**Location**: `infra/firebase/config-key.json`
**Database URL**: https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app

### Database Schema

```
Licenses/
  <license-key>/
    status: "active" | "revoked" | "expired"
    tier: "BASE" | "ULTRA"
    hwid: "<hardware-id>" | null
    activatedAt: <timestamp>
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

## Supabase PostgreSQL

**Project ID**: valmbajjpkjccqslsuou
**URL**: https://valmbajjpkjccqslsuou.supabase.co

### Tables

#### waybills (Postgrest)

| Column | Type | Description |
|--------|------|-------------|
| waybill_no | TEXT (PK) | Waybill tracking number |
| trang_thai_hien_tai | TEXT | Current status |
| thao_tac_cuoi | TEXT | Last operation |
| thoi_gian_thao_tac | TEXT | Last operation time |
| nguoi_thao_tac | TEXT | Operator |
| print_count | INT | Print count |
| is_active | BOOL | Active tracking |
| ... | ... | Many more columns |

#### RPC Functions

| Function | Purpose |
|---------|---------|
| try_acquire_inventory_lease | Get 30-min exclusive inventory sync lock |
| refresh_inventory_lease | Extend lease |
| release_inventory_lease | Release lock |
| complete_inventory_sync | Mark sync complete |
| upsert_new_waybills | Insert new waybills only (ON CONFLICT DO NOTHING) |
| merge_waybill_tracking_rows | Upsert tracking data |

## Supabase Storage

**Bucket**: autojms-modules
**Public URL pattern**: `https://valmbajjpkjccqslsuou.supabase.co/storage/v1/object/public/autojms-modules/`

### Bucket Structure

```
autojms-modules/
├── manifest/
│   ├── version-latest.json      # Control plane (which version/channel/provider)
│   ├── hash-manifest.json        # DLL hashes per version
│   └── tier-definitions.json    # Tier definitions
├── selector-updates/
│   └── selector-update-manifest.json  # Small selector updates
├── configs/
│   └── runtime-config.json       # Runtime config (encrypted)
└── releases/                    # Legacy (used by SimpleWebSource)
    └── stable/
```

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

### Supabase API Key

The Supabase anon key is hardcoded in `SupabaseDbService.cs`:
```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InZhbG1iYWpqcGtqY2Nxc2xzdW91Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3Nzg2MDM5OTMsImV4cCI6MjA5NDE3OTk5M30.dwuPB1nlzNpFdWYR4fuvTOP7w6wB8U4fWE0cW_rOJ-o
```

### JMS AuthToken

The JMS session token (32-char hex) is:
- Stored in WebView2 localStorage
- Captured via request header interception
- Persisted in AutoJMS.json (LastAuthToken)
- Never transmitted to license server

