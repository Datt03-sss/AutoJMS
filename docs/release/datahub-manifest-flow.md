# DataHub Manifest Flow

## Overview

The DataHub API on the VPS serves the small control-plane JSON files the client reads at
startup. There is no object store and no bucket — the files are plain HTTP resources under
`DATAHUB_MANIFEST_BASE_URL`, which defaults to `DATAHUB_API_BASE_URL`
(`https://dev.jmsauto.online`).

## Files Hosted

| Object path | Size | Purpose |
|------|------|---------|
| `manifest/version-latest.json` | ~1KB | Per-channel version pointer |
| `manifest/hash-manifest.json` | ~1KB | DLL hashes |
| `manifest/tier-definitions.json` | ~1KB | Tier features |
| `manifest/app-manifest.json` | ~1KB | Module manifest |
| `configs/public-config.json` | ~1KB | Public runtime config |
| `configs/runtime-policy.json` | ~1KB | Shared runtime policy |
| `configs/runtime-policy.base.json` | ~1KB | BASE overrides |
| `configs/runtime-policy.ultra.json` | ~1KB | ULTRA overrides |
| `selector-updates/runtime-config.json` | ~1KB | Selector runtime config |
| `selector-updates/selector-update-manifest.json` | ~1KB | Selector update manifest |

Binaries never live here. `RELEASES`, `.nupkg`, and `Setup.exe` go to GitHub Releases.

## Layout

```
https://dev.jmsauto.online/
├── manifest/
│   ├── app-manifest.json
│   ├── version-latest.json
│   ├── hash-manifest.json
│   └── tier-definitions.json
├── selector-updates/
│   ├── runtime-config.json
│   └── selector-update-manifest.json
└── configs/
    ├── public-config.json
    ├── runtime-policy.json
    ├── runtime-policy.base.json
    └── runtime-policy.ultra.json
```

## Publish Process

```powershell
$base = "https://dev.jmsauto.online"

# 1. Fetch existing (to preserve the other channel)
$existing = Invoke-RestMethod "$base/manifest/version-latest.json"

# 2. Update the stable channel
$existing.channels.stable.version = "1.26.6"
$existing.channels.stable.displayVersion = "1.26.6"
$existing.channels.stable.internalBuild = "1.26.6.0"
$existing.channels.stable.velopackChannel = "stable"
$existing.channels.stable.tag = "v1.26.6-Release"
$existing.channels.stable.prerelease = $false

# Beta example
$existing.channels.beta.version = "1.26.6-beta.1"
$existing.channels.beta.displayVersion = "1.26.6 beta 1"
$existing.channels.beta.internalBuild = "1.26.6.1"
$existing.channels.beta.velopackChannel = "beta"
$existing.channels.beta.tag = "v1.26.6-beta.1-Release"
$existing.channels.beta.prerelease = $true

# 3. Publish — PUT is a full replace, so step 1 is not optional
Invoke-WebRequest -Method Put `
    -Uri "$base/api/v1/admin/manifests/manifest/version-latest.json" `
    -Headers @{ Authorization = "Bearer $env:DATAHUB_ADMIN_TOKEN" } `
    -ContentType "application/json" `
    -Body ($existing | ConvertTo-Json -Depth 10)
```

`PUT` replaces the whole object. Skipping the fetch in step 1 silently drops whichever channel
you did not set — that is the failure mode this flow exists to prevent.

> **Open gap.** `/api/v1/admin/manifests/{objectPath}` is not implemented in
> `src/AutoJMS.DataHub.Api`, is absent from `backend/datahub/openapi/datahub-v1.yaml`, and the
> `Caddyfile` has no static-file handler. The `PUT` above and `build-release.ps1 -Upload` both
> return 404 today. Until the endpoint lands, place the JSON on the VPS by hand. Do not work
> around it by pointing the client at a third-party host.

## Verify

```powershell
Invoke-RestMethod "https://dev.jmsauto.online/manifest/version-latest.json"
```

## Automation

`release/build-release.ps1 -Upload` performs the same `PUT` for every object path, reading the
token from `DATAHUB_ADMIN_TOKEN` and the base URL from `DATAHUB_MANIFEST_BASE_URL` (falling back
to `DATAHUB_API_BASE_URL`). The admin token is server-side only and must never appear in the
client, in Render's environment, or in any public JSON.
