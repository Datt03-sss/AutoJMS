# Read Context First

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Ensure every coding task starts with the same project understanding and constraints before any patch.

## Do not modify

- Production code
- Designer files
- Project/solution files
- installer/inno/release scripts
- `server.js`

## Allowed files

- Markdown notes in `.agent/` and `docs/`
- Task-specific files only after the next task explicitly allows them

## Steps

1. Read `AGENTS.md`.
2. Read `.agent/context/*`.
3. Read `.agent/rules/*`.
4. Read `docs/audit/CODEBASE_AUDIT.md`.
5. State planned files to change.
6. Only then edit files.

## Acceptance criteria

- Context was read.
- Planned file list was stated.
- No production changes were made before context review.

## Rollback notes

Documentation-only. Revert the specific markdown task note if incorrect.


