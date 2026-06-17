# Known Issues & Risk Areas

## Current Verified Baseline

Verified issues from the current checkout:

1. Resolved build blocker: root `modules\app-manifest.json`, `modules\active_modules.json`, `modules\modules-cache.json`, `modules\selectors.json`, and `modules\config.json` were missing, but `src/AutoJMS/AutoJMS.csproj` now guards those `Content Include` entries with `Exists(...)`. Latest recorded Debug build succeeded with warnings only.
2. Token logging: full 32-hex JMS auth tokens are logged in `Main.cs` and `JmsAuthTokenService.cs`. Must not ship to production.
3. Sensitive credential file: `service_account.json` exists in the workspace. Treat as compromised if ever shared.
4. Supabase anon key is hardcoded in `SupabaseDbService.cs`; this requires strict RLS and server-side controls.
5. Settings split: `SettingsManager` writes encrypted `AutoJMS.config.enc`, while `UserSettingsService` still reads/writes plain `AutoJMS.json`. Token persistence behavior is `NEED VERIFY`.
6. Module trust: module download paths are hash-checked, but signature verification is inconsistent/optional and one updater has placeholder key material.
7. Supabase schema gap: C# calls waybill/inventory RPCs not present in checked-in `supabase-migration.sql`.
8. Package compatibility warning: `PdfiumViewer 2.13.0` restores .NET Framework assets under `net8.0-windows`.
9. Obsolete credential API warnings: `GoogleCredential.FromJson` and `GoogleCredential.FromStream`.
10. Unawaited Google Sheet calls around the tracking upload flow in `Main.cs`.

Use the detailed risk list below as supporting context.

## High Priority

### 1. Full Token Logging in Production

**Location**: `Main.cs`, `JmsAuthTokenService.cs`

**Issue**: Full 32-hex authToken is logged to `debug.log`.

**Code**:
```csharp
// Main.cs line 1016
AppLogger.Info($"Auth token captured from {source} (len={token.Length}), authToken={token}");

// JmsAuthTokenService.cs line 107
AppLogger.Info($"[JmsAuth] token source={source}, len={token.Length}, changed={(changed ? "yes" : "no")}, authToken={LogToken(token)}");
```

**TODO exists**: `TODO(before-release): mask token in logs` in `Main.cs` line 1015.

**Risk**: Exposed token could be used to access JMS API.

**Status**: TODO - needs masking before release.

### 2. WebView2 Automation Selectors (Vue/Element UI)

**Location**: `WebViewAutomation.cs`

**Issue**: CSS selectors and JS injection scripts are fragile. Changes to JMS frontend can break automation.

**Known fragile selectors**:
- Element UI input setter: `querySelector('.el-input__inner').value = '...'; fireEvent(input, 'input')`
- Form submission: `.el-button--primary` click

**Risk**: DKCH automation fails silently or submits wrong data.

**Mitigation**: 
- Retry logic in DkchManager
- Error classification (NeedSwitchToDkch1Exception, NeedSwitchToDkch2Exception)

### 3. BASE Tier Background Jobs

**Location**: `Main.cs` lines 165-174, 354-362

**Issue**: Tier policy guards must be correct. If `TierRuntimePolicy.Resolve()` returns wrong flags, BASE could run background jobs.

**Current guards**:
```csharp
if (_tierPolicy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}
```

**Risk**: BASE users experiencing unexpected network activity and Supabase writes.

**Mitigation**: TierRuntimePolicy is well-guarded. Verify on any tier-related change.

### 4. Token Refresh Race Condition

**Location**: `JmsApiClient.cs`, `JmsAuthTokenService.cs`

**Issue**: Multiple concurrent 401s could trigger multiple token refreshes.

**Current mitigation**:
- `_refreshGate` SemaphoreSlim in `ForceRefreshFromWebViewAsync`
- 3-second coalescing window
- `_expiredGate` prevents "confirmed expired" storm

**Risk**: Race condition could cause token to be cleared while API call in progress.

## Medium Priority

### 5. DataGridView Performance with Large Datasets

**Location**: `Main.cs`, `FullStackOperation.cs`

**Issue**: DataGridView with thousands of rows can freeze UI.

