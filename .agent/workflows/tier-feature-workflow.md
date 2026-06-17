# Tier Feature Workflow

Use this workflow when adding/removing features to tiers.

## Tier Policy Overview

| Feature | BASE | ULTRA |
|---------|------|-------|
| Core tabs | ✓ | ✓ |
| Manual operations | ✓ | ✓ |
| Background sync | ✗ | ✓ |
| FullStackOperation | ✗ | ✓ |

## Adding Feature to BASE

### Steps

1. **Understand feature**
   - What does it do?
   - Any ULTRA dependencies?

2. **Check dependencies**
   - Does it use Supabase?
   - Does it use authToken?
   - Any background processing?

3. **Update tier-definitions.json**

```json
{
  "tiers": {
    "BASE": {
      "tabs": [...],
      "forms": [...],
      "backgroundJobs": {
        "inventorySync": false,
        "databaseTracking": false,
        "autoSyncTimer": false,
        "fullStackRealtime": false
      }
    }
  }
}
```

4. **Implement with tier checks**

```csharp
// In Main.cs
if (_tierPolicy.AllowFeatureX)
{
    // Enable feature
}
```

5. **Test both tiers**

## Moving Feature to ULTRA

### Steps

1. **Remove from BASE definition**
2. **Add to ULTRA definition**
3. **Add tier guard in code**

```csharp
if (!_tierPolicy.EnableFeatureX)
{
    AppLogger.Info("Feature disabled for this tier");
    return;
}
```

4. **Test ULTRA only**

## Adding Background Job to ULTRA

### Steps

1. **Define in tier-definitions.json**

```json
{
  "tiers": {
    "ULTRA": {
      "backgroundJobs": {
        "myBackgroundJob": true
      }
    }
  }
}
```

2. **Check tier policy**

```csharp
if (_tierPolicy.EnableMyBackgroundJob)
{
    _backgroundJobTimer.Start();
}
```

3. **Ensure BASE never runs**

```csharp
// Double-check guard
if (_tierPolicy == null || !_tierPolicy.EnableMyBackgroundJob)
    return;
```

4. **Test BASE has no background activity**

## Testing Checklist

### BASE Tier Testing

- [ ] Core tabs work
- [ ] Manual operations work
- [ ] No background jobs running
- [ ] No FullStackOperation
- [ ] Logs show "disabled for BASE"

### ULTRA Tier Testing

- [ ] All BASE features work
- [ ] Background jobs run
- [ ] FullStackOperation works
- [ ] Logs show correct flags

## Common Mistakes

### Mistake 1: Hardcoded Tier Check

```csharp
// WRONG
if (CurrentTier == "ULTRA")

// CORRECT
if (_tierPolicy.EnableBackgroundSync)
```

### Mistake 2: Missing Guard

```csharp
// WRONG
_autoSyncTimer.Start();  // Always starts

// CORRECT
if (_tierPolicy.EnableBackgroundAutoSync)
    _autoSyncTimer.Start();
```

### Mistake 3: Not Testing Both Tiers

Always test with both BASE and ULTRA licenses.
