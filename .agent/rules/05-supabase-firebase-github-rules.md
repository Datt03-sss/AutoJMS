# Supabase, Firebase & GitHub Rules

## Service Overview

| Service | Technology | Purpose |
|---------|-----------|---------|
| Firebase | Realtime Database | License storage, session management |
| Supabase | PostgreSQL | Waybill database |
| Supabase Storage | S3-compatible | Manifest/config file storage |
| GitHub Releases | GitHub API | Velopack binary hosting |

## Supabase Rules

### Database (PostgreSQL)

**Used for**: Waybill tracking, inventory sync

**Key Tables:**
- `waybills` - Main waybill data
- RPC functions for locking and batch operations

**Connection:**
```csharp
// SupabaseDbService.cs
const SUPABASE_URL = "https://valmbajjpkjccqslsuou.supabase.co";
const SUPABASE_KEY = "eyJ..."; // anon key (public)
```

**Safety Rules:**
1. Only use anon key (read operations)
2. Never expose service_role key
3. Validate input before queries

### Storage (File Hosting)

**Buckets:**
- `autojms-modules` - Public bucket

**Folder Structure:**
```
autojms-modules/
├── manifest/
│   ├── version-latest.json
│   ├── hash-manifest.json
│   └── tier-definitions.json
├── selector-updates/
│   └── selector-update-manifest.json
└── configs/
    └── runtime-config.json
```

**Upload Rules:**
1. Only upload small JSON files to Supabase Storage
2. Never upload .nupkg (too large for free plan)
3. Use service_role key server-side only
4. Preserve other channels when updating version-latest.json
5. `version-latest.json` uses Velopack SemVer in `version`; four-part values belong only in `internalBuild`

### Manifest Files

#### version-latest.json

```json
{
  "schemaVersion": 1,
  "channels": {
    "stable": {
      "version": "1.26.6",
      "displayVersion": "1.26.6",
      "internalBuild": "1.26.6.0",
      "velopackChannel": "stable",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
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
      "tag": "v1.26.6-beta.1-Release",
      "prerelease": true,
      "manualOnly": true,
      "mandatory": false
    }
  }
}
```

#### hash-manifest.json

```json
{
  "schemaVersion": 1,
  "versions": {
    "1.26.6": {
      "files": {
        "AutoJMS.dll": "<sha256-hash>"
      }
    }
  }
}
```

### SupabaseManifestService Usage

```csharp
// Create service from license response
var manifestSvc = new SupabaseManifestService(
    result.SupabaseBaseUrl,
    result.Manifests);

// Fetch manifests
var latest = await manifestSvc.FetchVersionLatestAsync();
var hash = await manifestSvc.FetchHashManifestAsync();
var tier = await manifestSvc.FetchTierDefinitionsAsync();
var selector = await manifestSvc.FetchSelectorUpdateManifestAsync();
```

## Firebase Rules

### Purpose

License data storage and session management (server-side only).

### Database Structure

```
Licenses/
  <license-key>/
    status: "active" | "revoked"
    tier: "BASE" | "ULTRA"
    hwid: "<hardware-id>"
    skipHashCheck: true | false

sessions/
  <session-id>/
    licenseKey: "<key>"
    hwid: "<hwid>"
    status: "active"
    lastPing: <timestamp>
```

### Access Pattern

**Server only** (Render server.js):
```javascript
const serviceAccount = require("./serviceAccountKey.json");
admin.initializeApp({
  credential: admin.credential.cert(serviceAccount),
  databaseURL: "https://keyauthjms-default-rtdb..."
});

// Read license
const ref = admin.database().ref(`Licenses/${licenseKey}`);
const snap = await ref.once("value");
```

### Client Never Accesses Firebase Directly

Client accesses Firebase through:
1. License server API (verify/heartbeat)
2. No direct Firebase SDK in client code

## GitHub Rules

### Repository

**Repo**: `Datt03-sss/AutoJMS-Update`

**Purpose**: Host Velopack release binaries

### Release Assets

| Asset | Purpose | Size |
|-------|---------|------|
| RELEASES | Velopack index | ~1KB |
| AutoJMS.nupkg | Velopack package | ~100MB |
| *Setup.exe | Installer | ~100MB |

### Release Tag Format

```
v{VelopackVersion}-Release
```

Examples:
- Stable: `v1.26.6-Release`
- Beta: `v1.26.6-beta.1-Release`

### Uploading

```powershell
# Create release
gh release create v1.26.6-Release --title "v1.26.6"

# Upload assets
gh release upload v1.26.6-Release AutoJMS-1.26.6-stable-full.nupkg
gh release upload v1.26.6-Release *Setup.exe

# Set prerelease for beta
gh release create v1.26.6-beta.1-Release --title "v1.26.6 beta 1" --prerelease
```

### Velopack GithubSource

```csharp
// Use public repo, no token needed
var source = new GithubSource(
    "https://github.com/Datt03-sss/AutoJMS-Update",
    null,  // no token (public repo)
    prerelease: false,
    downloader: null
);
```

## Integration Patterns

### Version Check Flow

```
App startup
    ↓
License verify (server.js → Firebase)
    ↓
Get Supabase URLs from license response
    ↓
SupabaseManifestService.FetchVersionLatestAsync()
    ↓
Check provider=github
    ↓
Velopack GithubSource → GitHub Releases API
    ↓
Compare versions
```

### Update Flow

```
User clicks "Kiểm tra cập nhật"
    ↓
VelopackUpdateService.CheckAndUpdateAsync
    ↓
Read version-latest.json
    ↓
Use GithubSource for provider=github
    ↓
Download from GitHub
    ↓
Apply and restart
```

## Security Considerations

### Supabase

| Key Type | Usage | Risk |
|----------|-------|------|
| anon key | Client read | Low (read-only) |
| service_role key | Server upload | High (write access) |

**Rule**: Never expose service_role key in client code.

### Firebase

| Access | Via | Risk |
|---------|-----|------|
| Read license | Server API only | N/A |
| Write session | Server only | N/A |

**Rule**: Client never has Firebase credentials.

### GitHub

| Access | Token | Risk |
|--------|-------|------|
| Read releases | None (public) | Low |
| Upload releases | gh CLI auth | Medium |

## Manifest URLs

### Current Production URLs

```
# Supabase
https://valmbajjpkjccqslsuou.supabase.co/storage/v1/object/public/autojms-modules/manifest/version-latest.json

# GitHub
https://github.com/Datt03-sss/AutoJMS-Update/releases
```

### URL Structure in Client

```csharp
// From license response (cfg.supabase.manifests)
SupabaseManifestService.Urls.VersionLatest     // manifest/version-latest.json
SupabaseManifestService.Urls.HashManifest      // manifest/hash-manifest.json
SupabaseManifestService.Urls.TierDefinitions   // manifest/tier-definitions.json
SupabaseManifestService.Urls.SelectorUpdate   // selector-updates/selector-update-manifest.json
```
