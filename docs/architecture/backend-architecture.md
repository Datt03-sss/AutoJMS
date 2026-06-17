# Backend Architecture

## Current Verified Baseline

Backend responsibilities verified from current files:

- Render server: `backend/render-license-server/server.js`.
- Firebase: license/session/tier state used by the Render server via Firebase Admin SDK.
- Supabase Storage: public manifest/config/hash/tier/selector-update files under `infra/supabase/autojms-modules/`.
- Supabase PostgreSQL: C# client calls waybill tables/RPCs through `SupabaseDbService`.
- GitHub Releases: Velopack binary assets.

Important mismatches to preserve in audit:

- `server.js` returns `license.modulePolicy`, but `LicenseApiService` parses root `modulePolicy`. Effective module policy behavior is `NEED VERIFY`.
- `supabase-migration.sql` does not include current waybill/inventory RPC definitions used by the C# code.
- Checked-in hash manifest sample has a shape mismatch with `HashManifest.cs` expectation.

Use this baseline if older content below conflicts.

## Service Overview

```
┌────────────────────────────────────────────────────────────────────┐
│                      AUTOJMS BACKEND                               │
│                                                                    │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐      │
│  │  JMS Website │    │  License     │    │  Supabase   │      │
│  │  jtexpress.vn │    │  Server      │    │  (Storage)  │      │
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
  │  5. Return {payload: JWT, supabase: {...}}│
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

## Supabase (Waybill Data + Storage)

**URL**: https://valmbajjpkjccqslsuou.supabase.co

### PostgreSQL Tables

#### waybills

| Column | Type | Description |
|--------|------|-------------|
| waybill_no | TEXT PK | Tracking number |
| trang_thai_hien_tai | TEXT | Current status |
| thao_tac_cuoi | TEXT | Last operation |
| nguoi_thao_tac | TEXT | Operator |
| print_count | INT | Times printed |
| is_active | BOOL | Active tracking flag |
| last_tracked_at | TIMESTAMP | Last tracking time |

#### RPC Functions

| Function | Purpose |
|---------|---------|
| try_acquire_inventory_lease | Get exclusive sync lock |
| refresh_inventory_lease | Extend lock |
| release_inventory_lease | Release lock |
| upsert_new_waybills | Insert waybills (ON CONFLICT DO NOTHING) |
| merge_waybill_tracking_rows | Upsert tracking data |

### Storage Buckets

```
autojms-modules/
├── manifest/
│   ├── version-latest.json      ← Version control plane
│   ├── hash-manifest.json      ← DLL hashes
│   └── tier-definitions.json  ← Tier definitions
├── selector-updates/
│   └── selector-update-manifest.json
└── configs/
    └── runtime-config.json
```

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
| Large binaries (.nupkg, Setup.exe) | GitHub | >50MB Supabase limit |
| Small manifests (JSON) | Supabase Storage | <1KB each |

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

### Supabase Anon Key

Public read-only key only.
No write access from client.

### JMS AuthToken

32-char hex from JMS web session.
Not transmitted to license server.

