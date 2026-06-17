# Auth Token Architecture

## Two Token Types

| Token | Source | Purpose | Format | Lifetime |
|-------|--------|---------|--------|---------|
| License JWT | Render server | License activation/heartbeat | JWT (RS256) | 60 min |
| JMS AuthToken | JMS WebView2 | JMS API calls | 32 hex chars | Until JMS logout |

**Critical**: Never confuse these tokens. License JWT is for license server only.

## License JWT Flow

```
Render Server (server.js)                 AutoJMS Client
        │                                    │
        │  Sign JWT with private key         │
        │  (RS256, 60 min, issuer=autojms-license-server)│
        │◀───────────────────────────────────│
        │  POST /api/verify-license          │
        │                                    │
        │  Return JWT in payload field       │
        │                                    │
        │  Validate locally:                  │
        │  - RS256 signature                │
        │  - Issuer = autojms-license-server│
        │  - Audience = autojms-desktop-client│
        │  - Not expired                    │
        │                                    │
        │  Store in memory only (not persisted)│
        │                                    │
        │  For heartbeat:                    │
        │  - Send JWT to /api/heartbeat      │
        │  - Server returns new JWT          │
```

## JMS AuthToken Flow

```
JMS Website (jtexpress.vn)              AutoJMS Client
        │                                    │
        │  User logs into JMS in WebView2    │
        │  JMS sets authToken in localStorage│
        │                                    │
        │  Capture via header:               │
        │  CoreWebView2_WebResourceRequested │
        │  (intercept fetch/XHR headers)    │
        │                                    │
        │  Capture via JS:                  │
        │  RefreshAuthTokenAsync()           │
        │  (inject JS to read localStorage) │
        │                                    │
        │  Validate shape:                  │
        │  - Must be 32 hex characters       │
        │  - Reject JWT (has dots)          │
        │  - Reject GUID (has dashes)       │
        │                                    │
        │  Store in:                        │
        │  - JmsAuthStateService (memory)    │
        │  - AutoJMS.json (LastAuthToken)  │
```

## Token Priority

JmsAuthTokenService.ResolveTokenAsync() uses priority:

1. **In-memory**: `JmsAuthStateService.CurrentToken`
2. **WebView2**: Fresh read from JMS localStorage
3. **Config**: `LastAuthToken` from AutoJMS.json

## Token Capture Points

### 1. Request Header Interception

```csharp
// Main.cs - CoreWebView2_WebResourceRequested
CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Fetch);
CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.XmlHttpRequest);

CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

private void CoreWebView2_WebResourceRequested(...)
{
    string[] tokenNames = { "authToken", "YL_TOKEN", "token", "accessToken" };
    foreach (var name in tokenNames)
    {
        if (e.Request.Headers.Contains(name))
        {
            var token = e.Request.Headers.GetHeader(name);
            ApplyCapturedToken(token);
        }
    }
}
```

### 2. LocalStorage Read

```csharp
// Main.cs - RefreshAuthTokenAsync
string js = @"
    (function() {
        var HEX32 = /^[a-fA-F0-9]{32}$/;
        var stores = [localStorage, sessionStorage];
        for (var s of stores) {
            for (var k of ['YL_TOKEN','authToken','token']) {
                var v = s.getItem(k);
                if (v && HEX32.test(v)) return v;
            }
        }
        return null;
    })();
";
string result = await webView.ExecuteScriptAsync(js);
```

## Token Validation

### Shape Validation

```csharp
// JmsAuthTokenService
private static readonly Regex HexToken32 = 
    new("^[a-fA-F0-9]{32}$", RegexOptions.Compiled);

public static bool IsValidJmsToken(string t)
    => !string.IsNullOrEmpty(t) && HexToken32.IsMatch(t);
```

### JWT Rejection

```csharp
public static bool LooksLikeJwt(string t)
{
    if (string.IsNullOrEmpty(t)) return false;
    if (!t.StartsWith("eyJ")) return false;
    int dots = 0;
    foreach (var c in t) if (c == '.') dots++;
    return dots == 2;  // JWT has 3 parts = 2 dots
}
```

## 401 Handling

```csharp
// JmsApiClient.PostJsonAsync

var response = await httpClient.SendAsync(request);

// First attempt
if (response.StatusCode == HttpStatusCode.Unauthorized)
{
    // Force refresh from WebView2
    await JmsAuthTokenService.ForceRefreshFromWebViewAsync();
    
    // Retry once
    response = await RetryWithNewToken();
    
    // If still 401, truly expired
    if (response.StatusCode == HttpStatusCode.Unauthorized)
    {
        JmsAuthTokenService.NotifyReallyExpired();
        // Clears token, stops background jobs
    }
}
```

## Token Storage

| Storage | Location | Encryption | Purpose |
|---------|----------|------------|---------|
| Memory | JmsAuthStateService | None | Fast access |
| File | AutoJMS.json | None | Persistence |
| Secure | AutoJMS.secure | AES-CBC-HMACSHA256 | Sensitive config |

## Token Masking

Full tokens must not be logged in production:

```csharp
// CORRECT - masking
AppLogger.Info($"Token: {token.Substring(0,4)}...{token.Substring(token.Length-4)}");

// WRONG - full token logged
AppLogger.Info($"Token: {token}");
```

## State Machine

```
┌─────────┐    User login     ┌─────────┐
│  NONE   │───────────────▶│  IDLE   │
└─────────┘                 └────┬────┘
      ▲                          │
      │ Token captured            │ Token available
      │                          ▼
      │                    ┌─────────┐
      │                    │ ACTIVE  │──────► Token expired
      │                    └────┬────┘         (401 → NotifyReallyExpired)
      │                          │
      │                          │ User logout
      │                          ▼
      └─────────────────────►┌─────────┐
                            │  NONE   │
                            └─────────┘
```
