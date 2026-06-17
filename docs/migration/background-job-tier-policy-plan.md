# Background Job Tier Policy Plan

## Overview

This document describes the background job execution policy by tier and migration considerations.

## Current Policy

### TierRuntimePolicy Flags

| Flag | BASE | ULTRA | Purpose |
|------|:----:|:-----:|---------|
| EnableStartupInventorySync | ✗ | ✓ | Fetch inventory on startup |
| EnableBackgroundAutoSync | ✗ | ✓ | Run auto-sync timer |
| EnableStartupDatabaseTracking | ✗ | ✓ | Start DB tracking on startup |
| EnableFullStackOperation | ✗ | ✓ | Allow FullStackOperation form |

### Background Jobs by Tier

#### BASE

| Job | Trigger | Location |
|-----|---------|----------|
| None | - | - |

BASE has **NO automatic background jobs**.

#### ULTRA

| Job | Trigger | Interval |
|-----|---------|----------|
| Inventory sync | Timer | 1 second |
| Database tracking | New waybill | Event |
| FullStackOperation refresh | Timer | Configurable |

### Code Enforcement

```csharp
// Main.cs - Startup sync
if (_tierPolicy.EnableStartupInventorySync || 
    _tierPolicy.EnableStartupDatabaseTracking)
{
    _ = RunStartupSyncAsync(_appCts.Token);
}

// Main.cs - Auto sync timer
if (_tierPolicy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}
```

## Known Risks

### Risk 1: Timer Started on BASE

**Scenario**: TierRuntimePolicy returns wrong flags

**Impact**: BASE users run background jobs they didn't pay for

**Mitigation**: Verify flags are correctly returned

### Risk 2: ULTRA Jobs Not Starting

**Scenario**: TierRuntimePolicy returns no flags for ULTRA

**Impact**: ULTRA users miss background sync

**Mitigation**: Test ULTRA tier startup

## Testing Checklist

- [ ] Verify BASE has no background timers running
- [ ] Verify ULTRA has _autoSyncTimer started
- [ ] Verify ULTRA starts database tracking
- [ ] Check logs for tier policy resolution

## Migration Considerations

Current implementation is correct. No migration needed unless:
1. New tier added
2. Job requirements change significantly
