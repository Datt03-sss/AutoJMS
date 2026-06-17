# Tier Policy

## Tier Definitions

### BASE Tier

The core, stable tier for all users.

**Tabs Available:**
- HOME (JMS WebView2 browser)
- DKCH (Đăng ký chuyển hoàn automation)
- TRACKING (Manual waybill tracking)
- PRINT (Label printing)
- ABOUT (Version info, update check)

**Features:**

| Feature | BASE |
|---------|------|
| Manual tracking | ✓ |
| Manual print | ✓ |
| DKCH automation | ✓ |
| Inventory sync (auto) | ✗ |
| Database tracking (auto) | ✗ |
| Background sync timer | ✗ |
| FullStackOperation form | ✗ |
| Zalo chat integration | ✗ |

**Background Jobs (must NOT run on BASE):**
- `_autoSyncTimer` (every 1 second, triggers inventory sync)
- `RunStartupSyncAsync` (startup inventory sync)
- `ExecuteSyncWorkflowAsync` (inventory fetch + DB tracking)
- `InventorySyncService.RunInventorySyncAsync`
- `DatabaseTracking.RunBackgroundTrackingAsync`

**Behavior:**
- Manual tracking via `btnSearch_Click` works normally
- Print via `ExecutePrintAsync` works normally
- DKCH via `DkchManager` works normally
- `_autoSyncTimer` is never started
- No FullStackOperation form is created

### ULTRA Tier

Advanced tier with full automation capabilities.

**Additional Features:**

| Feature | ULTRA |
|---------|-------|
| FullStackOperation form | ✓ |
| Inventory sync (auto) | ✓ |
| Database tracking (auto) | ✓ |
| Background sync timer | ✓ |
| Realtime dashboard | ✓ |
| Thời hiệu monitoring | ✓ |
| Zalo chat integration | ✓ |

**Behavior:**
- All BASE features work the same
- `_autoSyncTimer` starts after MainForm is shown
- FullStackOperation is pre-created in `OnShown`
- Form shows when user types "DASH" in HOME URL bar
- Inventory sync runs every 30 minutes (8AM-11:30PM)
- Database tracking runs for new waybills

## How Tier is Determined

### At License Verification

```
LicenseApiService.VerifyLicenseSecureAsync
    ↓
Parse "tier" from server response
    ↓
Return VerifyResult.Tier = "BASE" | "ULTRA"
    ↓
Program.Main receives tier string
    ↓
new Main(sessionTier)  ← passed to Main constructor
```

### Tier Definition Source

`tier-definitions.json` shipped with app:

```json
{
  "tiers": {
    "BASE": {
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": [],
      "backgroundJobs": {
        "inventorySync": false,
        "databaseTracking": false,
        "autoSyncTimer": false,
        "fullStackRealtime": false
      }
    },
    "ULTRA": {
      "inherits": "BASE",
      "tabs": ["HOME", "DKCH", "TRACKING", "PRINT", "ABOUT"],
      "forms": [{
        "name": "FULLSTACK_OPERATION",
        "type": "VISIBLE_FORM",
        "launch": "AFTER_MAINFORM_SHOWN",
        "fetchApiAfterAuthToken": true
      }],
      "backgroundJobs": {
        "inventorySync": true,
        "databaseTracking": true,
        "autoSyncTimer": true,
        "fullStackRealtime": true
      }
    }
  }
}
```

### TierRuntimePolicy Resolution

```
TierRuntimePolicy.Resolve(tier, definitions)
    ↓
Normalize tier name
    ↓
Check tier-definitions.json for FULLSTACK_OPERATION form
    ↓
isUltra = (hasFullStack OR normalized == "ULTRA")
    ↓
Return TierRuntimePolicy with flags
```

## Tier Enforcement Points

### Main Constructor

```csharp
// Line 107-108
_tierPolicy = TierRuntimePolicy.Resolve(CurrentTier);

// Line 165-174
_autoSyncTimer.Interval = 1000;
_autoSyncTimer.Tick += async (s, e) => await HandleAutoSyncTickAsync(_appCts.Token);
if (_tierPolicy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}
```

### Main.OnLoad

```csharp
// Line 354-362
if (_tierPolicy.EnableStartupInventorySync || _tierPolicy.EnableStartupDatabaseTracking)
{
    _ = RunStartupSyncAsync(_appCts.Token);
}
else
{
    AppLogger.Info($"Background inventory sync disabled for {_tierPolicy.Tier}.");
    AppLogger.Info($"Background database tracking disabled for {_tierPolicy.Tier}.");
}
```

### Main.OnShown

```csharp
// Line 870-878
this.BeginInvoke(new Action(() =>
{
    if (!_ultraLaunched)
    {
        _ultraLaunched = true;
        PreCreateFullStackForm();
    }
}));
```

### PreCreateFullStackForm

```csharp
// Line 883-886
if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
{
    AppLogger.Info($"FullStackOperation disabled for {_tierPolicy?.Tier ?? "BASE"}");
    return;
}
```

### ShowFullStackForm

```csharp
// Line 904-907
if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
{
    AppLogger.Info($"ShowFullStackForm ignored for {_tierPolicy?.Tier ?? "BASE"} — ULTRA only.");
    return;
}
```

## Manual Operations (Always Allowed)

Both tiers support manual operations initiated by user:

| Operation | Trigger | Service |
|-----------|---------|---------|
| Tracking search | btnSearch_Click | WaybillTrackingService |
| Print | tabPrint_btnPrint | PrintService |
| DKCH1 | tabDKCH_btnDKCH1 | DkchManager |
| DKCH2 | tabDKCH_btnDKCH2 | DkchManager |
| Export Excel | btn_Export_Click | WaybillTrackingService |
| Upload BUMP | tabTracking_btnUpload | GoogleSheetService |

## Background Job Timing

### Auto Sync Timer

```csharp
_autoSyncTimer.Interval = 1000;  // 1 second

// HandleAutoSyncTickAsync (every 1 second)
//   ↓
// ShouldRunAutoSyncNow() → true if in window (8AM-11:30PM) and slot due
//   ↓
// ExecuteSyncWorkflowAsync
//   ↓
// SupabaseDbService.TryAcquireInventoryLease
//   ↓
// InventorySyncService.FetchAllInventoryWaybillsWithRetryAsync
//   ↓
// SupabaseDbService.UpsertNewWaybillsOnlyAsync
//   ↓
// DatabaseTracking.RunBackgroundTrackingAsync
```

### Manual Sync Cooldown

- 3 minutes between manual sync attempts
- Prevents sync spam

### Inventory Sync Lock

- 30-minute lease on Supabase
- Heartbeat every 5 minutes refreshes lease
- Other machines see lock and skip sync

## Potential Issues

### Issue 1: BASE Running Background Jobs

**Symptom**: Inventory sync timer running on BASE tier.

**Check**: 
- Is `_tierPolicy.EnableBackgroundAutoSync` true?
- Is `_autoSyncTimer.Enabled` true?

**Fix**: Verify `TierRuntimePolicy.Resolve()` returns correct flags.

### Issue 2: FullStackOperation Created for BASE

**Symptom**: ULTRA-only form accessible on BASE.

**Check**:
- `PreCreateFullStackForm` guard at line 883
- `ShowFullStackForm` guard at line 904

**Fix**: Never remove these guards.

### Issue 3: Manual Operations Blocked

**Symptom**: User cannot search/tracking on ULTRA.

**Check**:
- Are manual operation handlers working?
- Are background jobs interfering?

**Fix**: Background jobs should not block manual operations.
