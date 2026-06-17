# AutoJMS Claude Chat Handoff

Generated: 2026-06-08  
Workspace scanned: `D:\v1.2605.2(new-test)`  
Purpose: give Claude Chat enough current-codebase context to propose solutions and plans without first rediscovering the project.  
Scope of this file: architecture, structure, data flow, risks, Print/Tracking/FullStack, installer/update, backend/license. It intentionally does not include secrets or full tokens.

## 1. Non-Negotiable Project Rules

Read these before proposing changes:

- AutoJMS is a .NET 8 WinForms + SunnyUI + WebView2 desktop app.
- BASE tabs are HOME, DKCH, TRACKING, PRINT, ABOUT.
- ULTRA adds `FullStackOperation` as a separate visible form, not a tab.
- ABOUT tab must remain last.
- Firebase/Render license server handles license/session/auth/tier.
- Supabase is used for manifests/config/hash/tier/selector update and legacy waybill database paths.
- GitHub Releases host Velopack binaries.
- Inno Setup is first install/reinstall wrapper.
- Velopack is in-app update.
- Do not open GitHub pages for update.
- Do not upload `.nupkg` to Supabase.
- Do not let BASE start background inventory/database sync.
- Do not access WebView2 outside UI thread.
- Do not log production tokens in full.
- Do not change HOME/DKCH/TRACKING/PRINT/ABOUT logic unless explicitly requested.

## 2. Executive Summary

AutoJMS is a logistics automation tool for JMS/J&T workflows. It uses WebView2 for JMS website sessions and extracts a JMS `authToken` from the logged-in WebView session. User-triggered JMS APIs are called through `JmsApiClient`.

The app has two product layers:

- BASE: main form tabs only, manual tracking and printing.
- ULTRA: BASE plus the standalone `FullStackOperation` operation center with local-first SQLite inventory/workflow data.

Current critical flows:

- Startup verifies license against the Render backend, gets tier, middleCode, module/update config, then starts `Main`.
- Tracking tab calls JMS POST `operatingplatform/podTracking/inner/query/keywordList`.
- Print tab uses TrackingService output for current status, safety validation, print approval info, and PDF generation.
- Print safety is fail-closed: fresh tracking data must prove the waybill belongs to the current `MiddleCode`.
- FullStackOperation is code-first UI. It stores operational inventory/workflow in SQLite and uses JMS tracking API for journey detail enrichment.
- Updates are split: `update.xml` is UI/control metadata from GitHub raw; actual Velopack updates use GitHub Releases through `GithubSource`.
- First install is an Inno wrapper that runs Velopack Setup into the user-selected directory.

## 3. Main Source Map

Solution/project:

- `AutoJMS.slnx`
- `src/AutoJMS/AutoJMS.csproj`
- `src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj`

Core app:

- `src/AutoJMS/Program.cs`: entry point, Velopack init, license verify/offline fallback, service initialization, module startup, heartbeat, `Application.Run(new Main(sessionTier))`.
- `src/AutoJMS/Forms/Main.cs`: BASE tabs, WebView2, auth token capture, Tracking/Print/About, DASH command for FullStackOperation.
- `src/AutoJMS/Forms/Main.Designer.cs`: WinForms Designer for Main.
- `src/AutoJMS/Config/AppPaths.cs`: centralized runtime paths.
- `src/AutoJMS/Config/AppConfig.cs`: encrypted secure runtime config and JMS/Supabase/update defaults.
- `src/AutoJMS/Config/SettingsManager.cs`: plain `AutoJMS.json` settings.

License/auth/tier:

- `src/AutoJMS/Licensing/LicenseApiService.cs`
- `src/AutoJMS/Licensing/JmsAuthTokenService.cs`
- `src/AutoJMS/Licensing/JmsAuthStateService.cs`
- `src/AutoJMS/Licensing/AuthStateService.cs`
- `src/AutoJMS/Licensing/TierRuntimePolicy.cs`

JMS APIs:

- `src/AutoJMS/Services/JmsApiClient.cs`
- `src/AutoJMS/Services/JmsResponseClassifier.cs`

Tracking:

- `src/AutoJMS/Tracking/ITrackingService.cs`
- `src/AutoJMS/Tracking/WaybillTrackingService.cs`

