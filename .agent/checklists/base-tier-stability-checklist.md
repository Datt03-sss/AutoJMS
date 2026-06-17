# BASE Tier Stability Checklist

Use this checklist to verify BASE tier stability.

## Required Quick Checklist

- [ ] BASE không auto inventory sync
- [ ] BASE không auto database tracking
- [ ] BASE không auto FullStackOperation
- [ ] BASE manual TRACKING vẫn hoạt động
- [ ] BASE PRINT vẫn hoạt động
- [ ] ABOUT vẫn là tab cuối
- [ ] Không có background timer thừa
- [ ] Không gọi JMS API khi chưa có authToken

## Core Features

### HOME Tab

- [ ] JMS website loads
- [ ] Navigation works
- [ ] Token capture works
- [ ] DASH command NOT accessible

### DKCH Tab

- [ ] DKCH1 works
- [ ] DKCH2 works
- [ ] Stop button works
- [ ] Manual input works

### TRACKING Tab

- [ ] Search works
- [ ] Results display
- [ ] Export works
- [ ] Upload to BUMP works

### PRINT Tab

- [ ] Search works
- [ ] All print modes work
- [ ] Print count enforced
- [ ] PDF downloads

### ABOUT Tab

- [ ] Version shown
- [ ] Update check works
- [ ] No features shown for ULTRA

## Background Jobs

### Verify No Background Jobs

- [ ] No _autoSyncTimer running
- [ ] No inventory sync
- [ ] No database tracking
- [ ] No realtime updates

### Check Logs

```
[INFO] AutoSync timer not started for BASE
[INFO] Background inventory sync disabled for BASE
[INFO] Background database tracking disabled for BASE
```

## FullStackOperation

### Verify Not Accessible

- [ ] Form not created
- [ ] "DASH" command doesn't show form
- [ ] No ULTRA features in UI

### Check Logs

```
[INFO] FullStackOperation disabled for BASE — not pre-created
```

## Tier Policy

### Verify Policy

```csharp
_tierPolicy.EnableBackgroundAutoSync      // false
_tierPolicy.EnableStartupInventorySync    // false
_tierPolicy.EnableFullStackOperation       // false
_tierPolicy.AllowManualTracking           // true
_tierPolicy.AllowManualPrint              // true
```

## Performance

- [ ] No unexpected network activity
- [ ] No Supabase connections (on idle)
- [ ] No Firebase connections (on idle)
- [ ] Memory usage stable

## Auth Flow

- [ ] Login works
- [ ] Token captured
- [ ] Token persisted
- [ ] Offline works with cached key
