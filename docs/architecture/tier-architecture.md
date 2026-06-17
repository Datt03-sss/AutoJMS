# Tier Architecture

## Tier Model

AutoJMS uses a two-tier model to provide different feature sets:

| Feature | BASE | ULTRA |
|---------|:----:|:-----:|
| HOME tab | ✓ | ✓ |
| DKCH tab | ✓ | ✓ |
| TRACKING tab | ✓ | ✓ |
| PRINT tab | ✓ | ✓ |
| ABOUT tab | ✓ | ✓ |
| Manual operations | ✓ | ✓ |
| Inventory sync (auto) | ✗ | ✓ |
| Database tracking (auto) | ✗ | ✓ |
| Background sync timer | ✗ | ✓ |
| FullStackOperation form | ✗ | ✓ |

## Tier Definition Source

`tier-definitions.json` (shipped with app):

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
      "forms": [
        {
          "name": "FULLSTACK_OPERATION",
          "type": "VISIBLE_FORM",
          "launch": "AFTER_MAINFORM_SHOWN"
        }
      ],
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

## Tier Resolution

### Flow

```
Program.Main()
    │
    ├─► LicenseApiService.VerifyLicenseSecureAsync()
    │       │
    │       └─► Parse "tier" from server response
    │
    └─► new Main(sessionTier)  ← tier string passed
            │
            └─► TierRuntimePolicy.Resolve(tier)
                    │
                    ├─► Load tier-definitions.json
                    │
                    ├─► Check if FULLSTACK_OPERATION form exists
                    │
                    ├─► isUltra = hasFullStack || tier == "ULTRA"
                    │
                    └─► Return TierRuntimePolicy with flags
```

## TierRuntimePolicy Flags

| Flag | BASE | ULTRA | Description |
|------|:----:|:-----:|-------------|
| EnableStartupInventorySync | false | true | Run sync on startup |
| EnableStartupDatabaseTracking | false | true | Run tracking on startup |
| EnableBackgroundAutoSync | false | true | Start auto-sync timer |
| EnableFullStackOperation | false | true | Allow FullStack form |
| AllowManualTracking | true | true | Manual tracking always allowed |
| AllowManualPrint | true | true | Manual print always allowed |

## Tier Enforcement

### Enforcement Points in Main.cs

| Point | Check | BASE | ULTRA |
|-------|-------|:----:|:-----:|
| AutoSyncTimer start | `EnableBackgroundAutoSync` | ✗ | ✓ |
| StartupSyncAsync | `EnableStartupInventorySync` | ✗ | ✓ |
| PreCreateFullStackForm | `EnableFullStackOperation` | ✗ | ✓ |
| ShowFullStackForm | `EnableFullStackOperation` | ✗ | ✓ |

### Code Pattern

```csharp
// WRONG - hardcoded check
if (CurrentTier == "ULTRA")
{
    StartBackgroundJob();
}

// CORRECT - policy check
if (_tierPolicy.EnableStartupInventorySync)
{
    _ = RunStartupSyncAsync(_appCts.Token);
}
```

## Manual vs Automatic Operations

### Manual (Always Allowed)

Both tiers support manual operations initiated by user:

| Operation | Trigger | Service |
|-----------|---------|---------|
| Waybill search | btnSearch_Click | WaybillTrackingService |
| Print label | tabPrint_btnPrint | PrintService |
| DKCH1/DKCH2 | tabDKCH_btnDKCH1/2 | DkchManager |
| Export Excel | btn_Export_Click | WaybillTrackingService |

### Automatic (Tier-Restricted)

| Operation | BASE | ULTRA | Service |
|-----------|:----:|:-----:|---------|
| Inventory sync | ✗ | ✓ | InventorySyncService |
| Database tracking | ✗ | ✓ | DatabaseTracking |
| Auto refresh | ✗ | ✓ | FullStackOperation |

## Background Jobs (ULTRA Only)

### AutoSyncTimer

```csharp
_autoSyncTimer.Interval = 1000;  // 1 second

_autoSyncTimer.Tick += async (s, e) =>
{
    if (!_tierPolicy.EnableBackgroundAutoSync) return;
    if (!ShouldRunAutoSyncNow()) return;
    
    await ExecuteSyncWorkflowAsync(ct, forceRefreshLease: false, sourceHint: "CLOUD");
};
```

### Inventory Sync Lock

```csharp
// TryAcquireInventoryLease (30-minute lock)
// Other machines see lock and skip sync
bool acquired = await SupabaseDbService.TryAcquireInventoryLeaseAsync(LeaseSeconds);
if (!acquired)
{
    AppLogger.Info("[Sync] Máy khác đang giữ quyền sync.");
    return;
}
```

## FullStackOperation (ULTRA Only)

### Launch Flow

```
Main.OnShown
    │
    └─► PreCreateFullStackForm()
            │
            └─► if (!_tierPolicy.EnableFullStackOperation)
                    return;  // BASE: do nothing
                    
            _fullStackForm = new FullStackOperation()
```

### Show Flow

```
User types "DASH" in HOME URL bar
    │
    └─► ShowFullStackForm()
            │
            └─► if (!_tierPolicy.EnableFullStackOperation)
                    return;  // BASE: ignore command
                    
            _fullStackForm.Show()
```

## Testing Verification

### BASE Logs

```
[INFO] AutoSync timer not started for BASE
[INFO] Background inventory sync disabled for BASE
[INFO] FullStackOperation disabled for BASE — not pre-created
```

### ULTRA Logs

```
[INFO] Tier policy resolved: ULTRA (inventorySync=true, ...)
[INFO] AutoSync timer started
[INFO] FullStackOperation pre-created in background
```
