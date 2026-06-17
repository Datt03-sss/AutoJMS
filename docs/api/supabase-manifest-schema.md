# Supabase Manifest Schema

## Current Verified Baseline

Verified local examples under `infra/supabase/autojms-modules/`.

Supabase Storage is the control plane for small files:

- `manifest/app-manifest.json`
- `manifest/version-latest.json`
- `manifest/hash-manifest.json`
- `manifest/tier-definitions.json` or `.sec` references depending on file; exact production object is `NEED VERIFY`.
- `selector-updates/selector-update-manifest.json`
- `selector-updates/runtime-config.json`
- `configs/public-config.json`

Rules:

- Do not upload `.nupkg` to Supabase.
- Large Velopack assets belong in GitHub Releases.
- Supabase Storage manifests should remain small JSON/control files.
- `channels.*.version` is `VelopackVersion` and must be SemVer: stable `x.y.z`, beta `x.y.z-beta.n`.
- Four-part values belong in `channels.*.internalBuild`, not in `channels.*.version`.

Compatibility warnings:

- `HashManifest.cs` expects `versions[version].files["AutoJMS.dll"]`.
- The checked-in `infra/supabase/autojms-modules/manifest/hash-manifest.json` sample currently uses `versions[version].sha256`. Treat hash manifest compatibility as `NEED VERIFY`.
- Some docs/configs reference `tier-definitions.sec`; root app uses `tier-definitions.json`. Treat production tier manifest object as `NEED VERIFY`.

Older schema examples below are reference material.

## Storage URL

```
https://valmbajjpkjccqslsuou.supabase.co/storage/v1/object/public/autojms-modules/
```

## Bucket: autojms-modules

### manifest/version-latest.json

Control plane - which version/channel/provider.

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
      "mandatory": false,
      "releaseNotes": "Stable release."
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
      "mandatory": false,
      "releaseNotes": "Beta test release."
    }
  }
}
```

### manifest/hash-manifest.json

DLL hashes per version.

```json
{
  "schemaVersion": 1,
  "updatedAt": "2026-05-26T00:00:00+07:00",
  "versions": {
    "1.26.6": {
      "files": {
        "AutoJMS.dll": "a1b2c3d4e5f6..."
      }
    },
    "1.26.5": {
      "files": {
        "AutoJMS.dll": "b2c3d4e5f6a1..."
      }
    }
  }
}
```

### manifest/tier-definitions.json

Tier feature definitions.

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
        "autoSyncTimer": false,
        "fullStackRealtime": false
      }
    },
    "ULTRA": {
      "inherits": "BASE",
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": [
        {
          "name": "FULLSTACK_OPERATION",
          "type": "VISIBLE_FORM",
          "launch": "AFTER_MAINFORM_SHOWN",
          "fetchApiAfterAuthToken": true
        }
      ],
      "backgroundJobs": {
        "inventorySync": true,
        "databaseTracking": true,
        "autoSyncTimer": true,
        "fullStackRealtime": true
      }
    }
  }
}
```

### selector-updates/selector-update-manifest.json

Small selector/runtime config updates.

```json
{
  "schemaVersion": 1,
  "version": "1.0.0",
  "autoApply": true,
  "manualOnly": false,
  "files": {
    "runtimeConfig": {
      "path": "configs/runtime-config.json",
      "sha256": "..."
    }
  }
}
```

## Provider Types

| Provider | Binary Source | Client Implementation |
|----------|--------------|---------------------|
| github | GitHub Releases | Velopack GithubSource |
| supabase | Supabase Storage | Velopack SimpleWebSource |

## Upload Rules

1. Only upload small JSON (< 1MB)
2. Never upload .nupkg (> 50MB limit)
3. Preserve other channels when updating
4. Use x-upsert=true for updates
5. Use service_role key server-side only

