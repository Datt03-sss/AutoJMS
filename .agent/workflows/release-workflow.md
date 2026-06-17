# Release Workflow

Use this workflow when building and releasing AutoJMS.

## Pre-Release Checklist

### Code

- [ ] All changes committed
- [ ] Version bumped in src/AutoJMS/AutoJMS.csproj
- [ ] Version bumped in tier-definitions.json (if needed)
- [ ] No TODO comments for critical features
- [ ] No debug logging in production code

### Testing

- [ ] Manual test on BASE tier
- [ ] Manual test on ULTRA tier
- [ ] Auth flow tested
- [ ] DKCH automation tested
- [ ] Print tested
- [ ] Update flow tested

### Security

- [ ] No secrets in code
- [ ] Token masking implemented (TODO)
- [ ] .NET Reactor applied (if configured)

## Build Steps

### Step 1: Clean Previous Build

```powershell
# Kill any running AutoJMS
Get-Process AutoJMS -ErrorAction SilentlyContinue | Stop-Process -Force

# Clean build folders
Remove-Item -Recurse -Force artifacts\publish\win-x64
Remove-Item -Recurse -Force release\output
```

### Step 2: Run Build

```powershell
.\build-release.ps1 -Version "1.26.6" -Channel stable
.\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta
```

### Step 3: Verify Build Output

```
release/output/stable/
├── AutoJMS-stable-Setup.exe  (~100MB)
├── AutoJMS.nupkg           (~100MB)
├── RELEASES                 (~1KB)
└── version-latest.json      (local copy)
```

### Step 4: Test Installer

1. Run AutoJMS-win-Setup.exe
2. Install to test location
3. Launch app
4. Test login
5. Test basic features

## Release Steps

### Step 1: Upload to GitHub

```powershell
.\build-release.ps1 -Version "1.26.6" -Channel stable -Upload
.\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta -Upload
```

### Step 2: Verify GitHub Release

- [ ] Release created with correct tag
- [ ] Assets uploaded (.nupkg, Setup.exe, RELEASES)
- [ ] Pre-release flag correct (beta only)

### Step 3: Update Supabase Manifest

```powershell
# build-release.ps1 does this automatically with -Upload
# Verify version-latest.json is updated
```

### Step 4: Verify Update Flow

1. Install old version
2. Open app
3. Click "Kiểm tra cập nhật"
4. Verify new version detected
5. Verify update works

## Post-Release

- [ ] Tag created in git
- [ ] Release notes written
- [ ] Version documented
- [ ] Announce to users

## Version Numbering

### Format

```
Stable VelopackVersion: major.minor.patch
Beta VelopackVersion:   major.minor.patch-beta.n
```

### Examples

- Stable: `VelopackVersion=1.26.6`, `DisplayVersion=1.26.6`, `InternalBuild=1.26.6.0`
- Beta: `VelopackVersion=1.26.6-beta.1`, `DisplayVersion=1.26.6 beta 1`, `InternalBuild=1.26.6.1`

### Rules

- Increment patch for bug fixes
- Increment minor for new features
- Increment major for breaking changes
- Do not use four-part values as `VelopackVersion`
- Do not convert `1.26.6.1` into `1.26.7`

## Rollback Plan

If release has critical issues:

1. Remove GitHub release assets
2. Delete Supabase manifest entry
3. Keep previous release active
4. Notify users of issue
5. Push hotfix release

