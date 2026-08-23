# DataHub, Firebase & GitHub Rules

## Service Overview

| Service | Technology | Purpose |
|---------|-----------|---------|
| Firebase | Realtime Database | License storage, session management |
| Render | Node/Express | License verification, heartbeat, signs enrollment assertions |
| DataHub API | ASP.NET Core net10.0 on a VPS, behind Caddy | Device enrollment, JMS ingest, change feed, lease, SignalR hub, manifest JSON |
| DataHub PostgreSQL | Postgres in Docker, private network only | Waybill projection and operational tables |
| GitHub Releases | GitHub API | Velopack binary hosting |

There is no managed BaaS anywhere in this stack. No project ref, no storage bucket, no
PostgREST, no client-callable RPC, no row-level security, no `anon`/`authenticated` roles. If a
document, prompt, or generated snippet mentions any of those, it is describing a backend that no
longer exists — do not act on it, and fix the document.

## DataHub Rules

### HTTP surface

`https://dev.jmsauto.online`. Contract: `backend/datahub/openapi/datahub-v1.yaml`.

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

Adding a route means editing `src/AutoJMS.DataHub.Api/Endpoints/`, the OpenAPI file, and the
smoke test. A route that exists in only one of the three is a bug waiting to ship.

### Three credentials, never interchangeable

| Credential | Issued by | Lifetime | Valid for |
|---|---|---|---|
| Access token (RS256 JWT) | Render | 60 min | Render `/api/heartbeat`, `/api/datahub/license-assertion` |
| License assertion (`v1rs256.…`) | Render | 300 s | `POST /api/v1/devices/enroll`, nothing else |
| Device token (HMAC) | DataHub enroll | 24 h | every `/api/v1/sites/...` route and the hub |

Using the wrong one returns a bare `401`. This is the single most common integration mistake.

### Enrollment

```csharp
// LicenseApiService.VerifyLicenseSecureAsync exchanges the assertion for a device token.
// deviceName must be STABLE across launches — enrollment is idempotent on (site_id, name).
// A name that changes each run burns a seat per launch, then 409 SEAT_LIMIT_REACHED.
var deviceName = $"{Environment.MachineName}-{hwid[..8]}";
```

`siteId` comes from the enroll response, never from the Firebase license record — `siteId` in
Firebase is usually just the `middleCode`, which fails `Guid.TryParse` and leaves
`DataHubClient` unauthenticated.

Enrollment failure must not break license verification. The app still starts; it just runs
without cloud sync.

### Database

Forward-only migrations in `backend/datahub/migrations/`, applied by
`scripts/apply-migrations.sh --env-file` (container psql) or `apply-migrations.ps1 -DatabaseUrl`.
Each file records its own `schema_migrations` marker inside its own transaction.

Twelve tables: `sites`, `devices`, `waybill_scan_events`, `waybill_projections`,
`dashboard_changes`, `site_change_counters`, `site_fetch_leases`, `idempotency_records`,
`audit_logs`, `jms_event_policies`, `retention_policies`, `schema_migrations`.

Exactly one SQL function: `create_datahub_site(...)`, a provisioning helper called by
`scripts/provision-site.ps1`. It is not a client RPC. Do not add client-callable SQL functions —
new behaviour belongs in an endpoint where it can be authenticated, rate-limited, and audited.

**Migration safety:**

1. Never edit an applied migration. Add a new numbered file.
2. Never write a migration that does not record its own marker.
3. Database schema migrations are Protected Files (see `CLAUDE.md`) — owner request required.

### Manifest control plane

The client reads control-plane JSON from `DATAHUB_MANIFEST_BASE_URL`, defaulting to
`DATAHUB_API_BASE_URL`:

```
manifest/app-manifest.json
manifest/version-latest.json
manifest/hash-manifest.json
manifest/tier-definitions.json
configs/public-config.json
configs/runtime-policy.json
configs/runtime-policy.base.json
configs/runtime-policy.ultra.json
selector-updates/runtime-config.json
selector-updates/selector-update-manifest.json
```

**Publish rules:**

1. Small JSON only (< 1MB). Never `.nupkg`, `RELEASES`, or `Setup.exe`.
2. Fetch the current object first and preserve the other channel — `PUT` is a full replace.
3. `version-latest.json` uses Velopack SemVer in `version`; four-part values belong only in
   `internalBuild`.
4. `PUT {base}/api/v1/admin/manifests/{objectPath}` with `Authorization: Bearer $DATAHUB_ADMIN_TOKEN`.
5. `DATAHUB_ADMIN_TOKEN` is server-side only.

> **Open gap.** Rule 4's route is not implemented. It is absent from
> `src/AutoJMS.DataHub.Api`, absent from `openapi/datahub-v1.yaml`, and `Caddyfile` has no
> static-file handler, so `release/build-release.ps1 -Upload` returns 404. Publish by hand on
> the VPS until the endpoint lands. Never route the client at a third-party host to work around
> this.

### Client usage

```csharp
// Built from the license response, not hard-coded.
var manifestSvc = new VpsManifestService(result.DataHubBaseUrl, result.Manifests);

var latest   = await manifestSvc.FetchVersionLatestAsync();
var hash     = await manifestSvc.FetchHashManifestAsync();
var tier     = await manifestSvc.FetchTierDefinitionsAsync();
var selector = await manifestSvc.FetchSelectorUpdateManifestAsync();
```