**Current mitigations**:
- Double buffering enabled on tabTracking_dataView (line 176-177)
- Standard grid settings applied
- Pagination in Supabase queries (1000 rows per page)

**Risk**: UI freeze during inventory sync display.

### 6. Supabase API Key Exposure

**Location**: `SupabaseDbService.cs` line 17

**Issue**: Supabase anon key is hardcoded in source code.

```csharp
private const string SUPABASE_KEY = "eyJhbGci...";
```

**Risk**: Anyone can read public Supabase data.

**Mitigation**: This is the anon (public) key. Only read operations are used.

### 7. Inventory Sync Lock Contention

**Location**: `SupabaseDbService.cs`, `InventorySyncService.cs`

**Issue**: Multiple ULTRA users could fight for inventory sync lock.

**Current mitigation**:
- 30-minute lease with heartbeat refresh
- 5-minute heartbeat interval
- Non-acquisition is logged but non-fatal

**Risk**: One machine monopolizes sync, others never sync.

### 8. FullStackOperation Lifecycle

**Location**: `Main.cs` lines 866-925

**Issue**: Form is pre-created but hidden. Closing and re-showing must work correctly.

**Current behavior**:
1. Pre-created on `OnShown`
2. Show when user types "DASH"
3. `FormClosed` event sets `_fullStackForm = null`
4. `ShowFullStackForm` re-creates if disposed

**Risk**: Memory leak if form not disposed properly. Race condition between Show and Close.

## Low Priority

### 9. Module System Complexity

**Location**: `ModuleSystem/`, `archive/old-module-system/`

**Issue**: Multiple layers of module loading (BuiltIn, SupabaseModuleProvider, ActiveModules).

**Risk**: Confusing error messages if module loading fails.

**Status**: Module system exists but usage is minimal in current code.

### 10. Velopack Update Without Admin Rights

**Location**: `AutoJMS.iss`

**Issue**: Installer requires admin (`PrivilegesRequired=admin`) but Velopack updates need write to install directory.

**Current**: Install dir gets `users-modify` permissions.

**Risk**: Permission issues on restricted systems.

### 11. .NET Reactor Integration

**Location**: `src/AutoJMS/AutoJMS.csproj` lines 86-94, `tools/reactor/AutoJMS_Reactor.nrproj`

**Issue**: Reactor is integrated as PostBuildEvent. If Reactor fails, build continues.

**Risk**: Unprotected DLL deployed if Reactor step fails silently.

### 12. Firebase vs Supabase Confusion

**Location**: Multiple files

**Issue**: Two backends (Firebase for license, Supabase for data) can confuse developers.

**Clarification**:
- Firebase: License storage, session management
- Supabase: Waybill database, manifests

### 13. Path Separation (InstallDir vs UserDataDir)

**Location**: `AppPaths.cs`

**Issue**: Historical confusion about where data lives.

**Current structure**:
- InstallDir (AppContext.BaseDirectory): `...\current\`
- InstallRoot: `C:\AutoJMS`
- UserDataDir: `C:\AutoJMS\AppData`

**Risk**: Migration from old structure might be incomplete.

## Security Considerations

### Sensitive Data Locations

| Data | Storage | Encryption |
|------|---------|------------|
| License Key | `license.dat` | DPAPI via SecureConfigCrypto |
| Secure Config | `AutoJMS.secure` | AES-CBC-HMACSHA256 |
| LastAuthToken | `AutoJMS.json` | Plain JSON (should be encrypted) |
| Browser Data | `BrowserData\` | Unencrypted (WebView2) |

### Exposed Secrets

| Secret | Exposure | Risk |
|--------|----------|------|
| Supabase anon key | Source code | Read-only access |
| JMS AuthToken | Memory, AutoJMS.json | API access |
| License JWT | Memory only | Valid for 60 min |

## Migration Risks

### 1. Moving to src/ Structure

If code moves to `src/AutoJMS/`:
- All file references must update
- ModuleProjects reference path changes
- Velopack publish path changes

### 2. Splitting Services from MainForm

If services are extracted:
- Static service accessors need refactoring
- Tier policy access needs refactoring
- WebViewHost initialization dependency

### 3. Changing Update Provider

If switching from GitHub to Supabase:
- Binary size limit (50MB Supabase free)
- Download mechanism change
- User migration path needed

