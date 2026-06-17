# License, Tier & Security Rules

## CRITICAL: Two Token Types

### 1. License JWT (from Render Server)

- **Purpose**: License activation, heartbeat, server commands
- **Format**: JWT (RS256 signed)
- **Storage**: Memory only (not persisted)
- **Lifetime**: 60 minutes (server-defined)
- **Never use for**: JMS API calls

### 2. JMS AuthToken (from JMS WebView2)

- **Purpose**: JMS API calls (tracking, print, DKCH)
- **Format**: 32-character hex string
- **Storage**: Memory + AutoJMS.json (LastAuthToken)
- **Lifetime**: Until JMS session expires
- **Never confuse with**: License JWT

## Token Separation Rules

### MUST DO

1. **Never use license JWT as JMS token**
   ```csharp
   // WRONG
   JmsApiClient.PostJsonAsync(url, json, authToken: licenseJwt);
   
   // CORRECT
   JmsApiClient.PostJsonAsync(url, json, authToken: jmsAuthToken);
   ```

2. **Always validate token shape before use**
   ```csharp
   if (!JmsAuthTokenService.IsValidJmsToken(token))
   {
       AppLogger.Warning("Invalid token shape, rejecting");
       return;
   }
   ```

3. **Check for JWT-like patterns**
   ```csharp
   if (JmsAuthTokenService.LooksLikeJwt(token))
   {
       AppLogger.Warning("Rejected JWT-looking value as JMS token");
       return false;
   }
   ```

### MUST NOT DO

1. **Never log full tokens in production**
   ```csharp
   // WRONG
   AppLogger.Info($"Token: {token}");
   
   // CORRECT
   AppLogger.Info($"Token: {MaskToken(token)}");
   ```

2. **Never store tokens in plain text files** (except AutoJMS.json with user's encrypted config)

3. **Never send license JWT to JMS API**

## Tier Enforcement

### Tier Definition Source

`tier-definitions.json` is the single source of truth for:
- Which tabs are available
- Which forms are available
- Which background jobs can run

### Tier Resolution

Always use `TierRuntimePolicy.Resolve(tier)`:

```csharp
var policy = TierRuntimePolicy.Resolve(CurrentTier);

// Check before starting background jobs
if (policy.EnableBackgroundAutoSync)
{
    _autoSyncTimer.Start();
}

// Check before creating forms
if (policy.EnableFullStackOperation)
{
    PreCreateFullStackForm();
}
```

### NEVER Hardcode Tier Checks

```csharp
// WRONG
if (CurrentTier == "ULTRA")
{
    StartBackgroundJob();
}

// CORRECT
if (_tierPolicy.EnableStartupInventorySync)
{
    _ = RunStartupSyncAsync(_appCts.Token);
}
```

## AuthToken Handling

### Token Priority

1. In-memory (JmsAuthStateService.CurrentToken)
2. WebView2 localStorage (fresh from JMS page)
3. AutoJMS.json (LastAuthToken - cached)

### Token Refresh Flow

```
API call with current token
    ↓ (401 response)
ForceRefreshFromWebViewAsync() → Read from WebView2
    ↓ (still 401)
NotifyReallyExpired() → Clear local state, notify user
    ↓ (user re-logs in JMS)
WebResourceRequested / RefreshAuthTokenAsync → Capture new token
```

### Token Capture Points

1. **CoreWebView2_WebResourceRequested**: Capture from request headers
2. **RefreshAuthTokenAsync**: Read from localStorage/sessionStorage
3. **GetTokenFromJmsWebViewAsync**: JmsAuthTokenService callback

## 401 Handling

### JmsApiClient.PostJsonAsync

```csharp
var response = await httpClient.SendAsync(request);

// First attempt
if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    // Force refresh token
    await JmsAuthTokenService.ForceRefreshFromWebViewAsync();
    
    // Retry once
    response = await RetryWithNewToken();
    
    // If still 401, truly expired
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
        JmsAuthTokenService.NotifyReallyExpired();
    }
}
```

### DO NOT Clear Token Immediately

```csharp
// WRONG
if (response.StatusCode == 401)
{
    ClearToken();  // DON'T do this!
    return;
}

// CORRECT
if (response.StatusCode == 401)
{
    await JmsAuthTokenService.ForceRefreshFromWebViewAsync();
    // Retry, then notify expired if still failing
}
```

## Token Validation

### JMS AuthToken Validation

Valid JMS authToken is **exactly 32 hexadecimal characters**:

```csharp
private static readonly Regex HexToken32 = 
    new("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);

public static bool IsValidJmsToken(string t)
    => !string.IsNullOrEmpty(t) && HexToken32.IsMatch(t);
```

### Reject These Token Patterns

| Pattern | Reason |
|---------|--------|
| JWT (has dots) | License token, not JMS token |
| GUID (has dashes) | Not a token |
| Short strings | Not 32 chars |
| Contains letters beyond hex | Invalid format |

## Sensitive Data Handling

### Data That Must Be Encrypted

| Data | Location | Method |
|------|----------|--------|
| License Key | license.dat | DPAPI (SecureConfigCrypto) |
| Secure Config | AutoJMS.secure | AES-CBC-HMACSHA256 |
| API Keys | Environment | Don't store in code |

### Data That Can Be Plain Text

| Data | Location | Reason |
|------|----------|--------|
| User Settings | AutoJMS.json | User-specific, not sensitive |
| Browser Data | BrowserData\ | WebView2 managed |
| Logs | logs\debug.log | Timestamped, rotating |

### Data That Must Never Be Committed

```
.env
service_account.json
*.pfx
*.key
AutoJMS.secure
license.dat
```

## Security Checklist

### Before Release

1. [ ] Token masking implemented (TODO in code)
2. [ ] No secrets in source code
3. [ ] .gitignore excludes sensitive files
4. [ ] Supabase anon key is truly public-only
5. [ ] Firebase rules allow read-only for anon

### Before Any Code Change

1. [ ] Does this change touch token handling?
2. [ ] Does this change touch tier enforcement?
3. [ ] Does this change add new storage?
4. [ ] Does this change log sensitive data?

### If Answer is Yes

1. [ ] Read relevant security rules
2. [ ] Check existing patterns in codebase
3. [ ] Ask if change is necessary
4. [ ] Add security review to acceptance criteria
