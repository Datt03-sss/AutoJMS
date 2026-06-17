# Supabase Manifest Flow

## Overview

Supabase Storage hosts small control-plane manifests.

## Files Hosted

| File | Size | Purpose |
|------|------|---------|
| version-latest.json | ~1KB | Version control |
| hash-manifest.json | ~1KB | DLL hashes |
| tier-definitions.json | ~1KB | Tier features |

## Bucket

```
autojms-modules (public)
├── manifest/
│   ├── version-latest.json
│   ├── hash-manifest.json
│   └── tier-definitions.json
├── selector-updates/
│   └── selector-update-manifest.json
└── configs/
    └── runtime-config.json
```

## Upload Process

```powershell
# 1. Fetch existing (to preserve other channels)
$existing = Invoke-RestMethod `
    "https://valmbajjpkjccqslsuou.supabase.co/storage/v1/object/public/autojms-modules/manifest/version-latest.json"

# 2. Update stable channel
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

# 3. Upload
Invoke-RestMethod -Method Post `
    -Uri "https://valmbajjpkjccqslsuou.supabase.co/storage/v1/object/autojms-modules/manifest/version-latest.json" `
    -Headers @{
        Authorization = "Bearer $env:SUPABASE_SERVICE_ROLE_KEY"
        "x-upsert" = "true"
    } `
    -ContentType "application/json" `
    -Body ($existing | ConvertTo-Json -Depth 10)
```

## Manifest Update

Build script does this automatically with `-Upload` flag.
