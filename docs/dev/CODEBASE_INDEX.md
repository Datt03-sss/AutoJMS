# Codebase Index

Index này dựa trên file/folder hiện có trong workspace. Nếu một mục không xác minh được từ file listing hiện tại, ghi `NEED VERIFY`.

## Root app files

- `Program.cs`: startup/license/services/integrity.
- `Main.cs`: MainForm and BASE tabs.
- `Main.Designer.cs`: WinForms designer for MainForm.
- `FullStackOperation.cs`: ULTRA operation form.
- `FullStackOperation.Designer.cs`: WinForms designer for FullStackOperation.
- `frmLogin.cs`: license/login dialog.
- `src/AutoJMS/AutoJMS.csproj`: .NET 8 WinForms project.
- `AutoJMS.slnx`: solution file.
- `AutoJMS.json`: local/default settings template.
- `tier-definitions.json`: tier definitions shipped at root.

## Services

- `AuthStateService.cs`
- `GoogleSheetService.cs`
- `IAuthStateService.cs`
- `IntegrityService.cs`
- `InventorySyncService.cs`
- `IPrintService.cs`
- `ITrackingService.cs`
- `JmsAuthStateService.cs`
- `JmsAuthTokenService.cs`
- `LicenseApiService.cs`
- `MajorUpdateService.cs`
- `PrintService.cs`
- `RuntimeConfigService.cs`
- `SmallUpdateService.cs`
- `SupabaseDbService.cs`
- `SupabaseManifestService.cs`
- `uiControlService.cs`
- `UserSettingsService.cs`
- `VelopackUpdateService.cs`
- `WaybillTrackingService.cs`
- `ZaloChatService.cs`

## Modules

- `ModuleSystem/`: dynamic module loading/provider/update infrastructure.
- `archive/old-module-system/`: module projects including abstractions/selectors/retry policy.

## Backend references

- `backend/render-license-server/`: Render Node/Express license server source.
- `infra/supabase/`: Supabase Storage bucket example files.
- `infra/firebase/`: Firebase schema/config samples. Secret-like files require review before tracking.

## Release

- `installer/inno/`: Inno Setup scripts, redistributables and installer output.
- `release/`: release/build/upload scripts and output folders.

## Docs and agent

- `docs/`: developer docs, architecture, API, release, manual, roadmap, troubleshooting.
- `.agent/`: agent context, rules, prompts, tasks, task board, templates, decisions and handoff notes.


