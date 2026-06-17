# FullStackOperation Lifecycle

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Stabilize FullStackOperation lifecycle without turning it into a tab or exposing it to BASE.

## Do not modify

- BASE tabs
- ABOUT tab position
- FullStackOperation tier gate
- Release scripts

## Allowed files

- `FullStackOperation.cs`
- `Main.cs` only for lifecycle launch/close guards
- Docs/checklists

## Steps

1. Map pre-create, show, close, dispose and reopen flow.
2. Verify launch only after `MainForm.Shown`.
3. Verify authToken exists before API fetch.
4. Guard grid updates when controls are disposed or not ready.
5. Ensure background tasks/timers cancel on close.
6. Test close/reopen and BASE blocking.

## Acceptance criteria

- ULTRA can open FullStackOperation.
- BASE cannot open FullStackOperation.
- Closing form cancels background work.
- No grid update after dispose.
- Reopen works.

## Rollback notes

Revert lifecycle guards if form no longer opens on ULTRA; keep cancellation notes for follow-up.

