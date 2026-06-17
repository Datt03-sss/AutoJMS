# Production Workspace Boundary

Date: 2026-06-03

## Production Source

Only the following source projects are part of the active AutoJMS solution:

```text
src/AutoJMS/AutoJMS.csproj
src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj
```

`AutoJMS.slnx` references only these projects.

## Non-Production Workspaces

These folders are not production WinForms source:

| Folder | Purpose |
| --- | --- |
| `.agent/` | Agent rules, prompts, skills, workflows, checklists |
| `docs/` | Architecture, audit, release, troubleshooting, migration docs |
| `backend/` | Render Node/Express license server |
| `infra/` | Supabase/Firebase/GitHub release reference structure |
| `installer/` | Inno Setup first-install/reinstall tooling |
| `release/` | Velopack release/update packaging tooling |
| `tools/` | Maintenance scripts and Reactor project |
| `archive/` | Legacy/review-only files and old generated outputs |
| `tests/` | Test workspace |

## Rules

- Do not put docs, backend files, installer scripts, release outputs, or old module projects inside `src/AutoJMS/`.
- Do not add archived projects to `AutoJMS.slnx` unless explicitly reactivated.
- Do not track real secret files. See `docs/migration/SECRET_REVIEW_REQUIRED.md`.
- Do not modify production code merely to satisfy folder aesthetics.

## Build Command

```powershell
dotnet build src\AutoJMS\AutoJMS.csproj -c Debug
```
