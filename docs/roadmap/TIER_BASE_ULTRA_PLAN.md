# Tier BASE / ULTRA Plan

## Current Policy

BASE:

- HOME
- DKCH
- TRACKING
- PRINT
- ABOUT
- Manual operations only
- No background inventory/database sync
- No FullStackOperation

ULTRA:

- All BASE features
- FullStackOperation standalone form
- Background inventory/database sync
- Realtime/dashboard workflows

## Stabilization Goals

- BASE không auto inventory sync.
- BASE không auto database tracking.
- BASE không auto FullStack.
- BASE manual tracking/print vẫn hoạt động.
- ULTRA mới chạy background jobs sau authToken.
- All checks go through `TierRuntimePolicy`.

## Verification Checklist

```txt
[ ] BASE opens HOME/DKCH/TRACKING/PRINT/ABOUT only
[ ] ABOUT remains last tab
[ ] BASE manual TRACKING works
[ ] BASE PRINT works
[ ] BASE does not start _autoSyncTimer
[ ] BASE does not run startup inventory sync
[ ] BASE does not run DatabaseTracking background jobs
[ ] BASE typing DASH does not open FullStackOperation
[ ] ULTRA typing DASH opens FullStackOperation
[ ] ULTRA background jobs wait for valid authToken
```

## Risks

- Hardcoded tier checks can drift from `TierRuntimePolicy`.
- Background timers can start before authToken is ready.
- FullStackOperation can fetch API too early if launched before UI/token readiness.

## Planned Fix Strategy

1. Inspect tier resolution logs.
2. Verify all background job starts are guarded by `TierRuntimePolicy`.
3. Verify manual BASE flows do not depend on ULTRA services.
4. Add minimal tests for `TierRuntimePolicy` when test project exists.