Print:

- `src/AutoJMS/Printing/IPrintService.cs`
- `src/AutoJMS/Printing/PrintService.cs`
- `src/AutoJMS/Printing/PrintSafetyGuard.cs`
- `src/AutoJMS/Printing/PrintStatusSnapshot.cs`
- `src/AutoJMS/Printing/PrintApprovalInfo.cs`
- `src/AutoJMS/Services/SiteContextProvider.cs`

FullStackOperation:

- `src/AutoJMS/Forms/FullStackOperation*.cs`
- `src/AutoJMS/FullStack/**`

Update/release:

- `src/AutoJMS/Updates/VelopackUpdateService.cs`
- `src/AutoJMS/Updates/UpdateXmlManifestService.cs`
- `src/AutoJMS/Updates/MajorUpdateService.cs`
- `src/AutoJMS/Updates/SmallUpdateService.cs`
- `release/build-release.ps1`
- `installer/inno/AutoJMS.iss`
- `installer/inno/build-installer.ps1`

Backend:

- `backend/render-license-server/server.js`

## 4. Runtime Startup Flow

High-level flow in `Program.Main()`:

1. `VelopackApp.Build().Run()`.
2. Create writable dirs via `AppPaths.EnsureDirectories()`.
3. Migrate bundled config/modules into user data if needed.
4. Configure WinForms DPI and exception handlers.
5. Compute HWID and executable hash.
6. Enforce single instance by mutex.
7. Load bootstrap config.
8. Offline-first license flow:
   - If saved license key exists and network is available, verify with Render.
   - If online verification succeeds, initialize services from license response.
   - If offline with saved key, allow startup with cached key but limited service config may be unavailable.
   - If no valid saved key, show `frmLogin`.
9. Start network monitor and heartbeat.
10. Initialize module system and background module sync.
11. Run hash integrity check in background.
12. Start `Main(sessionTier)`.

Important: `InitializeServicesFromLicense(VerifyResult)` stores:

- `Program.UpdateChannel`
- Supabase manifest/runtime/update services
- `SiteContextProvider.ApplyLicenseMiddleCode(result.MiddleCode)`
- `AppConfig.Current.DataSpreadsheetId`

## 5. Runtime Paths and Config

`AppPaths` resolves paths around Velopack layout:

```text
{InstallRoot}\
  current\             app binaries, deleted/replaced by Velopack updates
  packages\            Velopack package cache
  AutoJMS.exe          Velopack stub launcher
  AppData\             all runtime/user data
    AutoJMS.json
    service_account.json
    logs\debug.log
    secure\
    cache\
    Downloads\
    BrowserData\
    FullStack\
```

Key files:

- `AppData\AutoJMS.json`: plain local settings.
- `AppData\secure\AutoJMS.secure`: encrypted runtime config.
- `AppData\secure\license.dat`: local license cache.
- `AppData\service_account.json`: Google service account path expected by app.
- `AppData\FullStack\autojms_fullstack.db`: FullStack local operational DB.
- `AppData\FullStack\details.db`: FullStack waybill journey raw JSON/events DB.

`SettingsManager.AppSettings` currently includes:

- `zoomFactor`
- `defaultUrl`
- `lastAuthToken`
- print settings
- `MiddleCode`
- `MiddleCodeAliases`
- `MiddleCodeSegment2`
- `AllowMiddleCodeSegment2Match`

`AppConfig` includes:

- JMS base/API URL.
- update XML raw URL default: `https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml`.
- Google service account path default: `AppData\service_account.json`.
- data spreadsheet ID.
- action site code.
- Supabase config.

## 6. Token and Auth Model

There are two separate token concepts:

- License JWT from Render server. Used for license heartbeat/session only.
- JMS `authToken`, observed as 32 hex chars. Used for JMS APIs.

`JmsAuthTokenService` resolves JMS token in this order:

1. In-memory `JmsAuthStateService.CurrentToken`.
2. WebView2 localStorage via a UI-thread reader registered from `Main`.
3. `lastAuthToken` from `AutoJMS.json`.

`JmsApiClient.PostJsonAsync()`:

