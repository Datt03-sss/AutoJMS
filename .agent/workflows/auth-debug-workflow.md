# Auth Debug Workflow

Use this workflow when debugging auth/token issues.

## Symptoms

- [ ] 401 errors on JMS API calls
- [ ] Token not captured
- [ ] Token not persisting
- [ ] Auth flow broken

## Debug Steps

### Step 1: Check Token Capture

Check logs for "Auth token captured":

```
[INFO] Auth token captured from WebView2 request header (len=32), authToken=1234...
```

If not found:
- Check WebView2 navigation to JMS
- Check CoreWebView2_WebResourceRequested firing
- Check header names (authToken, YL_TOKEN, etc.)

### Step 2: Check Token Validation

Check for "not 32-hex" or "Rejected JWT" warnings:

```
[WARNING] Ignored JWT-looking value from localStorage — not a JMS authToken
[WARNING] Ignored non-token value (len=25) — not 32-hex
```

If found:
- Token is wrong format
- Check what value is being captured
- Verify JMS is using correct token key

### Step 3: Check Token Storage

Check logs for token being set:

```
[INFO] Auth token captured from WebView2 storage 'YL_TOKEN' (len=32), authToken=1234...
```

If not found:
- Check WebView2 has navigated to JMS
- Check RefreshAuthTokenAsync is called
- Check token key names

### Step 4: Check API Calls

Check logs for API calls:

```
[INFO] API call: 200
```

vs

```
[WARN] API call: 401
```

If 401:
- Check token is valid
- Check token not expired
- Check RefreshAuthTokenAsync being called

### Step 5: Check Token Refresh

Check for "ForceRefresh" logs:

```
[INFO] ForceRefresh: reusing recent refresh result.
```

vs

```
[WARN] ForceRefresh: WebView returned no usable token.
```

## Log Points

### Token Capture

```csharp
// Main.cs - CoreWebView2_WebResourceRequested
AppLogger.Info($"[AUTH] Captured token from header: {MaskToken(token)}");
```

### Token Validation

```csharp
// JmsAuthTokenService
if (!IsValidJmsToken(token))
    AppLogger.Warning($"[AUTH] Invalid token shape: len={token?.Length}");
```

### Token Refresh

```csharp
// JmsApiClient
AppLogger.Info($"[AUTH] 401, refreshing token...");
```

### Token Expired

```csharp
// JmsAuthTokenService
AppLogger.Warning("[AUTH] Token expired after refresh");
```

## Common Causes

### Cause 1: WebView2 Not Navigated

**Symptom**: No token ever captured

**Fix**: Check Main.OnLoad WebView navigation

### Cause 2: Wrong Storage Key

**Symptom**: Token captured but wrong

**Fix**: Check JMS localStorage key names

### Cause 3: Token Expired

**Symptom**: Works initially, then 401s

**Fix**: Check JmsApiClient retry logic

### Cause 4: Multiple WebViews

**Symptom**: Token in one WebView but not another

**Fix**: Check RefreshAuthTokenAsync checks all WebViews

## Masking

For production, mask tokens:

```csharp
public static string MaskToken(string t)
{
    if (string.IsNullOrEmpty(t) || t.Length < 10)
        return "***";
    return $"{t.Substring(0, 4)}...{t.Substring(t.Length - 4)}";
}
```
