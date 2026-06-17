# FullStackOperation Rules

## Overview

FullStackOperation is an **ULTRA-only** independent form. It is NOT a tab in MainForm.

## Tier Enforcement

### Guards (NEVER REMOVE)

```csharp
// Main.cs - PreCreateFullStackForm
private void PreCreateFullStackForm()
{
    if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
    {
        AppLogger.Info($"FullStackOperation disabled for {_tierPolicy?.Tier ?? "BASE"}");
        return;  // ← DO NOT REMOVE
    }
    // ... create form
}

// Main.cs - ShowFullStackForm
private void ShowFullStackForm()
{
    if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
    {
        AppLogger.Info($"ShowFullStackForm ignored for BASE");
        return;  // ← DO NOT REMOVE
    }
    // ... show form
}
```

### Tier Check Rules

1. Always use `TierRuntimePolicy` for tier checks
2. Never check `CurrentTier == "ULTRA"` directly
3. Never remove tier guards

## Launch Flow

### Step 1: Pre-create (Main.OnShown)

```csharp
protected override void OnShown(EventArgs e)
{
    base.OnShown(e);
    this.BeginInvoke(new Action(() =>
    {
        if (!_ultraLaunched)
        {
            _ultraLaunched = true;
            PreCreateFullStackForm();  // ULTRA only
        }
    }));
}
```

### Step 2: Show (User Types "DASH")

```csharp
private void TabHome_urlBar_KeyDown(object sender, KeyEventArgs e)
{
    if (e.KeyCode == Keys.Enter && 
        tabHome_urlBar.Text.Trim().Equals("DASH", StringComparison.OrdinalIgnoreCase))
    {
        e.SuppressKeyPress = true;
        ShowFullStackForm();  // ULTRA only
        tabHome_urlBar.Text = "";
    }
}
```

### Step 3: Activate (After Auth Token)

```csharp
private async Task StartRealtimeRuntimeAsync()
{
    if (_isRealtimeStarted) return;
    _isRealtimeStarted = true;

    // Start auto-refresh timer
    _autoRefreshTimer = new System.Windows.Forms.Timer();
    _autoRefreshTimer.Interval = 2 * 60 * 1000;  // 2 minutes
    _autoRefreshTimer.Tick += async (s, ev) => await LoadDataAndRefreshViewsAsync();
    _autoRefreshTimer.Start();

    // Initial load
    await LoadDataAndRefreshViewsAsync();
}
```

## Form Lifecycle

### States

1. **IDLE**: Form created, waiting for auth token
2. **ACTIVE**: Auth token acquired, realtime running
3. **CLOSING**: Form closing, cleanup in progress

### State Transitions

```
Pre-create (IDLE)
    ↓
Auth token arrives
    ↓
StartRealtimeRuntimeAsync (IDLE → ACTIVE)
    ↓
FormClosing event
    ↓
Stop timers, cleanup (ACTIVE → CLOSING)
    ↓
Form disposed
```

## Components

### Tabs

| Tab | Purpose | Data Source |
|-----|---------|-------------|
| Dashboard (tabDash) | Realtime waybill list | Supabase DB |
| Chat (tabChat) | Zalo chat integration | ZaloChatService |
| Thời hiệu | SLA monitoring | Supabase DB |

### Key Services

| Service | Purpose |
|---------|---------|
| ZaloChatService | Zalo message integration |
| SupabaseDbService | Waybill data |
| InventorySyncService | Inventory fetch |

### Timers

| Timer | Interval | Purpose |
|-------|----------|---------|
| _autoRefreshTimer | 2 minutes | Refresh dashboard/chat data |
| _thoiHieuTimer | 1 minute | SLA monitoring |
| _alertCheckTimer | 30 seconds | Alert notifications |

## Auth Token Handling

### FullStackOperation Subscribes to Auth

```csharp
// Constructor
AuthStateService.Instance.TokenAcquired += async token => 
    await StartRealtimeRuntimeAsync();
```

### Token Validation

```csharp
// On Load
if (AuthStateService.Instance.IsAuthenticated)
    await StartRealtimeRuntimeAsync();

// On TokenAcquired event
await StartRealtimeRuntimeAsync();
```

