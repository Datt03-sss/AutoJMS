# DataHub Manifest Schema

## Current Verified Baseline

Verified local examples under `infra/datahub/autojms-modules/`.

The DataHub API is the control plane for these small files. They are plain HTTP resources under
`DATAHUB_MANIFEST_BASE_URL` (default `DATAHUB_API_BASE_URL`), not objects in a bucket:

- `manifest/app-manifest.json`
- `manifest/version-latest.json`
- `manifest/hash-manifest.json`
- `manifest/tier-definitions.json` or `.sec` references depending on file; exact production object is `NEED VERIFY`.
- `selector-updates/selector-update-manifest.json`
- `selector-updates/runtime-config.json`
- `configs/public-config.json`

Rules:

- Do not publish `.nupkg` through DataHub.
- Large Velopack assets belong in GitHub Releases.
- DataHub manifests must remain small JSON control files.
- `channels.*.version` is `VelopackVersion` and must be SemVer: stable `x.y.z`, beta `x.y.z-beta.n`.
- Four-part values belong in `channels.*.internalBuild`, not in `channels.*.version`.

Compatibility warnings:

- `HashManifest.cs` expects `versions[version].files["AutoJMS.dll"]`.
- The checked-in `infra/datahub/autojms-modules/manifest/hash-manifest.json` sample currently uses `versions[version].sha256`. Treat hash manifest compatibility as `NEED VERIFY`.
- Some docs/configs reference `tier-definitions.sec`; root app uses `tier-definitions.json`. Treat production tier manifest object as `NEED VERIFY`.

Older schema examples below are reference material.

## Base URL

```
https://dev.jmsauto.online/
```

## Object Paths

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
| datahub | DataHub API static JSON | Velopack SimpleWebSource |

`provider=github` is the only one used in production. `datahub` exists for an air-gapped
fallback and has never been exercised — treat it as `NEED VERIFY`.

## Publish Rules

1. Only publish small JSON (< 1MB).
2. Never publish `.nupkg` or `Setup.exe` — binaries go to GitHub Releases.
3. Fetch the current object first and preserve the other channel. `PUT` is a full replace, so
   skipping this silently drops whichever channel you did not set.
4. Publish with `PUT {base}/api/v1/admin/manifests/{objectPath}` and
   `Authorization: Bearer $DATAHUB_ADMIN_TOKEN`.
5. `DATAHUB_ADMIN_TOKEN` is server-side only — never in the client, never on Render, never in
   public JSON.

> **Open gap.** The admin route in rule 4 is not implemented yet: it is absent from
> `src/AutoJMS.DataHub.Api`, absent from `backend/datahub/openapi/datahub-v1.yaml`, and the
> `Caddyfile` has no static-file handler. Publishing returns 404 today; place the JSON on the
> VPS by hand until the endpoint lands.

