# Stabilize Release Pipeline

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Make release/update behavior predictable across GitHub Releases, Supabase manifests, Inno Setup and Velopack.

## Do not modify

- installer/inno/release scripts unless this task explicitly asks for script changes
- Runtime update logic without a focused bug report
- Secrets or tokens

## Allowed files

- `docs/release/*`
- `docs/manual/*`
- `.agent/checklists/*`
- Release scripts only if explicitly approved in the specific task turn

## Steps

1. Verify build output names and locations.
2. Verify GitHub release tag format.
3. Verify GitHub assets include `RELEASES`, `.nupkg`, `Setup.exe`.
4. Verify Supabase manifests are small control files only.
5. Verify stable/beta behavior.
6. Verify About tab is manual trigger.
7. Document rollback process.

## Acceptance criteria

- No `.nupkg` upload to Supabase.
- No GitHub browser opened by app update.
- Stable/beta manifest entries are clear.
- Manual release checklist exists and is actionable.

## Rollback notes

Rollback by restoring prior `version-latest.json`, unpublishing bad GitHub release if needed, and restoring previous hash manifest.


