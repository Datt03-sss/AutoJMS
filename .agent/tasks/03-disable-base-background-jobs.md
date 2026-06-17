# Disable BASE Background Jobs

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Guarantee BASE runs manual workflows only and never starts ULTRA background inventory/database jobs.

## Do not modify

- Manual TRACKING flow
- Manual PRINT flow
- DKCH flow
- ABOUT update flow

## Allowed files

- `TierRuntimePolicy.cs`
- `Main.cs` tier guard points only
- `tier-definitions.json`
- Docs/checklists

## Steps

1. Inspect `TierRuntimePolicy.Resolve`.
2. Inspect all timer/background job start points.
3. Verify BASE flags are false for inventory/database/fullstack.
4. Verify manual tracking/print remain true.
5. Add or adjust minimal guard if needed.
6. Test BASE and ULTRA behavior.

## Acceptance criteria

- BASE does not start `_autoSyncTimer`.
- BASE does not run startup inventory sync.
- BASE does not run database tracking.
- BASE cannot open FullStackOperation.
- BASE manual TRACKING and PRINT still work.
- ULTRA still runs allowed background jobs after authToken.

## Rollback notes

Revert the smallest guard change if manual BASE flows are blocked unexpectedly.

