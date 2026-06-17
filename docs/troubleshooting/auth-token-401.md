# Auth Token 401 Troubleshooting

## Symptoms

- 401 errors on JMS API calls
- Token not captured
- Auth flow broken

## Debug Steps

### 1. Check Token Capture

Look for logs:
```
[INFO] Auth token captured from WebView2 request header (len=32)
```

If missing:
- Check WebView2 navigated to JMS
- Check CoreWebView2_WebResourceRequested firing
- Check header names

### 2. Check Token Validation

Look for warnings:
```
[WARNING] Ignored JWT-looking value
[WARNING] Ignored non-token value (len=25)
```

If found:
- Token is wrong format
- Check what value is being captured

### 3. Check API Calls

```
[INFO] API call: 200  ← Success
[WARN] API call: 401  ← Token expired
```

## Common Causes

### Cause 1: WebView2 Not Navigated

**Check**: Is JMS URL loaded?
**Fix**: Check Main.OnLoad navigation

### Cause 2: Wrong Storage Key

**Check**: Is correct localStorage key used?
**Fix**: Check RefreshAuthTokenAsync keys

### Cause 3: Token Expired

**Check**: Is JMS session still valid?
**Fix**: User re-logs to JMS

## Log Points

Add temporary logging:
```csharp
AppLogger.Info($"[AUTH] Token: {MaskToken(token)}");
```
