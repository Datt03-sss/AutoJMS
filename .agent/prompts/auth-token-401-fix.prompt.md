# Auth Token 401 Fix Prompt

Use this prompt when fixing JMS authToken 401 errors.

## CRITICAL: Two Token Types

| Token | Source | Purpose | Format |
|-------|--------|---------|---------|
| License JWT | Render server | License activation | JWT |
| JMS AuthToken | JMS WebView2 | JMS API calls | 32-hex |

**NEVER confuse these.** License JWT is for the license server. JMS AuthToken is for JMS API.

## Auth Flow

```
User logs into JMS in WebView2
    ↓
JMS sets authToken in localStorage
    ↓
Our app captures token via:
  - CoreWebView2_WebResourceRequested (header)
  - RefreshAuthTokenAsync (JS injection)
    ↓
Token stored in:
  - JmsAuthStateService (memory)
  - AutoJMS.json (LastAuthToken)
    ↓
API calls use token via JmsApiClient
    ↓
On 401: Force refresh → retry once
    ↓
If still 401: NotifyReallyExpired → clear token
```

## Common 401 Issues

### Issue 1: Token Not Captured

**Symptom**: Token always empty, 401 on all API calls

**Check**:
```csharp
// Is CoreWebView2_WebResourceRequested firing?
// Check logs for "Auth token captured from"
```

**Fix**: Verify WebView2 navigation to JMS origin

### Issue 2: Token Expired Mid-Session

**Symptom**: Working initially, then 401s after some time

**Fix**: Check token refresh logic in JmsApiClient

### Issue 3: Wrong Token Type Used

**Symptom**: "JWT-looking token rejected" warnings

**Fix**: Never pass license JWT to JmsApiClient

### Issue 4: Token Not Refreshed After Expired

**Symptom**: User logged in to JMS, but 401s

**Fix**: Check RefreshAuthTokenAsync and token validity

## Fix Workflow

### Step 1: Understand the Flow

Read these files:
1. `Main.cs` - Token capture and storage
2. `JmsAuthTokenService.cs` - Token resolution
3. `JmsApiClient.cs` - 401 handling
4. `JmsAuthStateService.cs` - Token state

### Step 2: Identify Where 401 Happens

```csharp
// In JmsApiClient.PostJsonAsync
var response = await httpClient.SendAsync(request);

if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    // This is where 401 handling happens
    await JmsAuthTokenService.ForceRefreshFromWebViewAsync();
    // ... retry logic
}
```

### Step 3: Check Token Validation

The token MUST be 32 hex characters:

```csharp
// In JmsAuthTokenService
private static readonly Regex HexToken32 = 
    new("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);

public static bool IsValidJmsToken(string t)
    => !string.IsNullOrEmpty(t) && HexToken32.IsMatch(t);
```

### Step 4: Check Refresh Logic

```csharp
public static async Task<string> ForceRefreshFromWebViewAsync()
{
    // Uses WebViewTokenReader callback
    // Reads from WebView2 localStorage
    // Validates token shape
}
```

### Step 5: Verify 401 Retry

```csharp
// In JmsApiClient
// First attempt
if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    // Force refresh
    await JmsAuthTokenService.ForceRefreshFromWebViewAsync();
    
    // Retry once
    var retryResponse = await SendWithTokenAsync(newToken);
    
    // If still 401, truly expired
    if (retryResponse.StatusCode == HttpStatusCode.Unauthorized)
    {
        JmsAuthTokenService.NotifyReallyExpired();
    }
}
```

## DO NOT DO

1. **DO NOT clear token immediately on 401**
   - Give chance to refresh first
   - WebView session may still be valid

2. **DO NOT use license JWT for JMS API**
   - License JWT is for license server only
   - JMS AuthToken is 32 hex

3. **DO NOT log full tokens in production**
   - Mask to first6...last4

4. **DO NOT block while waiting for token**
   - Use async/await
   - CancellationToken for timeout

## Token Validation Rules

### Valid JMS AuthToken

```csharp
// 32 hexadecimal characters
if (token.Length == 32 && token.All(c => char.IsAsciiHexDigit(c)))
{
    // Valid
}
```

### Invalid Patterns

| Pattern | Why Invalid |
|---------|-------------|
| JWT with dots | License JWT |
| GUID with dashes | Not a token |
| Shorter than 32 | Too short |
| Contains non-hex | Invalid format |

## JMS Response Codes

| Code | Meaning | Action |
|------|---------|--------|
| code=200 | Success | Continue |
| code=0 | Success | Continue |
| code=1 | Success | Continue |
| code!=200,0,1 | Failure | Show error |
| HTTP 401 | Token expired | Refresh token |

## Debug Logging

Add temporary logging:

```csharp
AppLogger.Info($"[AUTH] API call: {(int)response.StatusCode}");
if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    AppLogger.Info($"[AUTH] Got 401, current token: {MaskToken(currentToken)}");
    AppLogger.Info($"[AUTH] Attempting refresh...");
}
```

## Testing

### Manual Test Steps

1. Login to JMS in WebView2
2. Copy token from localStorage
3. Make API call
4. Wait for token expiry (or logout/login)
5. Verify app handles 401 correctly

### Token Masking Test

```csharp
public static string MaskToken(string t)
{
    if (string.IsNullOrEmpty(t) || t.Length < 10)
        return "***";
    return $"{t.Substring(0, 4)}...{t.Substring(t.Length - 4)}";
}
```

## After Fix

1. Verify 401 retry works
2. Verify token refresh works
3. Verify "notify user" works (but not spam)
4. Check both BASE and ULTRA
5. Test with actual JMS session expiry
