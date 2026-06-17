# FullStackOperation Errors

## Current Verified Behavior

Error:

```text
Tier hiện tại là 'BASE', cần ULTRA để mở
```

Meaning:

- This is expected if the current license tier resolves to BASE.
- `FullStackOperation` is ULTRA-only.
- It is a standalone form, not a tab.
- `Main.ShowFullStackForm()` returns without opening the form when `TierRuntimePolicy.EnableFullStackOperation` is false.

Verification points:

- Confirm Render license response contains `license.tier = "ULTRA"` or root `tier = "ULTRA"`.
- Confirm `tier-definitions.json` grants `FULLSTACK_OPERATION` to ULTRA.
- Confirm `TierRuntimePolicy.Resolve()` logs `fullStack=true`.
- If any of those are missing, mark the license/tier path `NEED VERIFY`.

Do not bypass the tier gate for BASE.

## Common Issues

### Issue: Form Not Showing

**Check**:
1. Is license ULTRA tier?
2. Is `_tierPolicy.EnableFullStackOperation` true?
3. Did user type "DASH"?

**Logs to check**:
```
[INFO] FullStackOperation disabled for BASE
```

### Issue: Data Not Loading

**Check**:
1. Is auth token available?
2. Is Supabase connected?
3. Is `_isRealtimeStarted` true?

**Logs to check**:
```
[INFO] FullStack UI initialized
[INFO] FullStackOperation activating — token acquired
```

### Issue: Memory Leak

**Check**:
1. Are timers disposed?
2. Are event handlers unsubscribed?

**Fix**: Check FormClosing cleanup

## Debug

Add logging:
```csharp
AppLogger.Info($"[FULLSTACK] State: {state}");
```
