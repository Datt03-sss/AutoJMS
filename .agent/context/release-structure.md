# Release Structure

## Release Pipeline Overview

```
┌──────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  dotnet publish   │────▶│  .NET Reactor    │────▶│   vpk pack       │
│  (self-contained) │     │  (optional)      │     │   (Velopack)    │
└──────────────────┘     └─────────────────┘     └────────┬────────┘
                                                          │
                                                          ▼
┌──────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Supabase Storage │◀────│  version-latest  │     │  GitHub Release │
│  (manifest only) │     │  .json upload    │     │  (binaries)     │
└──────────────────┘     └─────────────────┘     └─────────────────┘
```

## Binary Split Strategy

| Binary Type | Size | Hosting | Reason |
|-------------|------|---------|--------|
| RELEASES | ~1KB | GitHub Releases | Velopack index |
| .nupkg | ~100MB | GitHub Releases | Too large for Supabase free |
| Setup.exe | ~100MB | GitHub Releases | Too large for Supabase free |
| version-latest.json | ~1KB | Supabase Storage | Control plane |
| hash-manifest.json | ~1KB | Supabase Storage | DLL hashes |

## Build Steps (build-release.ps1)

### Step 1: Publish

```powershell
dotnet publish src/AutoJMS/AutoJMS.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:Version=$VelopackVersion `
    -o publish/win-x64
```

### Step 2: Verify Self-Contained

Asserts presence of:
- AutoJMS.exe, AutoJMS.dll
- AutoJMS.runtimeconfig.json
- AutoJMS.deps.json
- coreclr.dll, hostfxr.dll, hostpolicy.dll, System.Private.CoreLib.dll

### Step 3: .NET Reactor (Optional)

```powershell
D:\Cshap\.NET Reactor\dotNET_Reactor.Console.exe `
    -project tools/reactor/AutoJMS_Reactor.nrproj `
    -targetfile publish/win-x64/AutoJMS.dll
```

### Step 4: Velopack Pack

```powershell
vpk pack `
    --packId AutoJMS `
    --packVersion $VelopackVersion `
    --packDir publish/win-x64 `
    --mainExe AutoJMS.exe `
    --outputDir release/output/stable `
    --channel stable
```

### Step 5: Upload

#### GitHub (Large Binaries)

```powershell
gh release create v1.26.6-Release `
    --repo Datt03-sss/AutoJMS-Update `
    --title "v1.26.6 (stable)"
release/output/stable/*.nupkg
release/output/stable/*Setup.exe
release/output/stable/RELEASES
```

#### Supabase (Small Manifests)

```powershell
# Fetch existing version-latest.json to preserve other channel
Invoke-RestMethod https://valmbajjpkjccqslsuou.supabase.co/storage/v1/object/public/autojms-modules/manifest/version-latest.json

# Upload new version-latest.json
Invoke-RestMethod -Method Post `
    -Uri "https://valmbajjpkjccqslsuou.supabase.co/storage/v1/object/autojms-modules/manifest/version-latest.json" `
    -Headers @{ Authorization = "Bearer $SERVICE_ROLE_KEY"; "x-upsert" = "true" } `
    -ContentType "application/json" `
    -InFile version-latest.json
```

## Version Format

### VelopackVersion vs DisplayVersion vs InternalBuild

| Release | VelopackVersion | DisplayVersion | InternalBuild |
|---------|-----------------|----------------|---------------|
| Stable | `1.26.6` | `1.26.6` | `1.26.6.0` |
| Beta | `1.26.6-beta.1` | `1.26.6 beta 1` | `1.26.6.1` |

**Rule**: `VelopackVersion` is SemVer. Four-part values belong only in `InternalBuild`; do not convert `1.26.6.1` into `1.26.7`.

### Release Tags

| Channel | Tag Format | Prerelease |
|---------|-----------|------------|
| stable | v{VelopackVersion}-Release | No |
| beta | v{VelopackVersion}-Release | Yes |

## Inno Setup (First Install Only)

**Script**: `installer/inno/AutoJMS.iss`
**Output**: `AutoJMS-win-Setup.exe`

### Prerequisites Installed

1. .NET 8 Desktop Runtime (download if not present)
2. Microsoft Edge WebView2 Runtime (required, fatal if missing)
3. VC++ Redistributable (optional, non-fatal)

### Install Layout

```
C:\AutoJMS\
├── current\                    ← Velopack installs here
├── packages\                   ← Velopack cache
├── AppData\                   ← User data
│   ├── AutoJMS.json
│   ├── secure\
│   ├── logs\
│   ├── cache\
│   ├── BrowserData\
│   └── Downloads\
└── AutoJMS.exe                ← Velopack stub
```

### Velopack Setup Flow

1. Inno extracts VelopackSetup.exe to {tmp}
2. Runs: `VelopackSetup.exe --silent --installto "C:\AutoJMS"`
3. Velopack creates `current\` and installs app
4. Shortcuts point to root `AutoJMS.exe` (stub)

## Update Flow

### User-Initiated (tab About)

```
User clicks "Kiểm tra cập nhật"
    ↓
User chooses Stable or Beta
    ↓
VelopackUpdateService.CheckAndUpdateAsync with ExplicitChannel
    ↓
Read version-latest.json from Supabase
    ↓
If provider=github:
    Use Velopack GithubSource (no browser)
    ↓
    Check GitHub Releases API
    ↓
    If newer version:
        Show confirm dialog
        ↓
        User confirms
        ↓
        Download (progress shown)
        ↓
        PrepareForUpdateAsync (stop services)
        ↓
        ApplyUpdatesAndRestart
```

### PrepareForUpdateAsync Steps

1. Cancel _appCts (stops background tasks)
2. Stop _autoSyncTimer
3. Stop ZaloService auto-reminder
4. Close FullStackOperation form
5. Release Supabase inventory lease
6. Dispose WebView2 instances
7. Wait 800ms for settling

## Version Manifest Flow

### Client Reads Manifest

```
Program startup
    ↓
InitializeServicesFromLicense
    ↓
SupabaseManifestService created
    ↓
SmallUpdateService.CheckAndApplyAsync (background)
    ↓
MajorUpdateService.CheckForUpdateAsync (manual)
```

### Server Updates Manifest

```
Developer runs build-release.ps1 -Upload
    ↓
Fetch existing version-latest.json from Supabase
    ↓
Merge new channel data (preserve other channel)
    ↓
Upload to Supabase Storage
    ↓
Upload binaries to GitHub Release
```

## Directory Structure

### release/output/

```
release/
└── output/
    └── stable/
        ├── AutoJMS-stable-Setup.exe
        ├── AutoJMS.nupkg
        ├── RELEASES
        └── version-latest.json
```

### installer/inno/installer-output/

```
installer/inno/
└── installer-output/
    └── AutoJMS-win-Setup.exe
```

### publish/

```
publish/
└── win-x64/
    ├── AutoJMS.exe
    ├── AutoJMS.dll
    ├── AutoJMS.deps.json
    ├── AutoJMS.runtimeconfig.json
    └── [~2000 runtime files]
```

