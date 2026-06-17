# Fix Build Blockers

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Fix build blockers safely without changing runtime behavior.

## Do not modify

- Runtime logic unless explicitly required
- WinForms Designer files
- installer/inno/release scripts
- `ModuleSystem` deletion/removal

## Allowed files

- `src/AutoJMS/AutoJMS.csproj` only if the build failure is project-file related and the user allows it
- Minimal default JSON files under `modules/` if runtime requires them
- `docs/troubleshooting/build-errors.md`

## Steps

1. Run `dotnet restore` if allowed.
2. Run `dotnet build src/AutoJMS/AutoJMS.csproj -c Debug` if allowed.
3. Record exact errors.
4. Inspect `src/AutoJMS/AutoJMS.csproj` for missing `Content Include` files.
5. Prefer `Condition="Exists('...')"` for optional content.
6. If runtime requires defaults, create minimal valid JSON files.
7. Re-run build.
8. Document result.

## Acceptance criteria

- Debug build passes.
- No secret added.
- No runtime logic changed.
- `ModuleSystem` remains intact.
- Result documented in `docs/troubleshooting/build-errors.md`.

## Rollback notes

If project-file change breaks runtime packaging, revert the specific `Content Include` change or replace it with explicit default JSON files after verifying runtime need.