- Resolves JMS token.
- Sends browser-equivalent JMS headers.
- On 401/session-expired response, forces one WebView token refresh and retries exactly once.
- If retry still fails, calls `JmsAuthTokenService.NotifyReallyExpired()`.

Risk:

- `JmsAuthTokenService.LogToken()` currently has a TODO for masking and can log the full JMS token in dev-style logs. This must be corrected before production logging.

## 7. Tier Policy

`TierRuntimePolicy.Resolve()` is the only intended runtime policy gate.

BASE:

- Manual Tracking allowed.
- Manual Print allowed.
- No startup inventory sync.
- No startup database tracking.
- No background auto sync.
- No FullStackOperation form.

ULTRA:

- All BASE capabilities.
- FullStackOperation enabled.
- Background inventory/realtime capabilities enabled.

`Main.OnShown` pre-creates `FullStackOperation` only when policy allows it. Typing `DASH` in HOME URL bar opens it only if `EnableFullStackOperation` is true.

## 8. Main Form / BASE Tabs

`Main` registers:

- HOME
- DKCH
- TRACKING
- PRINT
- ABOUT

Current notes:

- WebView2 surfaces are initialized in `Main.OnLoad`.
- Token capture happens through WebView network/localStorage handling.
- The DASH command is handled in HOME URL bar `KeyDown`.
- BASE tabs should not depend on FullStack.
- FullStack is not a tab and must not make Main dependent on its runtime.

## 9. Tracking Tab

Primary service: `WaybillTrackingService`.

Endpoint:

```text
POST {JMS API base}/operatingplatform/podTracking/inner/query/keywordList
```

Payload:

```json
{
  "keywordList": ["waybill1", "waybill2"],
  "trackingTypeEnum": "WAYBILL",
  "countryId": "1"
}
```

Headers are sent through `JmsApiClient` with routeName usually `trackingExpress`.

Tracking flow:

1. Extract waybills from textbox.
2. Batch by `BatchSize = 40`.
3. Parallelize with `MaxConcurrency = 8`.
4. Call keywordList API.
5. Parse `WaybillHistoryResponse.data[]`.
6. For each data item, call `ProcessTrackingData()`.
7. Optionally call `operatingplatform/order/getOrderDetail`.
8. Fill `_allRows`, sync into `_displayTable`, bind to `tabTracking_dataView`.

Important fields in `TrackingRow`:

- `WaybillNo`
- `TrangThaiHienTai`
- `ThaoTacCuoi`
- `ThoiGianThaoTac`
- `MaDoanFull`
- `MaDoan1`
- `MaDoan2`
- `MaDoan3`
- `SafetyEvents`
- print approval fields:
  - `PrintApprovalStatusName`
  - `PrintSenderNetworkCode`
  - `PrintApprovalPrintCount`
  - `PrintApplyStaffName`

`SafetyEvents` contains:

- `WaybillNo`
- `BillCode`
- `ScanTime`
- `ScanNetworkCode`

Grid state:

- Tracking grid uses a `DataTable` and `BindingSource`.
- Recent implementation caches/calculates column widths from header and returned row values.
- Empty display values are normalized to `-` in some measurement paths.

Risk:

- `WaybillTrackingService` still has a translation-like helper block near the model definitions. For Tracking tab this may be legacy UI behavior. If Claude proposes "no translation" changes, verify exact display requirement first because Print/Tracking may already rely on current labels.

## 10. Print Tab

Primary files:

- `PrintService`
- `PrintSafetyGuard`
- `Main.ExecutePrintAsync()`

Current print search flow:

1. User inputs waybill(s), can include suffix like `861822407381-001`.
2. `PrintService.SearchAndLoadAsync(input, mode)` clears old printable state.
3. Tracking input is normalized to base waybill before tracking, e.g. `861822407381-001` -> `861822407381`.
4. Calls `TrackingService.SearchTrackingAsync(..., updateMainGrid: false)`.
5. Loads fresh tracking rows into print rows.
6. Calls `RefreshPrintStatusCoreAsync()` which calls print approval API.
7. Merges tracking and approval info into the print grid.

Print approval API:

```text
POST {JMS API base}/operatingplatform/rebackTransferExpress/pringListPage
```

