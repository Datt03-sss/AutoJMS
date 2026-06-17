# FullStackOperation Architecture

## Overview

FullStackOperation is an **ULTRA-only independent form** (not a tab) that provides advanced operations:

- **Dashboard**: Realtime waybill list from Supabase
- **Thời Hiệu**: SLA monitoring
- **Chat**: Zalo integration

## Form Lifecycle

```
Main.OnShown
    │
    └─► PreCreateFullStackForm() [ULTRA only]
            │
            ├─► if (!_tierPolicy.EnableFullStackOperation) return;
            │
            └─► _fullStackForm = new FullStackOperation()
                    │
                    └─► Constructor runs
                            │
                            └─► Subscribes to AuthStateService.TokenAcquired
                                    │
                                    └─► StartRealtimeRuntimeAsync() on token


User types "DASH"
    │
    └─► ShowFullStackForm()
            │
            ├─► if (!_tierPolicy.EnableFullStackOperation) return;
            │
            └─► _fullStackForm.Show()
                    │
                    └─► Form_Load
                            │
                            ├─► SetupGrids()
                            ├─► InitializeEnhancedUI()
                            │
                            └─► if (AuthStateService.Instance.IsAuthenticated)
                                    StartRealtimeRuntimeAsync()


User closes form
    │
    └─► FullStackOperation_FormClosing
            │
            ├─► _isClosing = true
            ├─► Stop timers
            ├─► Cancel tasks
            ├─► Stop ZaloService
            │
            └─► Form disposed, _fullStackForm = null
```

## State Machine

```
┌─────────┐    Token arrives    ┌─────────┐
│  IDLE   │───────────────────▶│ ACTIVE  │
└─────────┘                    └────┬────┘
      │                            │
      │ FormClosing               │ Timer tick
      │ _isClosing = true        │ API call
      ▼                            ▼
┌─────────┐                   ┌─────────┐
│CLOSING │◀──────────────────│ ACTIVE  │
└─────────┘    Timer stopped   └─────────┘
```

## Components

### Tabs

| Tab | Purpose | Data Source |
|-----|---------|-------------|
| tabDash | Dashboard | Supabase waybills |
| tabChat | Zalo chat | ZaloChatService |
| tabThoiHieu | SLA monitoring | Supabase waybills |

### Services

| Service | Purpose |
|---------|---------|
| ZaloChatService | Zalo message integration |
| SupabaseDbService | Waybill data |
| InventorySyncService | Inventory fetch |
| AuthStateService | Token state |

### Timers

| Timer | Interval | Purpose |
|-------|----------|---------|
| _autoRefreshTimer | 2 min | Refresh dashboard/chat |
| _thoiHieuTimer | 1 min | SLA monitoring |
| _alertCheckTimer | 30 sec | Alert notifications |

## Data Flow

```
AutoRefreshTimer tick
    │
    └─► LoadDataAndRefreshViewsAsync()
            │
            ├─► Check AuthStateService.IsAuthenticated
            │
            ├─► SupabaseDbService.GetActiveWaybillsAsync()
            │       │
            │       └─► Paginated query (1000 rows/page)
            │
            ├─► DatabaseTracking (ULTRA only)
            │       │
            │       └─► RunBackgroundTrackingAsync()
            │               │
            │               └─► Track new waybills
            │
            └─► Update UI
                    │
                    ├─► tabDash_dataGridView
                    ├─► tabChat_dataGrid
                    └─► uiDataGridView2 (Thời Hiệu)
```

## UI Thread Safety

### Guard Pattern

```csharp
private volatile bool _uiReady;
private volatile bool _isClosing;

// Timer callback
if (_isClosing) return;      // Stop if closing
if (!_uiReady) return;       // Wait if not ready

// Set data
if (_isClosing) return;
if (!_uiReady)
{
    _pendingData = data;      // Cache until ready
    return;
}

// Safe to update UI
grid.DataSource = data;
```

### Lifecycle Guards

```csharp
// FullStackOperation_Load
_uiReady = true;  // Set after UI fully initialized

// FullStackOperation_FormClosing
_isClosing = true;  // Set first, stops all UI updates
```

## Auth Integration

### Token Subscription

```csharp
public FullStackOperation()
{
    AuthStateService.Instance.TokenAcquired += async token =>
        await StartRealtimeRuntimeAsync();
}
```

### Activation Check

```csharp
private async Task StartRealtimeRuntimeAsync()
{
    if (_isRealtimeStarted) return;
    _isRealtimeStarted = true;
    
    // Start timers
    _autoRefreshTimer.Start();
    
    // Initial load
    await LoadDataAndRefreshViewsAsync();
}
```

## Grid Configuration

### tabDash_dataGridView

```csharp
Columns:
- STT (45px, frozen)
- Mã vận đơn (140px, frozen)
- Nhân viên xử lý cuối (130px)
- Trạng thái hiện tại (140px)
- Thao tác cuối (160px)
- Thời gian thao tác (150px)
- NV kiện vấn đề (120px)
- Nguyên nhân kiện vấn đề (140px)
- Số lần nhắc (60px)
- Cập nhật lúc (140px)
```

### Cell Formatting

Status-based coloring for visual alerts.

## Cleanup

```csharp
private void FullStackOperation_FormClosing(...)
{
    // 1. Signal closing
    _isClosing = true;
    
    // 2. Stop timers
    _autoRefreshTimer?.Stop();
    _autoRefreshTimer?.Dispose();
    _thoiHieuTimer?.Stop();
    _thoiHieuTimer?.Dispose();
    
    // 3. Cancel tasks
    _cts.Cancel();
    
    // 4. Stop services
    _zaloChatService?.StopAutoReminder();
}
```
