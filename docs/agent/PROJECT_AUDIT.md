# AutoJMS Project Audit

## Summary
AutoJMS is a C# WinForms desktop application targeting .NET 8. It leverages SunnyUI for user interface styling, WebView2 for automated web interactions with the JMS portal, Render Node.js server and Firebase RTDB for licensing, and Supabase for cloud configuration and waybill database synchronization. 

This audit reviews the repository's current architecture to identify design gaps, naming discrepancies, file organization issues, and safety risks before implementing automated vibe coding.

---

## Current Structure
The codebase consists of:
1. `AutoJMS.slnx`: XML/JSON-style C# solution catalog.
2. `src/AutoJMS/AutoJMS.csproj`: Main executable Windows Forms project.
3. `src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj`: Shared contracts project.
4. `docs/`: Repository documentation (audit reports, release steps, architecture).
5. `eng/harness/`: PowerShell scripts for local testing, compiling, and validation.
6. `eng/prompts/`: Coding context instructions for AI agents.

---

## Module Map

| Module | Key Class / Files | Responsibility |
|---|---|---|
| **App Bootstrap** | `Program.cs` | Velopack initialization, HWID calculation, offline license check, services bootstrap. |
| **Main Shell** | `Forms/Main.cs` | UI shell container managing WebView2 instances and tab switching. |
| **Home** | `Forms/Main.cs` | Embedded WebView2 rendering the main JMS site (`jms.jtexpress.vn`). |
| **DKCH** | `Forms/Main.cs`, `Automation/WebViewAutomation.cs` | Automated registration of return shipments via JS injection in WebView2. |
| **Tracking** | `Tracking/WaybillTrackingService.cs` | Fetching waybill logs and histories from JMS API. |
| **Print** | `Printing/PrintService.cs`, `Printing/PrintSafetyGuard.cs` | Parsing PDF labels, sending print jobs to local printers, checking readiness. |
| **About** | `Forms/Main.cs`, `Updates/VelopackUpdateService.cs` | Displays app version, triggers update check manually. |
| **Operation Center** | `Forms/FullStackOperation.cs` (and partials) | स्टैंडअलोन dashboard form displaying waybill, SLA, and Zalo charts (ULTRA tier only). |
| **Auth/License** | `Licensing/LicenseApiService.cs`, `Licensing/TierRuntimePolicy.cs` | Verify RS256 JWT, heartbeat with Render server, cache license keys locally. |
| **Supabase Integration** | `Data/SupabaseDbService.cs` | Query and push waybills and sync manifests to Supabase PostgreSQL. |
| **Firebase Integration** | `backend/render-license-server/server.js` | Firebase Admin SDK used by the license backend for user tier administration. |
| **JMS Integration** | `Services/JmsApiClient.cs`, `Licensing/JmsAuthTokenService.cs` | Captures, refreshes, and authenticates requests against the JMS API Gateway. |
| **Zalo Integration** | `Services/ZaloChatService.cs` | Integrates messages and alerts with the Zalo messaging platform (ULTRA tier only). |
| **Velopack / Update** | `Updates/VelopackUpdateService.cs`, `Updates/MajorUpdateService.cs` | Fetches `version-latest.json` from Supabase and applies updates from GitHub Releases. |
| **Config** | `Config/AppConfig.cs`, `Config/SettingsManager.cs` | Loads secure local settings and boot properties. |
| **Logging** | `Config/AppLogger.cs` | General text logger (logs into AppData/logs/debug.log). |
| **Data/Repository** | `FullStack/Repositories/FullStackWaybillRepository.cs` | Isolated DB repository layer for local SQLite storage of waybills. |
| **UI Helpers** | `UI/UiThread.cs`, `UI/WebViewHost.cs` | Safety marshaling utility for WebView2 UI thread-safe operations. |

---

## Duplicated or Overlapping Responsibilities

1. **Manifest Parsing Overlap**:
   - `UpdateXmlManifestService` parses an XML manifest for updates.
   - `SupabaseManifestService` parses JSON manifests on Supabase Storage.
   - Both are responsible for checking if a new version exists.
2. **Update Service Orchestration**:
   - `MajorUpdateService` coordinates check/download flows.
   - `VelopackUpdateService` contains overlapping update logic combined with UI controls and forms.
3. **Database Connection Factories**:
   - `FullStackDbConnectionFactory` and `WaybillJourneyDetailsDbConnectionFactory` are separate classes but perform identical functions (creating local SQLite connections).
4. **Google Sheets Sync**:
   - Sync parameters are handled inside `GoogleSheetService.cs`, but spreadsheet synchronization commands are partially duplicated inside form events in `FullStackOperation.cs`.

---

## Hardcoded Config Risks

1. **Supabase Base URL**:
   - `https://bnsnnrlwfzxemmizknwy.supabase.co` is hardcoded across 4 separate files:
     - `Data/SupabaseDbService.cs`
     - `Licensing/HashVerifier.cs`
     - `ModuleSystem/SupabaseModuleProvider.cs`
     - `Updates/VelopackUpdateService.cs`
   - *Risk*: Relocating database/modules requires manual edits in multiple compiled files.

---

## Secret Risks

1. **Supabase Public Anon Key**:
   - The public-anon key is hardcoded in `SupabaseDbService.cs`. This key is intended to be public, but it relies heavily on correct PostgreSQL Row Level Security (RLS) to prevent write/delete operations from unauthorized users.
2. **Firebase service account key**:
   - `service_account.json` exists in the local workspace. It is git-ignored but its presence poses a risk of accidental inclusion in a git add.

---

## Naming Inconsistencies

1. **uiControlService.cs / uiControlService class**:
   - File and class name use lowercase `uiControlService`. This violates C# PascalCase conventions (should be `UiControlService`).
2. **Namespace standard**:
   - Most classes are under the `AutoJMS` namespace, but folder structures (like `FullStack/Services/`) do not always map cleanly to folder namespaces.

---

## Files That Should Not Be Touched Without Approval

To prevent breaking existing stable production features, these files are **frozen**:
- `Program.cs` (Startup & license hook)
- `Forms/Main.cs` / `Forms/Main.Designer.cs` (Tab configuration and Shell)
- `Licensing/TierRuntimePolicy.cs` (Tier policy enforcer)
- `Licensing/LicenseApiService.cs` (Auth handshake)
- `Licensing/JmsAuthTokenService.cs` (Capture & refresh token)
- `Updates/VelopackUpdateService.cs` (Update logic)
- `release/build-release.ps1` (Production release process)
- `installer/inno/AutoJMS.iss` (Installer definition)

---

## Safe Refactor Opportunities

1. **Rename uiControlService**:
   - Rename `uiControlService.cs` and `uiControlService` class to `UiControlService` (using PascalCase).
2. **Consolidate DB Connection Factories**:
   - Unify local SQLite connection factories into a single helper class in `FullStack/LocalDb/`.
3. **Move DkchManager**:
   - Extract `DkchManager` out of `WebViewAutomation.cs` into its own service class `Services/DkchManager.cs`.

---

## Risky Refactor Opportunities

1. **Move Main.cs logic to external services**:
   - Extracting WebView2 orchestration out of `Main.cs` could cause UI cross-thread violations.
2. **Unify Update XML and JSON manifests**:
   - Attempting to deprecate the XML path could break updating old clients already in the wild.

---

## Recommended Next Branches

* `agent/claude/refactor-ui-control-service`: Renaming and standardizing `uiControlService`.
* `agent/antigravity/consolidate-db-connection-factories`: Unifying local database connections.