Fallback endpoint also exists:

```text
operatingplatform/rebackTransferExpress/listPage
```

Payload fields include:

- `current`
- `size`
- `pringFlag`
- `applyNetworkId`
- `waybillIds`
- `applyTimeFrom`
- `applyTimeTo`
- `cancelOrderType`
- `pringType`
- `countryId`

Important: field names are `pringFlag` and `pringType`, not `printFlag` or `printType`.

Approval response mapping:

- `statusName` -> approval status.
- `senderNetworkCode` / `newSenderNetworkCode` -> segment after print.
- `printCount` -> number of prints.
- `applyStaffName` -> printer/apply staff.

Print execution flow in `Main.ExecutePrintAsync()`:

1. Get selected waybills.
2. `ValidateSelectedBeforePrintAsync()`.
3. Re-read selected waybills after validation because guard may clear state.
4. Ensure selected set matches current input.
5. Refresh print status before print.
6. Request PDF URL through:
   ```text
   POST operatingplatform/rebackTransferExpress/printWaybill
   ```
   payload includes `waybillIds`, `applyTypeCode`, `printType`, `pringType`, `countryId`.
7. Download PDF to `AppData\Downloads\Vận đơn đã in`.
8. Print with PdfiumViewer.
9. Refresh print status after print.
10. Write success log line:
    ```text
    |yyyy-MM-dd HH:mm:ss| Đã in x đơn | waybill | Số lần in: n | Người in: staff |
    ```
11. Write structured print audit TSV.

## 11. Print Safety Guard

Primary service: `PrintSafetyGuard`.

Current order observed in code:

1. Normalize input waybill.
2. Normalize tracking waybill by removing suffix after `-`.
3. Load middleCode from `SiteContext`.
4. Validate input length.
5. If middleCode missing, block `MIDDLE_CODE_MISSING_CONFIG`.
6. If tracking result missing, block `TRACKING_FAILED`.
7. If tracking waybill mismatch, block `WAYBILL_MISMATCH`.
8. If `SafetyEvents` empty, block `JOURNEY_EMPTY`.
9. Scan all `details[].scanNetworkCode` represented as `TrackingRow.SafetyEvents`, sorted by `scanTime` DESC.
10. If any `scanNetworkCode == MiddleCode`, allow `SCAN_NETWORK_CODE_MATCHED`.
11. Only if no scanNetworkCode match, fallback to `MaDoan2 == MiddleCode`, allow `MADOAN2_MATCHED`.
12. If MaDoan2 missing, block `MADOAN2_MISSING_IN_TRACKING`.
13. If MaDoan2 differs, block `MADOAN2_MISMATCH`.

Current allow rules:

- `details[].scanNetworkCode == MiddleCode`
- fallback only: `TrackingRow.MaDoan2 == MiddleCode`

Not used to allow print:

- content contains middleCode
- raw JSON contains middleCode
- scanNetworkName contains middleCode
- substring/segment guess
- cache

Audit log:

```text
AppData\logs\print-safety-audit.log
```

Audit fields currently include timestamp, input waybill, tracking waybill, middleCode, MaDoan2, matched scanNetworkCode/time/index, action, reason, event count, tracking status, hash, source, match type/value/field.

Warning message currently in code is shorter than the earlier requested wording:

```text
Đang in sai bưu cục, chuyển sang trang in truyền thống hoặc tắt AutoPrint.
```

If exact legal/operation wording matters, this should be reviewed.

## 12. Site Context / MiddleCode

`SiteContextProvider` is the current shared source for office context.

MiddleCode path:

```text
license verify response
-> VerifyResult.MiddleCode
-> Program.InitializeServicesFromLicense()
-> SiteContextProvider.ApplyLicenseMiddleCode()
-> AppConfig.Current.ActionSiteCode
-> AutoJMS.json MiddleCode/MiddleCodeAliases
-> SiteContextProvider.Current.MiddleCode
```

Important behavior:

- `SiteContext.IsValid` currently checks only non-empty `MiddleCode`.
- `MiddleCode = 0000` is not auto-invalidated.
- `ApplyLicenseMiddleCode()` persists runtime and local config.

Risk:

