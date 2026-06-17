# Update & Release Rules

## Update Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        SUPABASE STORAGE                     │
│  version-latest.json (control plane)                       │
│  hash-manifest.json (DLL hashes)                           │
│  selector-update-manifest.json (small updates)              │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                     AUTOJMS CLIENT                          │
│                                                             │
│  SmallUpdateService ──▶ Selector/config updates (auto)    │
│  MajorUpdateService ──▶ Version checks (manual)            │
│  VelopackUpdateService ──▶ Full app updates (manual)       │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼ (provider=github)
┌─────────────────────────────────────────────────────────────┐
│                      GITHUB RELEASES                        │
│  RELEASES, *.nupkg, *Setup.exe (large binaries)            │
└─────────────────────────────────────────────────────────────┘
```

## Update Types

### Type 1: Selector/Runtime Config Updates (Small)

| Aspect | Detail |
|--------|--------|
| Trigger | Auto after license verify |
| Mechanism | SmallUpdateService |
| Source | Supabase selector-updates/ |
| User action | None (auto) |
| Restart | No |
| Size | < 1MB |

**When to use:**
- CSS selector changes (WebView2 automation)
- Runtime config tweaks
- Small script updates

### Type 2: Major Version Updates

| Aspect | Detail |
|--------|--------|
| Trigger | Manual (user clicks About tab) |
| Mechanism | VelopackUpdateService |
| Source | GitHub Releases (provider=github) |
| User action | Confirm dialog |
| Restart | Yes (ApplyUpdatesAndRestart) |
| Size | ~100MB |

**When to use:**
- Breaking changes
- New features
- Major refactors

### Type 3: First Install

| Aspect | Detail |
|--------|--------|
| Mechanism | Inno Setup |
| Output | AutoJMS-win-Setup.exe |
| Bundles | VelopackSetup.exe |
| Prerequisites | .NET Runtime, WebView2, VC++ Redist |

## Velopack Rules

### Never Upload .nupkg to Supabase

Supabase free plan rejects files > 50MB.

```powershell
# WRONG
supabase storage cp AutoJMS.nupkg ss:///$bucket/releases/

# CORRECT
gh release upload v1.26.6-Release AutoJMS.nupkg
```

### GitHub Source Pattern

```csharp
// Read version-latest.json from Supabase
var latest = await manifestService.FetchVersionLatestAsync();

// If provider=github, use GithubSource
if (ch.IsGithubProvider)
{
    var source = new GithubSource(repoUrl, null, prerelease, null);
    var manager = new UpdateManager(source);
}

// Velopack reads GitHub API directly (no browser)
```

### Manual Update Only

```csharp
// tabAbout_btnCheckUpdate_Click
await updateSvc.CheckAndUpdateAsync(progress, _appCts.Token);

// CheckAndUpdateAsync:
// 1. Check for updates
// 2. Show confirm dialog
// 3. Download with progress
// 4. PrepareForUpdateAsync (stop services)
// 5. ApplyUpdatesAndRestart
```

## Inno Setup Rules

### Only for First Install

Inno Setup is for:
- First-time installation
- Reinstallation
- Uninstall
- Installing prerequisites

**NOT for:**
- Updates (Velopack handles this)
- Minor config changes

### Install Layout Must Match Velopack

```
C:\AutoJMS\
├── current\              ← Velopack installs here
├── packages\             ← Velopack cache
├── AppData\             ← User data (writable)
└── AutoJMS.exe         ← Velopack stub (shortcut target)
```

### Prerequisites Priority

1. **WebView2** - Required, fatal if missing
2. **.NET Runtime** - Self-contained, but installed if present
3. **VC++ Redist** - Optional, non-fatal

## Version Rules

### VelopackVersion vs DisplayVersion vs InternalBuild

| Field | Stable | Beta | Purpose |
|-------|--------|------|---------|
| `VelopackVersion` | `1.26.6` | `1.26.6-beta.1` | Technical SemVer for Velopack compare/update. |
| `DisplayVersion` | `1.26.6` | `1.26.6 beta 1` | User-facing label. |
| `InternalBuild` | `1.26.6.0` | `1.26.6.1` | Four-part assembly/file diagnostics. |

**Rule**: never convert `1.26.6.1` into `1.26.7`. Four-part values are internal metadata only and must not be used as `vpk pack --packVersion`.

### Version-latest.json Structure

```json
{
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
      "tag": "v1.26.6-beta.1-Release",
      "prerelease": true,
      "manualOnly": true,
      "mandatory": false,
      "releaseNotes": "Beta test release."
    }
  }
}
```

### Version Channel Rules

| Channel | Prerelease | Update Trigger |
|---------|------------|----------------|
| stable | false | Manual only |
| beta | true | Manual only |

**No automatic updates** - user must click About tab.

## Release Tag Format

```
v{VelopackVersion}-Release
```

Examples:
- `v1.26.6-Release` (stable)
- `v1.26.6-beta.1-Release` (beta)

## Release Checklist

### Before Building

- [ ] Update version in src/AutoJMS/AutoJMS.csproj
- [ ] Run tests
- [ ] Verify .NET Reactor path exists
- [ ] Check GitHub repo is accessible

### After Building

- [ ] Verify self-contained publish
- [ ] Test offline (without network)
- [ ] Test installer
- [ ] Verify Velopack update works

### After Release

- [ ] Upload to GitHub Release
- [ ] Update version-latest.json in Supabase
- [ ] Update hash-manifest.json if needed
- [ ] Test update from previous version

## Pre-Release Checklist

### Functional

- [ ] All tabs work (HOME, DKCH, TRACKING, PRINT, ABOUT)
- [ ] Login/license flow works
- [ ] AuthToken capture works
- [ ] DKCH automation works
- [ ] Print works
- [ ] Tracking works

### BASE vs ULTRA

- [ ] BASE: No background sync
- [ ] ULTRA: Background sync works
- [ ] ULTRA: FullStackOperation works

### Update Flow

- [ ] Velopack detects new version
- [ ] User can update from About tab
- [ ] Update does not open browser
- [ ] App restarts correctly

### Security

- [ ] No full tokens in logs
- [ ] No secrets in source code
- [ ] .NET Reactor applied (if configured)

