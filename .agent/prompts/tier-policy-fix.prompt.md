# Tier Policy Fix Prompt

Use this prompt when fixing BASE/ULTRA tier issues.

## Tier Policy Overview

| Feature | BASE | ULTRA |
|---------|------|-------|
| HOME, DKCH, TRACKING, PRINT, ABOUT | ✓ | ✓ |
| Manual tracking | ✓ | ✓ |
| Manual print | ✓ | ✓ |
| Auto inventory sync | ✗ | ✓ |
| Auto database tracking | ✗ | ✓ |
| Background sync timer | ✗ | ✓ |
| FullStackOperation | ✗ | ✓ |

## Tier Enforcement Pattern

### ALWAYS Use TierRuntimePolicy

```csharp
// CORRECT
var policy = TierRuntimePolicy.Resolve(CurrentTier);
if (policy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}

// WRONG - hardcoded check
if (CurrentTier == "ULTRA")
{
    _autoSyncTimer.Start();
}
```

### Guard Every Tier-Specific Feature

```csharp
private void PreCreateFullStackForm()
{
    // Guard
    if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
    {
        AppLogger.Info($"FullStackOperation disabled for {_tierPolicy?.Tier ?? "BASE"}");
        return;
    }

    // Create form
    _fullStackForm = new FullStackOperation();
}
```

## Common Tier Issues

### Issue 1: Background Jobs Running on BASE

**Symptom**: BASE users experiencing network activity, Supabase writes

**Check**:
```csharp
// In Main constructor
if (_tierPolicy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}
```

**Fix**: Verify `_tierPolicy` is correct

### Issue 2: Tier Policy Returns Wrong Flags

**Symptom**: ULTRA users missing features, or BASE users getting them

**Check**:
```csharp
// In TierRuntimePolicy.Resolve
bool isUltra = hasFullStack || normalized == "ULTRA";

// Check tier-definitions.json
var definitions = TierDefinitions.LoadFromFile();
bool hasFullStack = definitions.HasForm(normalized, "FULLSTACK_OPERATION");
```

**Fix**: Verify tier-definitions.json is correct

### Issue 3: FullStackOperation Accessible on BASE

**Symptom**: BASE users can open FullStackOperation

**Fix**: Ensure guard is in place and correct

```csharp
// In ShowFullStackForm
if (_tierPolicy == null || !_tierPolicy.EnableFullStackOperation)
{
    AppLogger.Info($"ShowFullStackForm ignored for BASE");
    return;
}
```

## Fix Workflow

### Step 1: Check Tier Source

Where does the tier come from?

```csharp
// Program.cs - From license response
var activateResult = LicenseApiService.VerifyLicenseSecureAsync(key, hwid);
string tier = activateResult.Tier;  // "BASE" or "ULTRA"
```

### Step 2: Check Tier Definition Source

```csharp
// TierRuntimePolicy.Resolve
definitions ??= TierDefinitions.LoadFromFile();
bool hasFullStack = definitions.HasForm(normalized, "FULLSTACK_OPERATION");
bool isUltra = hasFullStack || normalized == "ULTRA";
```

### Step 3: Check tier-definitions.json

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
          "type": "VISIBLE_FORM"
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

### Step 4: Add Debug Logging

```csharp
AppLogger.Info($"[TIER] Resolving tier: {tier}");
AppLogger.Info($"[TIER] Has FullStack: {hasFullStack}");
AppLogger.Info($"[TIER] Is Ultra: {isUltra}");
AppLogger.Info($"[TIER] EnableBackgroundAutoSync: {policy.EnableBackgroundAutoSync}");
```

## Manual Testing

### Test BASE Tier

1. Login with BASE license
2. Check logs: should show "Background inventory sync disabled for BASE"
3. Type "DASH" in HOME URL bar
4. FullStackOperation should NOT appear
5. Verify tabs work (HOME, DKCH, TRACKING, PRINT, ABOUT)

### Test ULTRA Tier

1. Login with ULTRA license
2. Check logs: should show "AutoSync timer started"
3. Type "DASH" in HOME URL bar
4. FullStackOperation should appear
5. Verify all tabs work

## DO NOT

1. **DO NOT remove tier guards** - They exist for a reason
2. **DO NOT hardcode tier checks** - Use TierRuntimePolicy
3. **DO NOT add features to BASE without approval** - BASE is core stable
4. **DO NOT move features from ULTRA to BASE** - Without migration plan

## TierMigrationPath

If moving feature from ULTRA to BASE:

1. Remove from ULTRA-only definition
2. Add to BASE definition in tier-definitions.json
3. Verify no ULTRA-specific dependencies
4. Test both tiers
5. Document breaking change

## After Fix

1. Test with BASE license
2. Test with ULTRA license
3. Verify correct features enabled/disabled
4. Check logs for tier policy resolution
5. Verify no background jobs on BASE