- If `Program.InitializeServicesFromLicense()` is not reached, e.g. offline startup with cached key only, middleCode may rely on existing `AutoJMS.json`.
- Previous audit lines with empty middleCode likely came from old config or before license middleCode persistence was fixed.

## 13. FullStackOperation / ULTRA Operation Center

FullStackOperation is a code-first UI form:

- Designer is intentionally minimal.
- Runtime layout is built by partials:
  - `FullStackOperation.Fields.cs`
  - `FullStackOperation.Theme.cs`
  - `FullStackOperation.Layout.cs`
  - `FullStackOperation.Dashboard.cs`
  - `FullStackOperation.Chatbot.cs`
  - `FullStackOperation.Events.cs`
  - `FullStackOperation.WaybillWorkspace.cs`

Lifecycle:

1. Constructor configures shell and code-first UI.
2. Subscribes to `AuthStateService.Instance.TokenAcquired`.
3. `Load` initializes grids/UI/local DB and waits for auth token.
4. When auth is available, starts 2-minute refresh timer and loads data.
5. `FormClosing` stops timers, cancels journey load, unsubscribes token handler.

Local-first data:

- Main local DB: `AppData\FullStack\autojms_fullstack.db`.
- Journey details DB: `AppData\FullStack\details.db`.
- Both use SQLite WAL, synchronous NORMAL, busy_timeout, foreign_keys.

Major services:

- `FullStackDashboardService`
- `FullStackInventorySyncService`
- `FullStackWorkflowService`
- `FullStackExportService`
- `FullStackJourneyService`
- `FullStackTrackingJourneyService`
- `WaybillJourneyDetailsRepository`
- `WaybillJourneyJsonParser`
- `FullStackRiskEngine`
- `FullStackSlaEngine`

UI components:

- Operation center controls under `src/AutoJMS/FullStack/UI/OperationCenter`.
- Thoi Hieu KPI sheet under `src/AutoJMS/FullStack/UI/ThoiHieu`.

## 14. FullStack Journey Workspace

Double-click journey workspace currently:

- Shows a journey panel instead of inventory grid.
- Header: back button, "Hành trình vận chuyển", waybill, status, cache/raw buttons, "Giảm dần".
- Grid columns:
  - STT
  - Thời gian thao tác
  - Thời gian tải lên
  - Loại thao tác
  - Mô tả lịch sử hành trình
  - Nguồn
  - Trọng lượng
  - Tệp đính kèm
- Grid uses cell selection, fixed small fonts, fixed row height, no `AutoSizeRowsMode.AllCells`.
- Empty values in journey view models are generally `-`.

Fresh API service:

- `FullStackTrackingJourneyService`
- Endpoint: `operatingplatform/podTracking/inner/query/keywordList`
- Payload:
  ```json
  {
    "keywordList": ["waybill"],
    "trackingTypeEnum": "WAYBILL",
    "countryId": "1"
  }
  ```
- Headers include `routeName=trackingExpress`, JMS origin/referer, timezone, lang/langType, routerNameList.
- It parses `root.data[]`, selects matching `keyword` or matching details, then `details[]`.
- It maps raw fields such as `scanTime`, `uploadTime`, `scanTypeName`, `waybillTrackingContent`, `remark6`, `weight`, `imgType`.

Potential inconsistency:

- `FullStackTrackingJourneyService` uses raw field values for action/description.
- `WaybillJourneyJsonParser` still calls `JourneyTextNormalizer.NormalizeActionType/NormalizeScanSource` and has fallback sentence assembly.
- There is also an old `FullStackPodTrackingApiClient` for GET `podTrackingList`.
- If solving journey correctness, confirm which service is actually used by `FullStackJourneyService` and remove/ignore old paths carefully.

## 15. Supabase / Database

`SupabaseDbService` still exists and is used by older sync paths:

- Hardcoded Supabase URL and anon key exist in source.
- RPCs:
  - `try_acquire_inventory_lease`
  - `refresh_inventory_lease`
  - `release_inventory_lease`
  - `complete_inventory_sync`
  - `upsert_new_waybills`
  - `merge_waybill_tracking_rows`
- Tables/models through Supabase C# client include `WaybillDbModel`.

Current product direction from recent work:

