# AutoJMS Codebase Audit

> Status 2026-06-03: the project structure has been migrated after this audit was first created. For current file locations, use docs/migration/PROJECT_STRUCTURE_MIGRATION_REPORT.md and docs/migration/FILE_MOVE_MAP.md.

## Current Verification Addendum

**Date**: 2026-06-03  
**Scope**: Documentation/agent context audit only. No production code changes.

This addendum supersedes older sections below where there is a conflict.

### Verified Project Facts

- AutoJMS is a .NET 8 WinForms desktop application.
- UI stack: WinForms + SunnyUI + WebView2.
- Main project: `src/AutoJMS/AutoJMS.csproj`, target `net8.0-windows`, self-contained `win-x64`.
- BASE tabs: `HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`.
- ULTRA adds `FullStackOperation` as a separate form, not a tab.
- `FullStackOperation` launch is gated by `TierRuntimePolicy.EnableFullStackOperation`; BASE typing `DASH` is expected to be blocked.
- Firebase is used by `backend/render-license-server/server.js` for license/session/tier data through Firebase Admin SDK.
- Render server exposes `/api/verify-license`, `/api/heartbeat`, `/api/logout`, and `/health`.
- Supabase Storage hosts manifests/config/hash/tier/selector-update control files.
- Supabase PostgreSQL is used by `SupabaseDbService` for waybill and inventory sync data.
- GitHub Releases host large Velopack binary assets; `.nupkg` must not be uploaded to Supabase.
- Inno Setup is used for first install/reinstall/uninstall and runtime prerequisites.
- Velopack is used for in-app updates. Major update flow is user-triggered from the About tab.
- Small selector/runtime config updates can be auto-applied through `selector-update-manifest`.

### Verified Build State

Command:

```powershell
dotnet build src/AutoJMS/AutoJMS.csproj -c Debug
```

Result: succeeded after the build-fix patch on 2026-06-03.

Root cause:

