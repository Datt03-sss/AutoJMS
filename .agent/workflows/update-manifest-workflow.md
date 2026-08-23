# Update Manifest Workflow

Use this workflow when updating DataHub manifests.

## When to Update

- [ ] New version released
- [ ] Tier definitions changed
- [ ] Selector updates available
- [ ] Runtime config updated

## Manifest Types

### version-latest.json

Control plane - which version/channel/provider.

```powershell
# Fetch existing
$existing = Invoke-RestMethod https://datahub.example.com/manifest/version-latest.json

# Update stable channel
$existing.channels.stable = @{
    version = "1.26.6"
    displayVersion = "1.26.6"
    internalBuild = "1.26.6.0"
    velopackChannel = "stable"
    provider = "github"
    githubRepo = "Datt03-sss/AutoJMS-Update"
    githubRepoUrl = "https://github.com/Datt03-sss/AutoJMS-Update"
    tag = "v1.26.6-Release"
    prerelease = $false
    manualOnly = $true
    mandatory = $false
    releaseNotes = "Stable release."
}

$existing.channels.beta = @{
    version = "1.26.6-beta.1"
    displayVersion = "1.26.6 beta 1"
    internalBuild = "1.26.6.1"
    velopackChannel = "beta"
    provider = "github"
    githubRepo = "Datt03-sss/AutoJMS-Update"
    githubRepoUrl = "https://github.com/Datt03-sss/AutoJMS-Update"
    tag = "v1.26.6-beta.1-Release"
    prerelease = $true
    manualOnly = $true
    mandatory = $false
    releaseNotes = "Beta test release."
}

# Publish — PUT is a full replace, so the fetch above is not optional
Invoke-WebRequest -Method Put `
    -Uri "https://dev.jmsauto.online/api/v1/admin/manifests/manifest/version-latest.json" `
    -Headers @{ Authorization = "Bearer $env:DATAHUB_ADMIN_TOKEN" } `
    -ContentType "application/json" `
    -Body ($existing | ConvertTo-Json -Depth 10)
```

> The admin route is not implemented yet, so this returns 404. Place the JSON on the VPS by
> hand until it lands — see [.agent/skills/datahub-manifest-skill.md](../skills/datahub-manifest-skill.md).

### hash-manifest.json

DLL hashes per version.

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

### tier-definitions.json

Tier feature definitions.

```json
{
  "tiers": {
    "BASE": { ... },
    "ULTRA": { ... }
  }
}
```

## Update Process

### Step 1: Fetch Current

```powershell
$current = Invoke-RestMethod https://.../manifest/version-latest.json
```

### Step 2: Modify

```powershell
# Update channel
$current.channels.stable.version = "1.26.6"
$current.channels.stable.displayVersion = "1.26.6"
$current.channels.stable.internalBuild = "1.26.6.0"
$current.channels.stable.velopackChannel = "stable"
$current.channels.stable.tag = "v1.26.6-Release"
```

### Step 3: Upload

```powershell
# Preserve other channels
Invoke-RestMethod -Method Post ... -Body ($current | ConvertTo-Json)
```

## Testing

- [ ] Manifest accessible via URL
- [ ] JSON valid
- [ ] Version matches release
- [ ] Provider correct (github/datahub)
- [ ] Other channels preserved

## Common Issues

| Issue | Solution |
|-------|----------|
| Other channels lost | Fetch before upload |
| Invalid JSON | Validate before upload |
| Wrong version | Double-check version |
| Provider wrong | Set to github or datahub |