- FullStack operational inventory should be local-first SQLite.
- Supabase should retain manifest/config/update, not be required for operational inventory performance.

Risk:

- Existing `Main` still contains Supabase inventory sync methods, but `TierRuntimePolicy` should prevent BASE background sync.
- Hardcoded anon key and missing/mismatched Supabase migration coverage are known risks.

## 16. Google Sheets

Primary service:

- `src/AutoJMS/Services/GoogleSheetService.cs`

Credential path:

- `AppData\service_account.json` through `AppPaths.GoogleServiceAccountJson`.

Risk:

- Workspace audit says `service_account.json` exists in the workspace and is sensitive. Do not paste its contents into external chats. If it was exposed, rotate.

## 17. Update Architecture

Current rule:

- `update.xml` = manifest for About/update UI and channel metadata.
- Velopack update source = GitHub Releases through `GithubSource` or a real `RELEASES` feed.
- Do not treat a GitHub release asset URL as a SimpleWebSource folder feed.

`UpdateXmlManifestService` reads:

```text
https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml
```

Fields parsed:

- `velopackVersion`
- `displayVersion`
- `internalBuild`
- `releaseTag`
- `setupUrl`
- `velopackSetupUrl`
- `releaseNotes`
- `releaseNotesUrl`
- `velopackFeedUrl`
- prerelease/mandatory/manualOnly

`VelopackUpdateService` flow:

1. Read `update.xml`.
2. If channel provider is GitHub/repo URL, use `GithubSource`.
3. Else fallback to version-latest.json.
4. Else fallback to legacy Supabase `SimpleWebSource`.
5. `CheckAndUpdateAsync()` asks user, downloads, calls `PrepareForUpdateAsync()`, applies update and restarts.

About tab:

- `Main.tabAbout_btnCheckUpdate_Click` loads update metadata, shows `UpdateChannelDialog`, then runs `VelopackUpdateService` with chosen channel.
- If manifests cannot be loaded, UI may show `UNKNOWN`.

## 18. Release Pipeline

Release script:

- `release/build-release.ps1`

Release repo:

- `Datt03-sss/AutoJMS-Update`

Velopack version rule:

- Stable: `x.y.z`, e.g. `1.26.6`.
- Beta: `x.y.z-beta.n`, e.g. `1.26.6-beta.1`.
- Do not use 4-part version as VelopackVersion.
- DisplayVersion may be 4-part.

Script steps:

1. `dotnet publish` self-contained win-x64.
2. Optional .NET Reactor protection.
3. Verify self-contained publish.
4. `vpk pack`:
   ```text
   --packId AutoJMS
   --packTitle AutoJMS
   --packVersion <VelopackVersion>
   --packDir <PublishDir>
   --mainExe AutoJMS.exe
   --outputDir <OutputDir>
   --channel <stable|beta>
   --shortcuts Desktop,StartMenuRoot
   ```
5. Normalize assets:
   - `RELEASES`
   - `AutoJMS-<VelopackVersion>-full.nupkg`
   - `AutoJMS-win-Setup.exe`
6. Build Inno installer:
   - `AutoJMS-Installer-<VelopackVersion>.exe`
7. Upload GitHub Release assets if `-Upload`.
8. Generate/update `update.xml` in GitHub repo.

Stable tag:

```text
v<VelopackVersion>-Release
```

Beta tag:

```text
v<VelopackVersion>
```

Expected GitHub Release assets:

- `RELEASES`
- `AutoJMS-<VelopackVersion>-full.nupkg`
- `AutoJMS-win-Setup.exe`
- `AutoJMS-Installer-<VelopackVersion>.exe`

## 19. Installer Architecture

Inno script:

- `installer/inno/AutoJMS.iss`

Architecture:

- Velopack = real app install/update/uninstall owner.
- Inno = bootstrapper wrapper with wizard and prerequisites.
- Windows should show only one AutoJMS app entry, owned by Velopack.

Current Inno settings:

- `AppName=AutoJMS Installer`
- `DefaultDirName=C:\AutoJMS`
- `DisableDirPage=no`
- `CreateAppDir=yes`
- `UsePreviousAppDir=yes`
- `DisableProgramGroupPage=yes`
- `Uninstallable=no`
- `CreateUninstallRegKey=no`
- `SetupLogging=no`
- `PrivilegesRequired=admin`
- no `PrivilegesRequiredOverridesAllowed` override directive
- optional desktop shortcut task.

