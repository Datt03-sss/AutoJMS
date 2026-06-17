# Firebase License Skill

## Overview

Firebase Realtime Database stores license data and sessions. Client accesses via Render server API, not directly.

## Architecture

```
Client ─────────────────────────────────────
         │                                   
         ▼ (HTTP)                           
Render Server (server.js)                     
         │                                   
         ▼ (Firebase Admin SDK)               
Firebase Realtime Database                   
         │                                   
         ├── Licenses/{key}/data             
         └── Sessions/{sid}/data            
```

## License Data Structure

```json
{
  "Licenses": {
    "XXXX-XXXX-XXXX-XXXX": {
      "status": "active",
      "tier": "ULTRA",
      "hwid": "hashed-machine-id",
      "activatedAt": 1708000000000,
      "middleCode": "0000",
      "skipHashCheck": true,
      "modulePolicy": {
        "autoUpdate": false,
        "silentUpdate": true,
        "applyOnNextStartup": true
      },
      "dataSpreadsheetId": "",
      "updateChannel": "stable"
    }
  }
}
```

## Session Data Structure

```json
{
  "Sessions": {
    "<session-uuid>": {
      "licenseKey": "XXXX-XXXX-XXXX-XXXX",
      "hwid": "hashed-machine-id",
      "tier": "ULTRA",
      "status": "active",
      "appVersion": "1.26.6",
      "ip": "client-ip",
      "createdAt": 1708000000000,
      "lastPing": 1708001200000
    }
  }
}
```

## Client Flow

### Verify License

```csharp
// Program.cs
var result = await LicenseApiService.VerifyLicenseSecureAsync(key, hwid);

// Server validates against Firebase
// Returns JWT + Supabase config
```

### Heartbeat

```csharp
// Program.cs - Background heartbeat
var heartbeat = new LicenseApiService.HeartbeatSupervisor(key, hwid, token, ...);
_ = heartbeat.StartAsync(AppCts.Token);

// Server updates lastPing in Firebase
```

### Server-Side (server.js)

```javascript
const admin = require('firebase-admin');
admin.initializeApp({
    credential: admin.credential.cert(serviceAccount),
    databaseURL: 'https://keyauthjms-default-rtdb.asia-southeast1.firebasedatabase.app/'
});

// Verify license
const ref = admin.database().ref(`Licenses/${licenseKey}`);
const snap = await ref.once('value');
const data = snap.val();

// Create session
const sessionId = crypto.randomUUID();
await admin.database().ref(`sessions/${sessionId}`).set({
    licenseKey,
    hwid,
    tier,
    status: 'active',
    // ...
});
```

## Security

### Firebase Rules (Server-Side)

```json
{
  "rules": {
    "Licenses": {
      "$key": {
        ".read": false,
        ".write": false
      }
    },
    "Sessions": {
      "$key": {
        ".read": false,
        ".write": false
      }
    }
  }
}
```

### HWID Lock

```javascript
// Check HWID matches
if (data.hwid && data.hwid !== hwid) {
    return res.status(401).json({
        error: "Key này đang được sử dụng trên một máy tính khác."
    });
}
```

## License Response

```csharp
public class VerifyResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string Token { get; set; }  // JWT
    public string Tier { get; set; }  // BASE or ULTRA
    public string SupabaseBaseUrl { get; set; }
    public SupabaseManifestUrls Manifests { get; set; }
    public string UpdateChannel { get; set; }
    // ...
}
```

## JWT Token

### Server Issues JWT

```javascript
jwt.sign({
    key: licenseKey,
    hwid: hwid,
    sid: sessionId,
    tier: tier,
    jti: crypto.randomUUID()  // For replay prevention
}, PRIVATE_KEY, {
    algorithm: 'RS256',
    expiresIn: '60m',
    issuer: 'autojms-license-server',
    audience: 'autojms-desktop-client'
});
```

### Client Validates JWT

```csharp
// LicenseApiService.cs
private static bool ValidateJwtToken(string token)
{
    var rsa = RSA.Create();
    rsa.ImportFromPem(JWT_PUBLIC_KEY.ToCharArray());
    
    var parameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = "autojms-license-server",
        ValidateAudience = true,
        ValidAudience = "autojms-desktop-client",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(2),
        IssuerSigningKey = new RsaSecurityKey(rsa)
    };
    
    var handler = new JwtSecurityTokenHandler();
    handler.ValidateToken(token, parameters, out _);
    return true;
}
```

## Common Issues

| Issue | Cause | Solution |
|-------|--------|----------|
| Key not found | License not in Firebase | Add license via server/admin |
| HWID mismatch | Key used on different machine | Reset HWID in Firebase |
| Token expired | 60 min lifetime | Heartbeat refreshes token |
| Session revoked | Server deleted session | Re-verify license |
