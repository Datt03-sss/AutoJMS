# Project Structure Migration Plan

Date: 2026-06-03

Status: executed. See `docs/migration/PROJECT_STRUCTURE_MIGRATION_REPORT.md` for the completed result.

## Objective

Separate the AutoJMS repository into clear workspaces:

- `src/` for compiled .NET source.
- `backend/` for Render license server code.
- `infra/` for Supabase, Firebase, and GitHub release reference files.
- `installer/` for Inno Setup.
- `release/` for Velopack release pipeline.
- `tools/` for maintenance and Reactor tooling.
- `archive/` for legacy/review-only files.
- `.agent/` and `docs/` for agent context and documentation.

## Constraints

- Do not refactor runtime logic.
- Do not delete files.
- Preserve WinForms designer `.cs/.resx` relationships.
- Preserve tier policy behavior.
- Preserve update flow: Inno first install, Velopack in-app updates, GitHub binaries, Supabase manifests.
- Do not open or review secret contents during move.

## Verification Gate

The required build gate after migration:

```powershell
dotnet restore src\AutoJMS\AutoJMS.csproj
dotnet build src\AutoJMS\AutoJMS.csproj -c Debug
```

Latest result: build succeeded with 0 errors on 2026-06-03.
