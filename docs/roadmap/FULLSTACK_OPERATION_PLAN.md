# FullStackOperation Stabilization Plan

## Current Contract

- `FullStackOperation` is ULTRA-only.
- It is a separate visible form, not a tab.
- It should be launched after `MainForm.Shown`.
- It should fetch JMS/Supabase data only after authToken is available.

## Stabilization Goals

- UI ready before data fetch.
- No grid update when grid is null, disposed, or handle is not ready.
- Close form cancels background tasks.
- Reopen works after close/dispose.
- BASE cannot create or show the form.

## Lifecycle Checklist

```txt
[ ] MainForm.Shown has completed
[ ] TierRuntimePolicy.EnableFullStackOperation is true
[ ] Form controls initialized
[ ] authToken is valid 32-hex JMS token
[ ] Background tasks have CancellationToken
[ ] Grid updates check IsDisposed/IsHandleCreated
[ ] FormClosed cancels tasks and clears references
```

## Risk Areas

- Pre-create and show race.
- Background task updates disposed controls.
- FullStack realtime starts before authToken.
- DataGridView performance with large row counts.
- Zalo/chat/timer flows continuing after close.

## Planned Fix Strategy

1. Map current lifecycle events.
2. Identify every background task/timer started by the form.
3. Add cancellation/guard points only where needed.
4. Avoid changing UI layout unless a UI bug requires it.
5. Validate close/reopen and BASE blocking behavior.

