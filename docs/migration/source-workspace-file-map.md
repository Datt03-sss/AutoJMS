# Source Workspace File Map

This map is plan-only. No file move is approved until the user says `EXECUTE_STRUCTURE_MIGRATION`.

Action values:

- `MOVE_AFTER_CONFIRM`
- `KEEP`
- `COPY_REFERENCE_ONLY`
- `LEGACY_AFTER_VERIFY`
- `NEED_VERIFY`

## Category legend

| Group | Category | Meaning | Target |
| ----- | -------- | ------- | ------ |
| A | Production source | Files compiled into AutoJMS app, WinForms resources, runtime content | `src/AutoJMS/` |
| B | Project phụ / contracts | Referenced or sample module projects/contracts | `src/AutoJMS.Abstractions/` or temporary `archive/old-module-system/` |
| C | Backend | Render, Supabase, Firebase, GitHub release references | `backend/` |
| D | Tooling | Release, installer, Reactor, maintenance scripts | `tools/` |
| E | Agent/docs | AI workspace and documentation | `.agent/`, `docs/` |
| F | Legacy / uncertain | Unknown, local, obsolete, output, or sensitive files requiring verification | `legacy/` or ignored after verify |

| Current path | Proposed path | Category | Compile? | Risk | Action |
| ------------ | ------------- | -------- | -------- | ---- | ------ |
| `.agent/` | `.agent/` | Agent workspace | no | low | KEEP |
| `docs/` | `docs/` | Docs | no | low | KEEP |
| `README.md` | `README.md` | Root metadata | no | low | KEEP |
| `AGENTS.md` | `AGENTS.md` | Agent/root metadata | no | low | KEEP |
| `NEXT_ACTIONS.md` | `NEXT_ACTIONS.md` | Agent/root metadata | no | low | KEEP |
| `.gitignore` | `.gitignore` | Root config | no | medium | KEEP |
| `AutoJMS.slnx` | `AutoJMS.slnx` or `AutoJMS.sln` | Solution | no | high | NEED_VERIFY |
| `src/AutoJMS/AutoJMS.csproj` | `src/AutoJMS/AutoJMS.csproj` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `AutoJMS.csproj.user` | legacy or ignored | Local IDE config | no | low | LEGACY_AFTER_VERIFY |
| `Program.cs` | `src/AutoJMS/Program.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `Main.cs` | `src/AutoJMS/Forms/Main.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `Main.Designer.cs` | `src/AutoJMS/Forms/Main.Designer.cs` | Production source / Designer | yes | high | MOVE_AFTER_CONFIRM |
| `Main.resx` | `src/AutoJMS/Forms/Main.resx` | Production resource | yes | high | MOVE_AFTER_CONFIRM |
| `FullStackOperation.cs` | `src/AutoJMS/Forms/FullStackOperation.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `FullStackOperation.Designer.cs` | `src/AutoJMS/Forms/FullStackOperation.Designer.cs` | Production source / Designer | yes | high | MOVE_AFTER_CONFIRM |
| `FullStackOperation.resx` | `src/AutoJMS/Forms/FullStackOperation.resx` | Production resource | yes | high | MOVE_AFTER_CONFIRM |
| `frmLogin.cs` | `src/AutoJMS/Forms/frmLogin.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `frmLogin.Designer.cs` | `src/AutoJMS/Forms/frmLogin.Designer.cs` | Production source / Designer | yes | high | MOVE_AFTER_CONFIRM |
| `frmLogin.resx` | `src/AutoJMS/Forms/frmLogin.resx` | Production resource | yes | high | MOVE_AFTER_CONFIRM |
| `AppConfig.cs` | `src/AutoJMS/Config/AppConfig.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `RuntimeConfig.cs` | `src/AutoJMS/Config/RuntimeConfig.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `SupabaseConfig.cs` | `src/AutoJMS/Config/SupabaseConfig.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `SettingsManager.cs` | `src/AutoJMS/Config/SettingsManager.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `AppPaths.cs` | `src/AutoJMS/Config/AppPaths.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `AppLogger.cs` | `src/AutoJMS/Config/AppLogger.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `AppVersion.cs` | `src/AutoJMS/Config/AppVersion.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `AuthStateService.cs` | `src/AutoJMS/Services/AuthStateService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `IAuthStateService.cs` | `src/AutoJMS/Services/IAuthStateService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `LicenseApiService.cs` | `src/AutoJMS/Services/LicenseApiService.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `JmsAuthStateService.cs` | `src/AutoJMS/Services/JmsAuthStateService.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `JmsAuthTokenService.cs` | `src/AutoJMS/Services/JmsAuthTokenService.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `JmsApiClient.cs` | `src/AutoJMS/Services/JmsApiClient.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `JmsResponseClassifier.cs` | `src/AutoJMS/Services/JmsResponseClassifier.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `InventorySyncService.cs` | `src/AutoJMS/Services/InventorySyncService.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `DatabaseTracking.cs` | `src/AutoJMS/Services/DatabaseTracking.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `SupabaseDbService.cs` | `src/AutoJMS/Data/SupabaseDbService.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `SupabaseManifestService.cs` | `src/AutoJMS/Update/SupabaseManifestService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `SmallUpdateService.cs` | `src/AutoJMS/Update/SmallUpdateService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `MajorUpdateService.cs` | `src/AutoJMS/Update/MajorUpdateService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `VelopackUpdateService.cs` | `src/AutoJMS/Update/VelopackUpdateService.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `IntegrityService.cs` | `src/AutoJMS/Security/IntegrityService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `HashVerifier.cs` | `src/AutoJMS/Security/HashVerifier.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `HashManifest.cs` | `src/AutoJMS/Security/HashManifest.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `TierDefinitions.cs` | `src/AutoJMS/Security/TierDefinitions.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `TierRuntimePolicy.cs` | `src/AutoJMS/Security/TierRuntimePolicy.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `WebViewAutomation.cs` | `src/AutoJMS/WebView/WebViewAutomation.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `WebViewHost.cs` | `src/AutoJMS/WebView/WebViewHost.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `UiThread.cs` | `src/AutoJMS/WebView/UiThread.cs` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `PrintService.cs` | `src/AutoJMS/Printing/PrintService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `IPrintService.cs` | `src/AutoJMS/Printing/IPrintService.cs` | Production source | yes | low | MOVE_AFTER_CONFIRM |
| `WaybillTrackingService.cs` | `src/AutoJMS/Tracking/WaybillTrackingService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `ITrackingService.cs` | `src/AutoJMS/Tracking/ITrackingService.cs` | Production source | yes | low | MOVE_AFTER_CONFIRM |
| `GoogleSheetService.cs` | `src/AutoJMS/Services/GoogleSheetService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `ZaloChatService.cs` | `src/AutoJMS/Services/ZaloChatService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `uiControlService.cs` | `src/AutoJMS/Services/uiControlService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `UserSettingsService.cs` | `src/AutoJMS/Services/UserSettingsService.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `TabManager.cs` | `src/AutoJMS/Forms/TabManager.cs` | Production source | yes | medium | MOVE_AFTER_CONFIRM |
| `SupabaseModels.cs` | `src/AutoJMS/Models/SupabaseModels.cs` | Production source | yes | low | MOVE_AFTER_CONFIRM |
| `WaybillModels.cs` | `src/AutoJMS/Models/WaybillModels.cs` | Production source | yes | low | MOVE_AFTER_CONFIRM |
| `ModuleSystem/` | `src/AutoJMS/ModuleSystem/` | Production source | yes | high | MOVE_AFTER_CONFIRM |
| `Properties/Resources.Designer.cs` | `src/AutoJMS/Properties/Resources.Designer.cs` | Production source / Designer | yes | high | MOVE_AFTER_CONFIRM |
| `Properties/Resources.resx` | `src/AutoJMS/Properties/Resources.resx` | Embedded resource | yes | high | MOVE_AFTER_CONFIRM |
| `Resources/` | `src/AutoJMS/Resources/` | Production resources | NEED VERIFY | high | MOVE_AFTER_CONFIRM |
| `favicon.ico` | `src/AutoJMS/favicon.ico` | Content/icon | output content | medium | MOVE_AFTER_CONFIRM |
| `tier-definitions.json` | `src/AutoJMS/tier-definitions.json` | Runtime content | output content | high | MOVE_AFTER_CONFIRM |
| `AutoJMS.json` | `src/AutoJMS/AutoJMS.json` or `backend/config example` | Config/template | no copy by explicit metadata | medium | NEED_VERIFY |
| `tools/reactor/AutoJMS_Reactor.nrproj` | `tools/reactor/tools/reactor/AutoJMS_Reactor.nrproj` | Tooling/Reactor config | no | high | MOVE_AFTER_CONFIRM |
| `src/AutoJMS.Abstractions/` | `src/AutoJMS.Abstractions/` | Project phụ / contracts | yes | high | MOVE_AFTER_CONFIRM |
| `archive/old-module-system/AutoJMS.RetryPolicy/` | `archive/old-module-system/` or `src/AutoJMS.RetryPolicy/` | Project phụ / sample module | not in solution | medium | NEED_VERIFY |
| `archive/old-module-system/AutoJMS.Selectors/` | `archive/old-module-system/` or `src/AutoJMS.Selectors/` | Project phụ / sample module | not in solution | medium | NEED_VERIFY |
| `backend/render-license-server/server.js` | `backend/render-license-server/server.js` | Backend | no | medium | COPY_REFERENCE_ONLY |
| `infra/firebase/config-key.json` | `backend/firebase/examples/config-key.example.json` | Backend/Firebase example | no | high | NEED_VERIFY |
| `infra/supabase/autojms-modules/` | `backend/supabase/manifests/` | Backend/Supabase examples | no | medium | COPY_REFERENCE_ONLY |
| `ServerStructure/Github release/` | `backend/github-release/` | Backend/release docs | no | low | COPY_REFERENCE_ONLY |
| `supabase-migration.sql` | `backend/supabase/migrations/supabase-migration.sql` | Backend/Supabase migration | no | medium | COPY_REFERENCE_ONLY |
| `LICENSE_DATA_SCHEMA.txt` | `backend/firebase/license-key-schema.md` | Backend/Firebase schema docs | no | low | COPY_REFERENCE_ONLY |
| `release/build-release.ps1` | `tools/release/build-release.ps1` | Tooling | no | high | MOVE_AFTER_CONFIRM |
| `release/build-release.bat` | `tools/release/build-release.bat` | Tooling | no | high | MOVE_AFTER_CONFIRM |
| `release/upload-only.bat` | `tools/release/upload-only.bat` | Tooling | no | high | MOVE_AFTER_CONFIRM |
| `release/_unlock_now.ps1` | `tools/maintenance/_unlock_now.ps1` | Tooling | no | medium | NEED_VERIFY |
| `installer/inno/AutoJMS.iss` | `tools/installer/AutoJMS.iss` | Tooling | no | high | MOVE_AFTER_CONFIRM |
| `installer/inno/build-installer.ps1` | `tools/installer/build-installer.ps1` | Tooling | no | high | MOVE_AFTER_CONFIRM |
| `installer/inno/build-installer.bat` | `tools/installer/build-installer.bat` | Tooling | no | high | MOVE_AFTER_CONFIRM |
| `installer/inno/clean-test-install.bat` | `tools/maintenance/clean-test-install.bat` | Tooling | no | medium | NEED_VERIFY |
| `installer/inno/redist/` | `tools/installer/redist/` | Tooling/runtime prerequisites | no | medium | MOVE_AFTER_CONFIRM |
| `build-modules.ps1` | `tools/maintenance/build-modules.ps1` | Tooling | no | medium | NEED_VERIFY |
| `upload-module.ps1` | `tools/maintenance/upload-module.ps1` | Tooling | no | medium | NEED_VERIFY |
| `bin/`, `obj/`, `publish/` | `artifacts/` or ignored | Build artifacts | no | low | LEGACY_AFTER_VERIFY |
| `release/output/` | `artifacts/release-output/` | Build artifacts | no | low | LEGACY_AFTER_VERIFY |
| `installer/inno/installer-output/` | `artifacts/installer-output/` | Build artifacts | no | low | LEGACY_AFTER_VERIFY |
| `.vs/`, `.vscode/` | ignored / editor config | Local IDE | no | low | NEED_VERIFY |
| `service_account.json` | do not move | Secret | no | critical | NEED_VERIFY |
| `COMMERCIAL_HARDENING_REPORT.md` | `docs/audit/` or `legacy/` | Docs/legacy | no | low | NEED_VERIFY |
| `REACTOR_SAFE_CLASSES.md` | `tools/reactor/README.md` or `docs/release/` | Docs/tooling | no | low | NEED_VERIFY |
| `Structure.txt` | `docs/architecture/` or `legacy/` | Docs/legacy | no | low | NEED_VERIFY |
| `manualuse.txt` | `docs/manual/` or `legacy/` | Docs/legacy | no | low | NEED_VERIFY |

