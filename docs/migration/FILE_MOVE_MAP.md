# File Move Map

Date: 2026-06-03

## Source

| From | To | Status |
| --- | --- | --- |
| `AutoJMS.csproj` | `src/AutoJMS/AutoJMS.csproj` | moved |
| `Program.cs` | `src/AutoJMS/Program.cs` | moved |
| `Main.*` | `src/AutoJMS/Forms/` | moved |
| `FullStackOperation.*` | `src/AutoJMS/Forms/` | moved |
| `frmLogin.*` | `src/AutoJMS/Forms/` | moved |
| `TabManager.cs` | `src/AutoJMS/Forms/TabManager.cs` | moved |
| `AppConfig.cs`, `RuntimeConfig.cs`, `SupabaseConfig.cs`, `SettingsManager.cs`, `AppPaths.cs`, `AppLogger.cs`, `AppVersion.cs`, `RuntimeConfigService.cs` | `src/AutoJMS/Config/` | moved |
| `LicenseApiService.cs`, `AuthStateService.cs`, `IAuthStateService.cs`, `JmsAuthStateService.cs`, `JmsAuthTokenService.cs`, `IntegrityService.cs`, `HashVerifier.cs`, `HashManifest.cs`, `TierDefinitions.cs`, `TierRuntimePolicy.cs` | `src/AutoJMS/Licensing/` | moved |
| `SupabaseManifestService.cs`, `SmallUpdateService.cs`, `MajorUpdateService.cs`, `VelopackUpdateService.cs` | `src/AutoJMS/Updates/` | moved |
| `SupabaseDbService.cs`, `DatabaseTracking.cs` | `src/AutoJMS/Data/` | moved |
| `SupabaseModels.cs`, `WaybillModels.cs` | `src/AutoJMS/Models/` | moved |
| `WebViewAutomation.cs` | `src/AutoJMS/Automation/` | moved |
| `WebViewHost.cs`, `UiThread.cs`, `uiControlService.cs` | `src/AutoJMS/UI/` | moved |
| `PrintService.cs`, `IPrintService.cs` | `src/AutoJMS/Printing/` | moved |
| `WaybillTrackingService.cs`, `ITrackingService.cs` | `src/AutoJMS/Tracking/` | moved |
| `JmsApiClient.cs`, `JmsResponseClassifier.cs`, `InventorySyncService.cs`, `GoogleSheetService.cs`, `ZaloChatService.cs`, `UserSettingsService.cs` | `src/AutoJMS/Services/` | moved |
| `ModuleSystem/` | `src/AutoJMS/ModuleSystem/` | moved |
| `Resources/` | `src/AutoJMS/Resources/` | moved |
| `Properties/` | `src/AutoJMS/Properties/` | moved |
| `favicon.ico`, `tier-definitions.json` | `src/AutoJMS/` | moved |
| root `modules/*.json` defaults | `src/AutoJMS/modules/` | created/minimal defaults |

## Module Projects

| From | To | Status |
| --- | --- | --- |
| `ModuleProjects/AutoJMS.Abstractions/` | `src/AutoJMS.Abstractions/` | moved |
| `ModuleProjects/AutoJMS.RetryPolicy/` | `archive/old-module-system/AutoJMS.RetryPolicy/` | archived |
| `ModuleProjects/AutoJMS.Selectors/` | `archive/old-module-system/AutoJMS.Selectors/` | archived |

## Backend And Infra

| From | To | Status |
| --- | --- | --- |
| `ServerStructure/Render server/` | `backend/render-license-server/` | moved |
| `ServerStructure/Supabase/` | `infra/supabase/` | moved |
| `ServerStructure/Firebase/` | `infra/firebase/` | moved |
| `ServerStructure/Github Release/` | `infra/github-release/` | moved |
| `supabase-migration.sql` | `infra/supabase/migrations/supabase-migration.sql` | moved |
| `LICENSE_DATA_SCHEMA.txt` | `infra/firebase/license-key-schema.txt` | moved |

## Release And Tools

| From | To | Status |
| --- | --- | --- |
| `Installer/` | `installer/inno/` | moved |
| `Release/` | `release/` | moved |
| `build-modules.ps1` | `tools/maintenance/build-modules.ps1` | moved |
| `upload-module.ps1` | `tools/maintenance/upload-module.ps1` | moved |
| `AutoJMS_Reactor.nrproj` | `tools/reactor/AutoJMS_Reactor.nrproj` | moved |

## Archive

| From | To | Status |
| --- | --- | --- |
| `AutoJMS.csproj.user` | `archive/needs-review/AutoJMS.csproj.user` | archived |
| `AutoJMS.json` | `archive/needs-review/AutoJMS.json` | archived |
| `COMMERCIAL_HARDENING_REPORT.md` | `archive/needs-review/COMMERCIAL_HARDENING_REPORT.md` | archived |
| `REACTOR_SAFE_CLASSES.md` | `archive/needs-review/REACTOR_SAFE_CLASSES.md` | archived |
| `Structure.txt` | `archive/needs-review/Structure.txt` | archived |
| `manualuse.txt` | `archive/needs-review/manualuse.txt` | archived |
| root `bin/`, `obj/`, `publish/` | `archive/old-generated-output/` | archived |