### Data Fetching (After Auth Only)

```csharp
private async Task LoadDataAndRefreshViewsAsync()
{
    // Check if authenticated
    if (!AuthStateService.Instance.IsAuthenticated)
        return;

    // Fetch data using JMS authToken
    var waybills = await SupabaseDbService.GetActiveWaybillsAsync();
    // ... update UI
}
```

## Cleanup

### On FormClosing

```csharp
private void FullStackOperation_FormClosing(object sender, FormClosingEventArgs e)
{
    // Stop background work
    _isClosing = true;
    _autoRefreshTimer?.Stop();
    _thoiHieuTimer?.Stop();
    _cts.Cancel();

    // Stop Zalo auto-reminder
    _zaloChatService?.StopAutoReminder();
}
```

### Lifecycle Guards

```csharp
private volatile bool _uiReady;    // Set after UI initialized
private volatile bool _isClosing;  // Set on FormClosing

// Use in timer callbacks
if (_isClosing) return;
if (!_uiReady) return;
```

## Update Flow Integration

### PrepareForUpdateAsync (Main.cs)

```csharp
private async Task PrepareForUpdateAsync(CancellationToken ct)
{
    // ... stop other services ...

    // Close FullStackOperation
    if (_fullStackForm != null && !_fullStackForm.IsDisposed)
    {
        _fullStackForm.Close();
        _fullStackForm.Dispose();
        _fullStackForm = null;
    }
}
```

## Grid Management

### SetupGrids

```csharp
private void SetupGrids()
{
    ApplyStandardGridSettings(tabDash_dataGridView);
    ApplyStandardGridSettings(uiDataGridView2);
    ApplyStandardGridSettings(tabChat_dataGrid);

    // Configure columns
    tabDash_dataGridView.AutoGenerateColumns = false;
    tabDash_dataGridView.Columns.Clear();
    tabDash_dataGridView.Columns.AddRange(...);
}
```

### Data Binding

```csharp
private void SetThoiHieuData(List<ThoiHieuRow> rows)
{
    if (_isClosing) return;
    if (!_uiReady)
    {
        _pendingThoiHieuRows = rows;  // Cache until UI ready
        return;
    }

    // Update grid
    _thoiHieuGridData = rows;
    _thoiHieuGrid.DataSource = null;
    _thoiHieuGrid.DataSource = _thoiHieuGridData;
}
```

## Error Handling

### Network Errors

```csharp
try
{
    var data = await SupabaseDbService.GetActiveWaybillsAsync();
    // Update UI
}
catch (Exception ex)
{
    AppLogger.Warning($"FullStack data load failed: {ex.Message}");
    // Show error in UI
}
```

### Timer Errors

```csharp
_autoRefreshTimer.Tick += async (s, ev) =>
{
    try
    {
        await LoadDataAndRefreshViewsAsync();
    }
    catch (Exception ex)
    {
        AppLogger.Warning($"Auto-refresh failed: {ex.Message}");
    }
};
```

## Testing

### Manual Testing Steps

1. Login with ULTRA license
2. Verify FullStackOperation pre-created (check logs)
3. Type "DASH" in HOME URL bar
4. Verify form appears
5. Verify tabs work
6. Close form
7. Verify cleanup

### BASE Testing

1. Login with BASE license
2. Type "DASH" in HOME URL bar
3. Verify form does NOT appear
4. Check logs for "FullStackOperation disabled for BASE"

## Common Issues

### Issue: Form Not Showing

**Check:**
1. Is license ULTRA tier?
2. Is `_tierPolicy.EnableFullStackOperation` true?
3. Did user type "DASH" correctly?
4. Is form already shown?

### Issue: Data Not Loading

**Check:**
1. Is auth token available?
2. Is Supabase connected?
3. Check `_isRealtimeStarted` flag
4. Check `AuthStateService.Instance.IsAuthenticated`

### Issue: Memory Leak

**Check:**
1. Are all timers disposed in FormClosing?
2. Are event handlers unsubscribed?
3. Is `_fullStackForm = null` set after dispose?
