# Rollback Structure Migration

Date: 2026-06-03

Use this only if the executed structure migration must be undone before staging/commit.

## Preconditions

- Do not run destructive git commands.
- Do not delete files.
- Preserve `service_account.json` at root.
- Stop AutoJMS, MSBuild, and build server first if files are locked:

```powershell
taskkill /IM AutoJMS.exe /F /T
dotnet build-server shutdown
```

## Manual Rollback Map

Move production app files back from:

```text
src/AutoJMS/
```

to repository root, preserving original filenames and designer `.resx` pairs.

Move shared abstractions back from:

```text
src/AutoJMS.Abstractions/
```

to:

```text
ModuleProjects/AutoJMS.Abstractions/
```

Move archived legacy modules back from:

```text
archive/old-module-system/AutoJMS.RetryPolicy/
archive/old-module-system/AutoJMS.Selectors/
```

to:

```text
ModuleProjects/AutoJMS.RetryPolicy/
ModuleProjects/AutoJMS.Selectors/
```

Move backend/infra references back only if required:

```text
backend/render-license-server/ -> ServerStructure/Render server/
infra/supabase/                -> ServerStructure/Supabase/
infra/firebase/                -> ServerStructure/Firebase/
infra/github-release/          -> ServerStructure/Github Release/
```

Move release/tooling files back only if required:

```text
installer/inno/                         -> Installer/
release/                                -> Release/
tools/maintenance/build-modules.ps1     -> build-modules.ps1
tools/maintenance/upload-module.ps1     -> upload-module.ps1
tools/reactor/AutoJMS_Reactor.nrproj    -> AutoJMS_Reactor.nrproj
```

## Restore Path References

After moving files back, restore:

- `AutoJMS.slnx` project paths to root `AutoJMS.csproj` and `ModuleProjects\AutoJMS.Abstractions\AutoJMS.Abstractions.csproj`.
- Main project `ProjectReference` to `ModuleProjects\AutoJMS.Abstractions\AutoJMS.Abstractions.csproj`.
- Reactor project path to root `AutoJMS_Reactor.nrproj`.
- Release publish path to root `publish\win-x64` if using the old flow.
- Installer discovery path to old `Release\output\<channel>` if using the old flow.

## Verify Rollback

```powershell
dotnet restore AutoJMS.csproj
dotnet build AutoJMS.csproj -c Debug
```

If this fails after rollback, compare against `docs/migration/FILE_MOVE_MAP.md` and the current migration report.
