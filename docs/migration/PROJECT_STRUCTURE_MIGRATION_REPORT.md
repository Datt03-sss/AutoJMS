# Project Structure Migration Report

Date: 2026-06-03  
Branch: `chore/restructure-workspaces`

## Result

Structure migration was executed. Production C# source was moved under `src/AutoJMS/`; shared abstractions moved under `src/AutoJMS.Abstractions/`; backend, infra, installer, release, and tooling files were separated into their own workspaces.

`dotnet restore src/AutoJMS/AutoJMS.csproj` succeeded.

`dotnet build src/AutoJMS/AutoJMS.csproj -c Debug` succeeded with 0 errors. Latest incremental run reported 3 warnings; the earlier full compile in this migration session reported 30 pre-existing warnings.

`dotnet build AutoJMS.slnx -c Debug` also succeeded with 0 errors and 3 warnings.

## Current Root Layout

```text
.agent/
.vscode/
archive/
backend/
docs/
infra/
installer/
release/
src/
tests/
tools/
.gitignore
AGENTS.md
AutoJMS.slnx
NEXT_ACTIONS.md
README.md
service_account.json
```

`service_account.json` remains at root and is ignored by `.gitignore`; content was not opened during this migration.

## Production Source

```text
src/
├── AutoJMS/
│   ├── AutoJMS.csproj
│   ├── Program.cs
│   ├── Forms/
│   ├── Config/
│   ├── Licensing/
│   ├── Updates/
│   ├── Data/
│   ├── Models/
│   ├── Automation/
│   ├── UI/
│   ├── Printing/
│   ├── Tracking/
│   ├── Services/
│   ├── ModuleSystem/
│   ├── Resources/
│   ├── Properties/
│   ├── modules/
│   ├── favicon.ico
│   └── tier-definitions.json
└── AutoJMS.Abstractions/
    └── AutoJMS.Abstractions.csproj
```

## Backend And Infra

```text
backend/render-license-server/
infra/supabase/
infra/firebase/
infra/github-release/
```

Moved from the previous `ServerStructure/*` layout.

## Release And Tooling

```text
installer/inno/
release/
tools/maintenance/
tools/reactor/
```

Updated path references:

- `AutoJMS.slnx` now references `src\AutoJMS\AutoJMS.csproj` and `src\AutoJMS.Abstractions\AutoJMS.Abstractions.csproj`.
- `src/AutoJMS/AutoJMS.csproj` now references `..\AutoJMS.Abstractions\AutoJMS.Abstractions.csproj`.
- Reactor path now points to `tools\reactor\AutoJMS_Reactor.nrproj`.
- Release publish output now points to `artifacts\publish\win-x64`.
- Inno build discovery now points to `release\output\<channel>`.

## Archive

```text
archive/old-module-system/
archive/old-generated-output/
archive/needs-review/
```

Archived items include legacy module projects, old root generated outputs, local IDE/user files, and review-only documents.

## Module Defaults

Minimal module JSON defaults now exist under:

```text
src/AutoJMS/modules/
├── active_modules.json
├── app-manifest.json
├── config.json
├── modules-cache.json
└── selectors.json
```

The project file still keeps `Exists(...)` guards for these content files.

## Security Notes

- No secret contents were opened for review.
- `infra/firebase/config-key.json` is listed in `docs/migration/SECRET_REVIEW_REQUIRED.md`.
- Root `service_account.json` remains untouched and ignored.
- `.gitignore` includes secret patterns for service account/key files and generated outputs.

## Verification

Commands run:

```powershell
dotnet restore src\AutoJMS\AutoJMS.csproj
dotnet build src\AutoJMS\AutoJMS.csproj -c Debug
dotnet build AutoJMS.slnx -c Debug
```

Build result:

```text
Build succeeded.
3 Warning(s)
0 Error(s)
```

Known warnings are pre-existing categories. The latest incremental build reported `PdfiumViewer` compatibility and `WindowsBase` version conflict warnings. The earlier full compile also reported nullable warnings, obsolete Google credential APIs, unawaited calls, and unused fields.

## Git Status Note

`git status --short` reports the workspace as untracked (`??`) because this checkout has no tracked baseline. This migration should be reviewed as a filesystem-level restructure before staging.
