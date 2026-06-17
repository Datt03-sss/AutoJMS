# FullStackOperation Fix Prompt

Use this prompt when fixing FullStackOperation form issues.

## Overview

FullStackOperation is an **ULTRA-only** independent form. It's NOT a tab - it's a separate window launched after MainForm.

## Lifecycle

```
MainForm.OnShown
    ↓
PreCreateFullStackForm() [ULTRA only]
    ↓
(Form hidden, waiting)
    ↓
User types "DASH" in HOME URL bar
    ↓
ShowFullStackForm()
    ↓
StartRealtimeRuntimeAsync() [After auth token]
    ↓
User closes form
    ↓
FullStackOperation_FormClosing
    ↓
Cleanup timers, services
```

## Tier Guards

### These Guards MUST Exist

```csharp
// Main.cs - PreCreateFullStackForm
if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
{
    AppLogger.Info($"FullStackOperation disabled for {_tierPolicy?.Tier ?? "BASE"}");
    return;
}

// Main.cs - ShowFullStackForm
if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
{
    AppLogger.Info($"ShowFullStackForm ignored for BASE");
    return;
}
```

## Common Issues

### Issue 1: Form Not Showing

**Check**:
1. Is license ULTRA tier?
2. Is `_tierPolicy.EnableFullStackOperation` true?
3. Did user type "DASH" correctly?
4. Is form already open?

**Debug**:
```csharp
AppLogger.Info($"[FULLSTACK] Show requested, tier={_tierPolicy?.Tier}");
AppLogger.Info($"[FULLSTACK] EnableFullStack={_tierPolicy?.EnableFullStackOperation}");
```

### Issue 2: Form Shows But No Data

**Check**:
1. Is auth token available?
2. Is `AuthStateService.Instance.IsAuthenticated` true?
3. Is `_isRealtimeStarted` true?

**Debug**:
```csharp
AppLogger.Info($"[FULLSTACK] Auth state: {AuthStateService.Instance.IsAuthenticated}");
AppLogger.Info($"[FULLSTACK] Realtime started: {_isRealtimeStarted}");
```

### Issue 3: Data Not Refreshing

**Check**:
1. Is `_autoRefreshTimer` running?
2. Is `LoadDataAndRefreshViewsAsync()` called?
3. Is `SupabaseDbService` connected?

**Debug**:
```csharp
AppLogger.Info($"[FULLSTACK] Timer enabled: {_autoRefreshTimer?.Enabled}");
AppLogger.Info($"[FULLSTACK] Last refresh: {_lastRefreshUtc}");
```

### Issue 4: Memory Leak

**Check**:
1. Are timers disposed?
2. Are event handlers unsubscribed?
3. Is `_fullStackForm = null` set after close?

**Fix**:
```csharp
// FullStackOperation_FormClosing
_autoRefreshTimer?.Stop();
_autoRefreshTimer?.Dispose();
_thoiHieuTimer?.Stop();
_thoiHieuTimer?.Dispose();
_cts.Cancel();
```

## Auth Token Handling

### FullStackOperation Waits for Token

```csharp
// Constructor
AuthStateService.Instance.TokenAcquired += async token => 
    await StartRealtimeRuntimeAsync();

// OnLoad
if (AuthStateService.Instance.IsAuthenticated)
    await StartRealtimeRuntimeAsync();
```

### Data Only Fetches After Token

```csharp
private async Task LoadDataAndRefreshViewsAsync()
{
    if (!AuthStateService.Instance.IsAuthenticated)
        return;

    // Safe to call APIs now
    var waybills = await SupabaseDbService.GetActiveWaybillsAsync();
    // ... update UI
}
```

## Cleanup Pattern

```csharp
private void FullStackOperation_FormClosing(object sender, FormClosingEventArgs e)
{
    // 1. Signal closing (stops UI updates)
    _isClosing = true;

    // 2. Stop timers
    _autoRefreshTimer?.Stop();
    _autoRefreshTimer?.Dispose();
    _thoiHieuTimer?.Stop();
    _thoiHieuTimer?.Dispose();

    // 3. Cancel background tasks
    _cts.Cancel();

    // 4. Stop services
    _zaloChatService?.StopAutoReminder();
}
```

## UI Ready Guard

```csharp
private volatile bool _uiReady;

// OnLoad - after UI setup
_uiReady = true;

// In timer callbacks
if (_isClosing || !_uiReady) return;
```

## Data Binding

```csharp
// Set data with guards
private void SetThoiHieuData(List<ThoiHieuRow> rows)
{
    if (_isClosing) return;

    if (!_uiReady)
    {
        _pendingThoiHieuRows = rows;  // Cache until ready
        return;
    }

    // Safe to update UI
    _thoiHieuGrid.DataSource = rows;
}
```

## Form States

| State | Description | Guard |
|-------|-------------|-------|
| IDLE | Form created, no token | Wait for token |
| ACTIVE | Token available, refreshing | Run refresh |
| CLOSING | Form closing | Stop everything |

## Testing

### Test ULTRA Tier

1. Login with ULTRA license
2. Check logs: "FullStackOperation pre-created"
3. Type "DASH"
4. Verify form appears
5. Verify data loads
6. Close form
7. Verify cleanup

### Test BASE Tier

1. Login with BASE license
2. Check logs: "FullStackOperation disabled for BASE"
3. Type "DASH"
4. Verify form does NOT appear

## DO NOT

1. **DO NOT remove tier guards**
2. **DO NOT call APIs before auth token**
3. **DO NOT skip `_isClosing` checks**
4. **DO NOT skip `_uiReady` checks**
5. **DO NOT forget to dispose timers**

## After Fix

1. Test with ULTRA license
2. Test with BASE license
3. Verify data loads after auth
4. Verify cleanup on close
5. Check for memory leaks