Current Inno install layout:

- Creates `{app}`, `{app}\current`, `{app}\packages`, `{app}\AppData\...`.
- Bundles Velopack Setup as a `dontcopy` file.
- Extracts it to `{tmp}`.
- Runs:
  ```text
  AutoJMS-win-Setup.exe --silent --installto "{app}"
  ```
- If user unchecked desktop shortcut task, deletes Velopack-created desktop shortcuts from user/common desktop.

Important test expectations:

- User can choose install path; default is `C:\AutoJMS`.
- Normal double-click install should trigger UAC/admin elevation.
- If user chooses `D:\AutoJMS`, no app/user files should be created outside that install root except standard Windows shortcuts/registry entries owned by Velopack.
- Apps & Features should show one AutoJMS entry.
- Start Menu should not be duplicated.
- Desktop shortcut should follow checkbox.

## 20. Backend License Server

Backend:

- `backend/render-license-server/server.js`

Main endpoints:

- `GET /health`
- `POST /api/verify-license`
- `POST /api/heartbeat`

Verify flow:

1. Read `licenseKey`, `hwid`, `exeHash`, optional `appVersion`.
2. Load license from Firebase Realtime Database `Licenses/{licenseKey}`.
3. Check active status.
4. Check HWID binding.
5. Optionally validate hash from env `VALID_EXE_HASHES` unless `skipHashCheck`.
6. Clear old sessions for same license/device.
7. Create new session.
8. Sign JWT.
9. Return:
   - `payload`
   - `sid`
   - `tier`
   - `middleCode`
   - `skipHashCheck`
   - `modulePolicy`
   - nested `license`
   - `cfg.dataSpreadsheetId`
   - `cfg.updateChannel`
   - `supabase.baseUrl`
   - `supabase.manifests`

Firebase license example shape from recent context:

```json
{
  "createdAt": "26-05-2026 01:22",
  "dataSpreadsheetId": "...",
  "hwid": "...",
  "middleCode": "214A02",
  "modulePolicy": {
    "applyOnNextStartup": true,
    "autoUpdate": true,
    "silentUpdate": true
  },
  "skipHashCheck": true,
  "status": "active",
  "tier": "BASE"
}
```

Do not change the user's Firebase key structure without explicit request.

## 21. DevTools Internal Tooling

Internal WebView DevTools files exist under:

- `src/AutoJMS/Automation/DevTools/DevToolsCaptureModels.cs`
- `src/AutoJMS/Automation/DevTools/DomSnapshotService.cs`
- `src/AutoJMS/Automation/DevTools/NetworkCaptureService.cs`
- `src/AutoJMS/Automation/DevTools/SelectorDiscoveryService.cs`
- `src/AutoJMS/Automation/DevTools/WebRouteDetector.cs`
- `src/AutoJMS/Automation/DevTools/WebViewDevToolsInspector.cs`

Use these for controlled capture of WebView routes/network/DOM/selectors when future tasks require API or selector discovery. Do not log full tokens.

## 22. Current Known Risks

Security/config:

- JMS authToken may be logged in full via `JmsAuthTokenService.LogToken()`; fix before production logging.
- Google `service_account.json` is sensitive. Do not share contents externally.
- Supabase anon key is hardcoded in `SupabaseDbService.cs`; verify RLS/RPC permissions.
- License server public key/private key rotation model should be reviewed before public distribution.

Architecture:

- Some old Supabase operational sync code remains while FullStack is moving local-first.
- FullStack journey has multiple API/parser paths; risk of inconsistent display or old GET client accidentally being reused.
- Settings are split between `AutoJMS.json` and secure config; this is workable but should be documented in any future config plan.
- Offline startup can bypass fresh license config initialization; middleCode depends on local saved settings.

Installer/update:

- Old installations may have duplicate Inno/Velopack entries until cleaned manually.
- Validate every release installer with a clean machine or VM: install path, shortcut checkbox, Apps & Features, update.
- Verify `update.xml` stable/beta metadata after each release upload.