### Realtime

`DataHubSyncService` connects to `/hubs/site`, joins group `site:{siteId}`, and handles the
server-invoked client method `change` carrying a `ChangeDoorbell`. The doorbell only signals
"something changed" — the sync loop still pulls `/changes` by `change_seq`. Losing the hub
degrades to polling; it must never lose data.

`Microsoft.AspNetCore.SignalR.Client` is an explicit `PackageReference`. It is not part of
`Microsoft.NETCore.App`, so with `SelfContained=true` a missing reference fails at runtime, not
at build time.

## Firebase Rules

### Purpose

License data storage and session management, server-side only.

### Database structure

```
Licenses/
  <license-key>/
    status: "active" | "revoked"
    tier: "BASE" | "ULTRA"
    hwid: "<hardware-id>"
    middleCode: "<office code>"
    skipHashCheck: true | false

sessions/
  <session-id>/
    licenseKey: "<key>"
    hwid: "<hwid>"
    status: "active"
    lastPing: <timestamp>
```

### Access pattern

Render server only:

```javascript
admin.initializeApp({
  credential: admin.credential.cert(serviceAccount),
  databaseURL: "https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/"
});

const snap = await admin.database().ref(`Licenses/${licenseKey}`).once("value");
```

The client never holds Firebase credentials and never links the Firebase SDK. It reaches
Firebase only through Render's verify/heartbeat endpoints.

Do not store update URLs or binary URLs in Firebase. Update control belongs to the DataHub
manifests and GitHub Releases.

## GitHub Rules

### Repository

`Datt03-sss/AutoJMS-Update` — hosts Velopack release binaries.

### Release assets

| Asset | Purpose | Size |
|-------|---------|------|
| `RELEASES` | Velopack index | ~1KB |
| `AutoJMS.nupkg` | Velopack package | ~100MB |
| `*Setup.exe` | Installer | ~100MB |

### Tag format

```
v{VelopackVersion}-Release
```

Stable: `v1.26.6-Release`. Beta: `v1.26.6-beta.1-Release`.

### Uploading

```powershell
gh release create v1.26.6-Release --title "v1.26.6"
gh release upload v1.26.6-Release AutoJMS-1.26.6-stable-full.nupkg
gh release upload v1.26.6-Release *Setup.exe

gh release create v1.26.6-beta.1-Release --title "v1.26.6 beta 1" --prerelease
```

### Velopack GithubSource

```csharp
var source = new GithubSource(
    "https://github.com/Datt03-sss/AutoJMS-Update",
    null,              // public repo, no token
    prerelease: false,
    downloader: null);
```

## Integration Patterns

### Startup flow

```
App startup
    ↓
POST /api/verify-license  (Render → Firebase)
    ↓
response: JWT + tier + apiBaseUrl + licenseAssertion
    ↓
POST /api/v1/devices/enroll  (assertion → device token + siteId)
    ↓
VpsManifestService.FetchVersionLatestAsync()
    ↓
provider=github → Velopack GithubSource → GitHub Releases API
    ↓
compare versions
```

### Sync flow (ULTRA only)

```
DataHubSyncService starts
    ↓
POST /api/v1/sites/{siteId}/lease/acquire
    ↓
leader: fetch from JMS → POST /jms/ingest with Idempotency-Key
    ↓
all stations: GET /changes?sinceSeq=<cursor>
    ↓
WS /hubs/site doorbell shortens the polling interval
    ↓
lease/renew on a timer; lease/release on shutdown
```

## Security Considerations

### DataHub

| Secret | Lives where | Risk if leaked |
|---|---|---|
| `DATAHUB_ADMIN_TOKEN` | `.env.production` on the VPS only | High — full admin surface |
| `DATAHUB_DEVICE_TOKEN_SIGNING_KEY` | VPS only | High — forge any device token |
| `DATAHUB_ENROLLMENT_PEPPER` | VPS only | High — device secret hashing |
| `DATAHUB_LICENSE_ASSERTION_PRIVATE_KEY` | Render only | High — enroll as any site |
| Device token | Client, 24h, per-site | Medium — scoped to one site |

Rules:

- Never put `DATAHUB_ADMIN_TOKEN` in client code, on Render, or in public JSON.
- Never enable `DATAHUB_ALLOW_STAGING_TEST_ISSUER` on production — it lets anyone mint their own
  enrollment assertion.
- Never publish the PostgreSQL port to the host.
- Mask every token in logs as `first4...last4`.

### Firebase

Client never has Firebase credentials. Read license and write session happen on Render only.

### GitHub

Reading releases needs no token (public repo). Uploading uses `gh` CLI auth.

## Manifest URLs

```
https://dev.jmsauto.online/manifest/version-latest.json
https://github.com/Datt03-sss/AutoJMS-Update/releases
```

```csharp
// From the license response (datahub.manifests)
manifestSvc.Urls.VersionLatest            // manifest/version-latest.json
manifestSvc.Urls.HashManifest             // manifest/hash-manifest.json
manifestSvc.Urls.TierDefinitions          // manifest/tier-definitions.json
manifestSvc.Urls.SelectorUpdateManifest   // selector-updates/selector-update-manifest.json
```
