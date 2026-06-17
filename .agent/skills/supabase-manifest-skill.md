# Supabase Manifest Skill

## Overview

Supabase Storage hosts small manifest/config files. Large binaries go to GitHub Releases.

## Storage Structure

```
autojms-modules/
├── manifest/
│   ├── version-latest.json      # Which version/channel/provider
│   ├── hash-manifest.json      # DLL hashes per version
│   └── tier-definitions.json   # Tier definitions
├── selector-updates/
│   └── selector-update-manifest.json
└── configs/
    └── runtime-config.json
```

## Manifest URLs

```csharp
// From license response
SupabaseManifestService.Urls.VersionLatest      // manifest/version-latest.json
SupabaseManifestService.Urls.HashManifest      // manifest/hash-manifest.json
SupabaseManifestService.Urls.TierDefinitions    // manifest/tier-definitions.json
SupabaseManifestService.Urls.SelectorUpdate     // selector-updates/selector-update-manifest.json
```

## version-latest.json

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

## hash-manifest.json

```json
{
  "schemaVersion": 1,
  "versions": {
    "1.26.6": {
      "files": {
        "AutoJMS.dll": "a1b2c3d4..."
      }
    }
  }
}
```

## tier-definitions.json

```json
{
  "schemaVersion": 1,
  "tiers": {
    "BASE": {
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": [],
      "backgroundJobs": {
        "inventorySync": false,
        "databaseTracking": false,
        "autoSyncTimer": false
      }
    },
    "ULTRA": {
      "inherits": "BASE",
      "forms": [
        {
          "name": "FULLSTACK_OPERATION",
          "type": "VISIBLE_FORM"
        }
      ],
      "backgroundJobs": {
        "inventorySync": true,
        "databaseTracking": true,
        "autoSyncTimer": true
      }
    }
  }
}
```

## Client Usage

```csharp
var manifestSvc = new SupabaseManifestService(supabaseUrl, manifests);

// Fetch version latest
var latest = await manifestSvc.FetchVersionLatestAsync();
var channel = latest.GetChannel("stable");

// Check if github provider
if (channel.IsGithubProvider)
{
    // Use Velopack GithubSource
}
```

## Server Update Flow

```powershell
# Fetch existing to preserve other channels
$existing = Invoke-RestMethod https://.../version-latest.json

# Build new manifest (preserve beta, update stable)
$manifest = @{
    schemaVersion = 1
    channels = @{
        stable = @{ ... new data ... }
        beta = $existing.channels.beta  # Preserve
    }
}

# Upload
Invoke-RestMethod -Method Post `
    -Uri "https://.../manifest/version-latest.json" `
    -Headers @{ Authorization = "Bearer $SERVICE_ROLE_KEY"; "x-upsert" = "true" } `
    -ContentType "application/json" `
    -Body ($manifest | ConvertTo-Json -Depth 10)
```

## Upload Rules

1. Only upload small JSON to Supabase
2. Never upload .nupkg (> 50MB limit)
3. Preserve other channels when updating
4. Use x-upsert=true for updates
5. Service_role key only server-side
6. Keep `version` as Velopack SemVer; use `internalBuild` for four-part values

## Common Issues

| Issue | Cause | Solution |
|-------|--------|----------|
| No update found | version-latest.json not uploaded | Check upload script |
| Wrong version | Wrong channel in request | Check channel parameter |
| Upload fails | Wrong bucket/path | Verify bucket structure |
| 403 Forbidden | Missing/invalid key | Check service_role key |
