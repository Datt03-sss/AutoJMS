# DataHub Manifest Skill

## Overview

The DataHub API on the VPS serves the small control-plane JSON files the client reads at
startup. Large binaries go to GitHub Releases. There is no object store, no bucket, and no
vendor CLI — these are plain HTTP resources under `DATAHUB_MANIFEST_BASE_URL`, which defaults to
`DATAHUB_API_BASE_URL` (`https://dev.jmsauto.online`).

Full rules: [.agent/rules/05-datahub-firebase-github-rules.md](../rules/05-datahub-firebase-github-rules.md).

## Object Paths

```
https://dev.jmsauto.online/
├── manifest/
│   ├── app-manifest.json
│   ├── version-latest.json      # Which version/channel/provider
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

## Manifest URLs

```csharp
// From the license response (datahub.manifests), never hard-coded
manifestSvc.Urls.VersionLatest            // manifest/version-latest.json
manifestSvc.Urls.HashManifest             // manifest/hash-manifest.json
manifestSvc.Urls.TierDefinitions          // manifest/tier-definitions.json
manifestSvc.Urls.SelectorUpdateManifest   // selector-updates/selector-update-manifest.json
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

`version` must be Velopack SemVer (`x.y.z` or `x.y.z-beta.n`). Four-part values belong in
`internalBuild` only — `vpk pack` rejects `1.26.6.1` as a version.

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
        "autoSyncTimer": false,
        "fullStackRealtime": false
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
        "autoSyncTimer": true,
        "fullStackRealtime": true
      }
    }
  }
}
```

## Client Usage

```csharp
var manifestSvc = new VpsManifestService(result.DataHubBaseUrl, result.Manifests);

var latest = await manifestSvc.FetchVersionLatestAsync();
var channel = latest.GetChannel("stable");

if (channel.IsGithubProvider)
{
    // Velopack GithubSource against Datt03-sss/AutoJMS-Update
}
```

## Publish Flow

```powershell
$base = "https://dev.jmsauto.online"

# 1. Fetch the current object — PUT is a full replace
$existing = Invoke-RestMethod "$base/manifest/version-latest.json"

# 2. Update one channel, keep the other verbatim
$manifest = @{
    schemaVersion = 1
    channels = @{
        stable = @{ }              # new data
        beta   = $existing.channels.beta
    }
}

# 3. Publish
Invoke-WebRequest -Method Put `
    -Uri "$base/api/v1/admin/manifests/manifest/version-latest.json" `
    -Headers @{ Authorization = "Bearer $env:DATAHUB_ADMIN_TOKEN" } `
    -ContentType "application/json" `
    -Body ($manifest | ConvertTo-Json -Depth 10)
```

> **Open gap.** `/api/v1/admin/manifests/{objectPath}` is not implemented — it is absent from
> `src/AutoJMS.DataHub.Api`, absent from `backend/datahub/openapi/datahub-v1.yaml`, and
> `Caddyfile` has no static-file handler. Both the `PUT` above and
> `release/build-release.ps1 -Upload` return 404 today. Publish by hand on the VPS until the
> endpoint lands; never point the client at a third-party host to work around it.

## Publish Rules

1. Small JSON only (< 1MB).
2. Never publish `.nupkg`, `RELEASES`, or `Setup.exe` — those go to GitHub Releases.
3. Always fetch first and preserve the other channel.
4. `version` stays Velopack SemVer; four-part values go in `internalBuild`.
5. `DATAHUB_ADMIN_TOKEN` is server-side only — never in client code, never on Render, never in
   public JSON.

## Common Issues

| Issue | Cause | Solution |
|-------|--------|----------|
| No update found | `version-latest.json` was never published | `curl` the public URL; publish by hand |
| Wrong version | Wrong channel requested | Check the `channel` parameter and `updateChannel` in the license |
| Beta silently disappeared | Published without fetching first — `PUT` replaced the whole object | Always fetch and merge |
| Publish returns 404 | The admin route is not implemented yet | Place the JSON on the VPS by hand |
| Publish returns 401 | `DATAHUB_ADMIN_TOKEN` missing or wrong | Read it from the VPS `.env.production`; never commit it |
| `vpk pack` rejects the version | Four-part value or leading zero in `version` | Use `1.26.6`; put `1.26.6.1` in `internalBuild` |