Reliability:

- WebView2 access must remain on UI thread.
- Print must remain fail-closed.
- BASE must not start FullStack/background sync.
- App has limited automated tests; most validation is manual/log based.

## 23. Recommended Claude Review Plan

If Claude Chat is asked for project planning, use this order:

1. Verify safety-critical Print flow:
   - `PrintService.SearchAndLoadAsync`
   - `ValidateSelectedBeforePrintAsync`
   - `PrintSafetyGuard`
   - `Main.ExecutePrintAsync`
   - print audit logs
2. Verify JMS token lifecycle:
   - token capture
   - masking
   - forced refresh
   - no JWT/JMS token confusion
3. Verify release/install:
   - Inno wrapper behavior
   - Velopack `GithubSource`
   - update.xml metadata mapping
   - GitHub Release asset completeness
4. Verify FullStack local-first:
   - SQLite schema/migrations
   - old Supabase dependencies
   - Journey API/parser consistency
   - BASE/ULTRA gating
5. Verify backend license contract:
   - middleCode propagation
   - tier/module policy
   - hash/integrity behavior
6. Build a test matrix before code changes.

## 24. Suggested Manual Test Matrix

Build:

- `dotnet build src\AutoJMS\AutoJMS.csproj -c Debug`
- `release\build-release.ps1 -Version "<x.y.z>" -Channel stable`
- `release\build-release.ps1 -Version "<x.y.z-beta.n>" -Channel beta`

License:

- Fresh key online.
- Saved key online.
- Saved key offline.
- Revoked key.
- middleCode present and saved to `AutoJMS.json`.

WebView/JMS auth:

- Login to JMS.
- Capture token from WebView.
- Tracking API succeeds.
- Force token expiry/401 if possible.

Tracking:

- Search single waybill.
- Search batch.
- Validate `MaDoan2`, `SafetyEvents`, latest scan.
- Confirm grid widths before/after data.

Print:

- Correct office: `scanNetworkCode == MiddleCode` -> allow.
- MaDoan2 changed but scan history contains MiddleCode -> allow.
- No scanNetworkCode match, MaDoan2 == MiddleCode -> allow.
- No scanNetworkCode match, MaDoan2 mismatch -> block.
- Missing middleCode -> block `MIDDLE_CODE_MISSING_CONFIG`.
- Waybill suffix `-001` uses base waybill for tracking.
- Invalid input after successful print must not reprint old PDF.
- Enter two codes quickly must not print stale result.
- After print, refresh approval info and log latest print count.

FullStack:

- BASE typing DASH blocked.
- ULTRA typing DASH opens form.
- Local DB initializes under `AppData\FullStack`.
- Inventory grid loads local data.
- Single-click previews only.
- Double-click journey requests fresh tracking and binds grid.
- Back returns to current filtered grid.
- Thoi Hieu tab still renders.

Installer/update:

- Clean install to default path.
- Clean install to `D:\AutoJMS`.
- Confirm no data outside chosen install root except shortcuts/registry.
- Desktop shortcut checkbox on/off.
- Apps & Features single AutoJMS entry.
- About check update stable/beta.
- Apply update keeps `AppData`.

## 25. Important Questions for Future Planning

Ask the owner before large changes:

- Is `MiddleCode` the only production site identifier for print safety, or are there multi-site licenses?
- Should FullStack journey display preserve raw JMS text 100%, or is translation acceptable in any context?
- Should Supabase operational data be removed entirely from FullStack path, or only kept as legacy fallback?
- Which endpoints are officially stable vs captured from JMS web?
- Should service account credentials be distributed per install, per license, or managed server-side?
- Is code signing required for installer/Velopack releases?
- Are there staging Firebase/Render/Supabase projects for safe testing?

## 26. Minimal Context Summary for Claude

AutoJMS is not a small WinForms app. It is a license-gated logistics automation system with WebView2 JMS session capture, JMS API clients, print safety logic, local SQLite FullStack operation center, and Velopack/GitHub update infrastructure. Most dangerous areas are Print safety, token handling, tier gating, and installer/update ownership. Any proposed plan must preserve BASE tabs and should avoid broad refactors until the exact active service path is verified in code.