- Historical failure: `src/AutoJMS/AutoJMS.csproj` included missing root content files under `modules\`.
- Fixed by adding `Condition="Exists('...')"` to the root `modules/*.json` `Content Include` entries.
- No default module JSON files or runtime code changes were added.

Warnings observed:

- `PdfiumViewer 2.13.0` is restored from .NET Framework assets and may not be fully compatible with `net8.0-windows`.
- `GoogleCredential.FromJson` and `GoogleCredential.FromStream` are obsolete.
- Nullable annotations exist while nullable is disabled.
- Some Google Sheet async calls are not awaited.

### Verified Security Issues

- Full JMS auth token logging exists in `Main.cs` and `JmsAuthTokenService.cs`. Production logs must mask tokens.
- `service_account.json` exists in the workspace and contains sensitive Google service account material. Rotate if exposed.
- Supabase anon key is hardcoded in `SupabaseDbService.cs`; RLS and RPC permissions must be verified.
- Dynamic module update trust is incomplete: hash checks exist, but signature enforcement is inconsistent/optional.
- `SettingsManager` and `UserSettingsService` split encrypted/plain settings paths; token storage behavior is `NEED VERIFY`.

### Verified Architecture Gaps

- `supabase-migration.sql` does not define the waybill/inventory RPCs used by `SupabaseDbService`.
- `server.js` returns `license.modulePolicy`, while `LicenseApiService` parses root `modulePolicy`; effective module policy parsing is `NEED VERIFY`.
- Checked-in `hash-manifest.json` shape does not match the `HashManifest` DTO expectation of `versions[version].files["AutoJMS.dll"]`; current integrity manifest compatibility is `NEED VERIFY`.

### Non-Negotiable Rules

- Do not let BASE run background inventory/database sync.
- Do not open GitHub web pages during update; use Velopack `GithubSource`.
- Do not access WebView2 outside the UI thread.
- Do not log full JMS auth token in production.
- Do not upload `.nupkg` to Supabase.

**Date**: 2026-06-03
**Auditor**: AI Codebase Auditor
**Project**: AutoJMS - Logistics Automation Desktop App

---

## Executive Summary

AutoJMS is a **.NET 8 WinForms desktop application** for logistics automation in Vietnam. It uses WebView2 for browser automation, Firebase for license management, Supabase for waybill database, and Velopack/GitHub for updates.

The codebase has been through multiple refactors (Firebase license, Render server, Supabase manifests, GitHub Releases, Inno Setup, WebView2 automation, BASE/ULTRA tiers, FullStackOperation form). The structure reflects this history with some complexity.

---

## 1. Solution/Project Map

### Solution File

```
AutoJMS.slnx (JSON format)
├── src/AutoJMS/AutoJMS.csproj
└── src\AutoJMS.Abstractions\AutoJMS.Abstractions.csproj
```

### Project File

```
AutoJMS.csproj
├── TargetFramework: net8.0-windows
├── RuntimeIdentifier: win-x64
├── SelfContained: true
├── Nullable: disable
├── UseWindowsForms: true
├── Version: 1.26.6
├── Velopack: 0.0.1297
├── SunnyUI: 3.9.6
├── WebView2: 1.0.3912.50
└── supabase-csharp: 0.16.2
```

### ModuleProjects

```
archive/old-module-system/
├── AutoJMS.Abstractions/      ← Referenced by main project
├── AutoJMS.RetryPolicy/
└── AutoJMS.Selectors/
```

### ModuleSystem

```
ModuleSystem/
├── ActiveModules.cs
├── AppManifest.cs
├── BuiltInModules.cs
├── ModuleLoadContext.cs
├── ModuleRegistry.cs
├── ModulesManifest.cs
├── ModuleStartup.cs
├── ModuleUpdater.cs
├── SupabaseModuleProvider.cs
└── VersionRange.cs
```

### Server Structure

```
ServerStructure/
├── Firebase/
│   └── config-key.json         ← Sample Firebase config
├── Render server/
│   └── server.js               ← License server (Node.js)
├── Supabase/
│   └── autojms-modules/
│       ├── manifest/
│       ├── selector-updates/
│       └── configs/
└── Github release/
    ├── v1.26.x-release/
    └── v1.26.x-beta-release/
```

### Installer

```
installer/inno/
├── AutoJMS.iss                ← Inno Setup script
├── build-installer.ps1
├── build-installer.bat
├── clean-test-install.bat
├── README_INSTALLER.md
└── installer-output/          ← Build output
    └── AutoJMS-win-Setup.exe
```

### Release

```
release/
├── build-release.ps1          ← Main release script
├── build-release.bat
├── upload-only.bat
├── _unlock_now.ps1
└── output/
    └── stable/
        ├── AutoJMS-stable-Setup.exe
        ├── AutoJMS.nupkg
        └── RELEASES
```

### Documentation

```
docs/                          ← NEW (created during audit)
├── README.md
├── architecture/
│   ├── system-overview.md
│   ├── client-architecture.md
│   ├── backend-architecture.md
│   ├── tier-architecture.md
│   ├── auth-token-architecture.md
│   └── fullstack-operation-architecture.md
├── api/
│   ├── render-server-api.md
│   ├── firebase-license-schema.md
│   ├── jms-api-notes.md
│   └── supabase-manifest-schema.md
├── release/
│   ├── release-overview.md
│   ├── inno-first-install.md
│   ├── velopack-update.md
│   ├── github-release-flow.md
│   ├── supabase-manifest-flow.md
│   └── versioning-rules.md
├── migration/
│   └── current-to-clean-structure-plan.md
├── troubleshooting/
│   ├── auth-token-401.md
│   ├── webview2-issues.md
│   ├── velopack-setup-errors.md
│   ├── supabase-manifest-errors.md
│   └── fullstack-operation-errors.md
└── audit/
    └── CODEBASE_AUDIT.md      ← This file
```

---

## 2. Entry Points

### Program.cs (Main Entry)

**Lines**: 1-598

**Flow**:
```
1. VelopackApp.Build().Run()           ← Initialize Velopack
2. AppPaths.EnsureDirectories()       ← Create writable dirs
3. GetHWID()                         ← Compute hardware ID
4. AppConfig.LoadBootstrap()          ← Load encrypted config
5. License verification (offline-first)
   ├── Read local cache (DPAPI encrypted)
   ├── Online: LicenseApiService.VerifyLicenseSecureAsync()
   └── Offline: Use cached key if exists
6. InitializeServicesFromLicense()
   ├── SupabaseManifestService
   ├── RuntimeConfigService
   ├── IntegrityService
   ├── MajorUpdateService
   └── SmallUpdateService
7. Start background services
   ├── uiControlService (network monitor)
   ├── HeartbeatSupervisor (license heartbeat)
   ├── ModuleSystem initialization
   └── HashVerifier (async integrity check)
8. Application.Run(new Main(sessionTier))
```

### Main.cs (Main Form)

**Lines**: 1-2040

**Constructor Flow**:
```
1. TierRuntimePolicy.Resolve(tier)
2. InitializeComponent() [Designer]
3. Register tabs with TabManager
4. Apply tier restrictions
5. Setup WebView2 CreationProperties (shared BrowserData)
6. Start autoSyncTimer (ULTRA only)
```

**OnLoad Flow**:
```
1. InitNetworkUI()
2. Ensure WebView2 instances
3. Navigate to JMS URLs
4. Initialize services
5. DkchManager.StartDaemon()
6. Refresh auth token
7. Run startup sync (ULTRA only)
```

**OnShown Flow**:
```
1. PreCreateFullStackForm() (ULTRA only)
   └── Form created but hidden
```

### Login/License Flow

```
frmLogin.ShowDialog()
    ↓
LicenseApiService.VerifyLicenseSecureAsync(key, hwid)
    ↓
Validate JWT (RS256 with hardcoded public key)
    ↓
Parse tier, Supabase URLs, manifests
    ↓
InitializeServicesFromLicense()
    ↓
Save local cache (DPAPI encrypted)
```

### Update Flow

```
tabAbout_btnCheckUpdate_Click
    ↓
VelopackUpdateService.CheckAndUpdateAsync()
    ↓
SupabaseManifestService.FetchVersionLatestAsync()
    ↓
provider=github → GithubSource
    ↓
Check GitHub Releases API
    ↓
User confirms → Download → PrepareForUpdateAsync → ApplyUpdatesAndRestart
```

---

## 3. UI/Tabs Map

### Main Form Tabs

| Tab | File | BASE | ULTRA | Purpose |
|-----|------|:----:|:-----:|---------|
| HOME | Main.cs | ✓ | ✓ | JMS WebView2 browser |
| DKCH | Main.cs | ✓ | ✓ | Return shipment registration |
| TRACKING | Main.cs | ✓ | ✓ | Waybill tracking |
| PRINT | Main.cs | ✓ | ✓ | Label printing |
| ABOUT | Main.cs | ✓ | ✓ | Version, update |

### FullStackOperation Form

**File**: `FullStackOperation.cs` (~2500 lines)

**Tiers**: ULTRA only (not a tab, separate form)

**Launch**: Pre-created in Main.OnShown, shown when user types "DASH" in HOME URL bar

**Tabs**:
| Tab | Purpose | Data Source |
|-----|---------|-------------|
| tabDash | Dashboard | Supabase waybills |
| tabChat | Zalo integration | ZaloChatService |
| tabThoiHieu | SLA monitoring | Supabase waybills |

---

## 4. Services Map

### License/Auth Services

| Service | File | Responsibility |
|---------|------|----------------|
| LicenseApiService | LicenseApiService.cs | License verify, heartbeat |
| JmsAuthStateService | JmsAuthStateService.cs | Token state management |
| JmsAuthTokenService | JmsAuthTokenService.cs | Token resolution, 401 handling |
| AuthStateService | AuthStateService.cs | Token event publishing |

### API Services

| Service | File | Responsibility |
|---------|------|----------------|
| JmsApiClient | JmsApiClient.cs | JMS HTTP API calls |
| InventorySyncService | InventorySyncService.cs | Fetch inventory from JMS |
| WaybillTrackingService | ITrackingService.cs | Waybill tracking |

### Update Services

| Service | File | Responsibility |
|---------|------|----------------|
| VelopackUpdateService | VelopackUpdateService.cs | In-app updates |
| SmallUpdateService | SmallUpdateService.cs | Selector/config updates |
| MajorUpdateService | MajorUpdateService.cs | Major version updates |
| HashVerifier | HashVerifier.cs | DLL integrity check |

### Data Services

| Service | File | Responsibility |
|---------|------|----------------|
| SupabaseDbService | SupabaseDbService.cs | Waybill database |
| GoogleSheetService | GoogleSheetService.cs | Google Sheets integration |

### UI Services

| Service | File | Responsibility |
|---------|------|----------------|
| DkchManager | (in Main.cs) | DKCH automation |
| PrintService | PrintService.cs | Label printing |
| ZaloChatService | ZaloChatService.cs | Zalo integration |
| WebViewAutomation | WebViewAutomation.cs | Browser automation |
| WebViewHost | WebViewHost.cs | WebView2 hosting |

### Infrastructure Services

| Service | File | Responsibility |
|---------|------|----------------|
| AppConfig | AppConfig.cs | Runtime configuration |
| AppPaths | AppPaths.cs | Path management |
| AppLogger | AppLogger.cs | Logging |
| SettingsManager | SettingsManager.cs | User settings |
| TierRuntimePolicy | TierRuntimePolicy.cs | Tier enforcement |
| TierDefinitions | TierDefinitions.cs | Tier definitions |

---

## 5. Background Jobs Map

### ULTRA-Only Jobs

| Job | Trigger | Location | Notes |
|-----|---------|----------|-------|
| _autoSyncTimer | Every 1 sec | Main.cs:165-174 | Triggers HandleAutoSyncTickAsync |
| RunStartupSyncAsync | On load | Main.cs:354-362 | Runs inventory sync |
| ExecuteSyncWorkflowAsync | Timer/Manual | Main.cs:416-472 | Full sync workflow |
| InventorySyncService.FetchAll... | Sync workflow | InventorySyncService.cs:139 | Fetch waybills |
| DatabaseTracking.RunBackground... | New waybills | DatabaseTracking.cs | Track new waybills |
| FullStackOperation timers | After auth | FullStackOperation.cs | Auto-refresh |

### BASE Jobs (All Manual)

| Job | Trigger | Location |
|-----|---------|----------|
| Manual tracking | btnSearch_Click | Main.cs:1509 |
| Manual print | tabPrint_btnPrint | Main.cs:1745 |
| DKCH | btnDKCH1/DKCH2 | Main.cs:1324, 1348 |

### Tier Policy Enforcement

```csharp
// Main.cs:165-174
if (_tierPolicy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}

// Main.cs:354-362
if (_tierPolicy.EnableStartupInventorySync || 
    _tierPolicy.EnableStartupDatabaseTracking)
{
    _ = RunStartupSyncAsync(_appCts.Token);
}
```

### KNOWN RISK: Verify BASE has no background jobs

**Current status**: TierRuntimePolicy appears correctly implemented.

---

## 6. Config/Data Map

### Local Files

| File | Location | Encrypted | Contents |
|------|----------|----------|----------|
| AutoJMS.json | AppData\ | No | User settings, LastAuthToken |
| AutoJMS.secure | AppData\secure\ | Yes (AES) | Runtime config |
| license.dat | AppData\secure\ | Yes (DPAPI) | License key |
| debug.log | AppData\logs\ | No | Application logs |
| BrowserData\ | AppData\BrowserData\ | No | WebView2 state |
| AutoJMS.json (template) | InstallDir\ | No | Default settings |

### Secure Storage

Encryption method: AES-CBC-HMACSHA256 (SecureConfigCrypto.cs)

Secret derivation: MachineName|UserName|HWID|AutoJMS

### tier-definitions.json

Shipped with app, loaded at runtime for tier enforcement.

### Module Files

```
modules/                       ← In InstallDir (read-only shipped)
├── app-manifest.json
├── active_modules.json
├── modules-cache.json
├── selectors.json
├── config.json
└── tier-definitions.json
```

---

## 7. release/Deployment Map

### Binary Split

| Type | Size | Host |
|------|------|------|
| .nupkg | ~100MB | GitHub Releases |
| *Setup.exe | ~100MB | GitHub Releases |
| RELEASES | ~1KB | GitHub Releases |
| version-latest.json | ~1KB | Supabase Storage |

### Build Flow

```
1. dotnet publish -c Release -r win-x64 --self-contained
2. .NET Reactor (optional)
3. vpk pack --packId AutoJMS
4. Upload binaries → GitHub Release
5. Upload manifest → Supabase Storage
```

### Release Scripts

| Script | Purpose |
|--------|---------|
| release/build-release.ps1 | Full build + upload |
| installer/inno/build-installer.ps1 | Inno Setup build |

### Velopack Configuration

```
version-latest.json:
{
  "channels": {
    "stable": {
      "version": "1.26.6",
      "displayVersion": "1.26.6",
      "internalBuild": "1.26.6.0",
      "velopackChannel": "stable",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
      "tag": "v1.26.6-Release"
    }
  }
}
```

---

## 8. Known Risky Areas

### High Priority

#### 1. Full Token Logging (TODO)

**Location**: Main.cs:1016, JmsAuthTokenService.cs:107

**Issue**: Full 32-hex authToken logged to debug.log.

```csharp
// Main.cs:1016
AppLogger.Info($"Auth token captured from {source} (len={token.Length}), authToken={token}");

// TODO exists: "TODO(before-release): mask token in logs"
```

**Risk**: Exposed token could be used to access JMS API.

**Status**: TODO - needs masking before release.

#### 2. WebView2 Automation Fragility

**Location**: WebViewAutomation.cs

**Issue**: CSS selectors and JS injection scripts break if JMS frontend changes.

**Risk**: DKCH automation fails silently.

**Mitigation**: Error classification (NeedSwitchToDkch1/2Exception).

#### 3. Supabase API Key Exposure

**Location**: SupabaseDbService.cs:17

**Issue**: Supabase anon key hardcoded in source.

```csharp
private const string SUPABASE_KEY = "eyJ...";
```

**Risk**: Anyone can read public Supabase data.

**Mitigation**: This is the public anon key for read-only access.

### Medium Priority

#### 4. BASE Tier Background Jobs

**Location**: Main.cs:165-174, 354-362

**Issue**: If TierRuntimePolicy.Resolve() returns wrong flags, BASE could run background jobs.

**Current status**: Guards appear correctly implemented.

#### 5. FullStackOperation Lifecycle

**Location**: Main.cs:866-925, FullStackOperation.cs

**Issue**: Form pre-created but hidden. Closing/re-showing must work correctly.

**Risk**: Memory leak, race conditions.

#### 6. DataGridView Performance

**Location**: Main.cs, FullStackOperation.cs

**Issue**: DataGridView with thousands of rows can freeze UI.

**Current mitigations**: Double buffering, pagination (1000 rows/page).

### Low Priority

#### 7. Module System Complexity

**Location**: ModuleSystem/, archive/old-module-system/

**Issue**: Multiple layers of module loading.

#### 8. Velopack Update Without Admin Rights

**Location**: AutoJMS.iss

**Issue**: Installer requires admin, but Velopack needs write to install dir.

**Current mitigation**: `users-modify` permissions on dirs.

#### 9. .NET Reactor Integration

**Location**: src/AutoJMS/AutoJMS.csproj:86-94

**Issue**: Reactor integrated as PostBuildEvent. If fails, build continues.

#### 10. Firebase vs Supabase Confusion

**Location**: Multiple files

**Clarification**:
- Firebase: License storage, session management
- Supabase: Waybill database, manifests

---

## 9. Audit Summary

### Project Health

| Aspect | Status |
|--------|--------|
| Architecture | Complex but functional |
| Tier separation | Correctly implemented |
| Update mechanism | Properly split (GitHub + Supabase) |
| Documentation | NEW - created during audit |
| Technical debt | Moderate (see risky areas) |

### Strengths

1. Clear tier separation with TierRuntimePolicy
2. Offline-first license verification
3. Proper 401 retry with token refresh
4. Binary split (GitHub for large, Supabase for small)
5. Comprehensive documentation created

### Areas Needing Attention

1. Token masking in logs (TODO)
2. WebView2 selector resilience
3. Supabase anon key in source (known acceptable risk)
4. Module system complexity

### Recommendations

1. **Immediate**: Implement token masking before release
2. **Soon**: Add selector versioning/fallback
3. **Later**: Consider service extraction from Main.cs
4. **Future**: Evaluate src/ structure migration

---

## 10. File Index

### Core Files

| File | Lines | Purpose |
|------|-------|---------|
| Program.cs | ~600 | Entry point, initialization |
| Main.cs | ~2040 | Main form, tab management |
| FullStackOperation.cs | ~2500 | ULTRA-only form |
| frmLogin.cs | ~200 | License dialog |

### Service Files

| File | Lines | Purpose |
|------|-------|---------|
| LicenseApiService.cs | ~408 | License verify/heartbeat |
| JmsAuthTokenService.cs | ~215 | Token orchestration |
| JmsApiClient.cs | ~200 | JMS HTTP API |
| InventorySyncService.cs | ~325 | Inventory fetch |
| SupabaseDbService.cs | ~166 | Database operations |
| VelopackUpdateService.cs | ~226 | In-app updates |
| SmallUpdateService.cs | ~197 | Selector updates |
| MajorUpdateService.cs | ~134 | Major updates |
| HashVerifier.cs | ~85 | DLL integrity |
| TierRuntimePolicy.cs | ~99 | Tier enforcement |

### Infrastructure Files

| File | Lines | Purpose |
|------|-------|---------|
| AppConfig.cs | ~200 | Runtime config |
| AppPaths.cs | ~140 | Path management |
| AppLogger.cs | ~100 | Logging |
| SecureConfigCrypto.cs | ~120 | Encryption |

---

**End of Audit Report**


