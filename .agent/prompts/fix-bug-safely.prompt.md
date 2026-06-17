# Fix Bug Safely Prompt

Use this prompt when asked to fix a bug in AutoJMS.

## Prerequisites

Before fixing, read:
1. `AGENTS.md`
2. `.agent/context`
3. `.agent/rules`
4. `docs/audit/CODEBASE_AUDIT.md`
5. The specific file where the bug exists

Then:

- Identify root cause.
- List files to change.
- Propose smallest patch.
- Do not refactor unrelated code.
- Add logs only where useful.
- Keep old behavior unless requested.
- Provide acceptance criteria.

## Fix Bug Workflow

### Step 1: Locate Root Cause

1. Read the file containing the bug
2. Identify the exact line(s) causing the issue
3. Trace the data flow to understand why
4. Check logs for relevant error messages

### Step 2: Understand Impact

- What functionality is broken?
- Is it affecting BASE, ULTRA, or both?
- Are there side effects?
- What tests would verify the fix?

### Step 3: Propose Minimal Fix

**Rule**: Fix the smallest possible change.

```csharp
// BAD: Rewriting entire method
public async Task<List<Waybill>> GetWaybillsAsync()
{
    // ... 200 lines of new code ...
}

// GOOD: Fix specific bug
// Change line 45 from:
return result;

// To:
return result.Where(x => x.IsActive).ToList();
```

### Step 4: Add Debug Logging

Add temporary logging to understand the issue:

```csharp
AppLogger.Info($"[DEBUG] Fetching waybills, count={waybills?.Count ?? 0}");
// ... operation ...
AppLogger.Info($"[DEBUG] After filter, count={result?.Count ?? 0}");
```

### Step 5: Verify Tier Safety

If the fix touches background jobs:

```csharp
// Before starting any background work:
if (_tierPolicy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}
```

### Step 6: Define Acceptance Criteria

1. What should work after the fix?
2. What should NOT break?
3. How to test?

## Output Format

### Bug Description

[Clear description of the bug]

### Root Cause

[What causes the bug]

### Location

File: `path/to/file.cs`
Lines: 45-67

### Proposed Fix

[Code change, minimal as possible]

### Acceptance Criteria

1. [ ] [Expected behavior]
2. [ ] [Regression test]
3. [ ] [Tier verification]

### Risk Assessment

- Impact: Low/Medium/High
- Regression risk: Low/Medium/High
- Mitigation: [how to reduce risk]

## Rules

1. **DO NOT rewrite entire methods** - Fix the bug, not the world
2. **DO NOT add new features** - Just fix the bug
3. **DO NOT change interfaces** - Unless absolutely necessary
4. **DO add logging** - For debugging
5. **DO verify tier safety** - Check BASE/ULTRA guards
6. **DO test manually** - Before claiming fixed

## Common Bug Patterns

### Thread Safety

```csharp
// Bug: Accessing UI from background thread
tabTracking_dataView.DataSource = data;

// Fix: Marshal to UI thread
if (this.InvokeRequired)
{
    this.Invoke(() => tabTracking_dataView.DataSource = data);
}
```

### Null Reference

```csharp
// Bug: Null check missing
var count = data.Count;

// Fix: Add null check
if (data == null || data.Count == 0)
{
    return;
}
var count = data.Count;
```

### Tier Violation

```csharp
// Bug: Running on BASE tier
await SupabaseDbService.InitializeAsync();
await InventorySyncService.RunInventorySyncAsync();

// Fix: Check tier policy
if (_tierPolicy.EnableStartupInventorySync)
{
    await InventorySyncService.RunInventorySyncAsync();
}
```

### Token Handling

```csharp
// Bug: Not checking token validity
var response = await JmsApiClient.PostJsonAsync(url, json, authToken);

// Fix: Validate token first
if (!JmsAuthTokenService.IsValidJmsToken(authToken))
{
    AppLogger.Warning("Invalid token, refreshing...");
    await JmsAuthTokenService.ForceRefreshFromWebViewAsync();
}
```

## After Fix

1. Remove debug logging (or keep if valuable)
2. Verify with manual test
3. Check both BASE and ULTRA
4. Document in PR description
