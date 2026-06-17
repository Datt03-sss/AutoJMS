# Bug Fix Workflow

Use this workflow when fixing bugs in AutoJMS.

## Before Starting

- [ ] Read `docs/audit/CODEBASE_AUDIT.md`
- [ ] Read relevant context files
- [ ] Read relevant rules files
- [ ] Understand the bug fully

## Required Workflow

1. Read agent context/rules.
2. Reproduce or locate error.
3. Find root cause.
4. Identify minimum files to change.
5. Patch only required code.
6. Add safe logs.
7. Build/test if possible.
8. Document result.

## Step 1: Reproduce the Bug

### Manual Testing

1. Identify steps to reproduce
2. Document exact error
3. Note affected tier (BASE/ULTRA)
4. Note affected feature

### Log Analysis

1. Check `debug.log` for relevant entries
2. Look for exceptions
3. Check tier policy resolution

## Step 2: Locate Root Cause

1. Read the file containing the bug
2. Find exact line(s)
3. Trace data flow
4. Understand why it fails

## Step 3: Design Fix

### Minimal Change Principle

Fix only the broken part. Do not refactor.

### Backward Compatibility

Ensure fix doesn't break existing functionality.

### Tier Safety

If fix touches background jobs:
```csharp
if (_tierPolicy.EnableBackgroundAutoSync)
{
    // Only for ULTRA
}
```

## Step 4: Implement Fix

1. Apply minimal change
2. Add debug logging
3. Test manually
4. Remove debug logging (or keep if valuable)

## Step 5: Verify

### Test Checklist

- [ ] Bug is fixed
- [ ] Existing features still work
- [ ] BASE tier is unaffected
- [ ] ULTRA tier is unaffected
- [ ] Logs show correct behavior

### Regression Testing

- [ ] HOME tab works
- [ ] DKCH1/DKCH2 works
- [ ] TRACKING works
- [ ] PRINT works
- [ ] ABOUT works
- [ ] Auth flow works
- [ ] Token capture works

## Step 6: Document

### PR/Commit Message

```
Fix: [Short description]

Root cause: [What caused the bug]
Fix: [What was changed]

Testing:
- [ ] Manual test
- [ ] Regression tests

See: docs/audit/CODEBASE_AUDIT.md
```

## Common Bug Categories

### Thread Safety Bugs

**Symptoms**: Cross-thread exceptions, UI freezes

**Fix**: Marshal to UI thread

### Null Reference Bugs

**Symptoms**: NullReferenceException

**Fix**: Add null checks

### Tier Violation Bugs

**Symptoms**: BASE running ULTRA features

**Fix**: Check TierRuntimePolicy

### Token Bugs

**Symptoms**: 401 errors, empty tokens

**Fix**: Check token capture and refresh

### WebView2 Bugs

**Symptoms**: Automation failures, timeouts

**Fix**: Check UI thread access, selectors
