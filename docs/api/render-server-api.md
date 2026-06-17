# Render Server API

## Current Verified Baseline

Verified file: `backend/render-license-server/server.js`.

Endpoints:

- `GET /health`
- `POST /api/verify-license`
- `POST /api/heartbeat`
- `POST /api/logout`

`/api/verify-license` success response currently includes:

- `payload`: RS256 JWT.
- `sid`: session id.
- `license`: status, tier, middleCode, skipHashCheck, modulePolicy.
- `cfg`: dataSpreadsheetId, updateChannel.
- `supabase`: baseUrl, manifests.

Client compatibility note:

- `LicenseApiService` parses tier from root or `license.tier`.
- Module policy parsing currently appears to expect root `modulePolicy`; server returns nested `license.modulePolicy`. Mark effective module update policy as `NEED VERIFY`.

Security note:

- Server requires `JWT_PRIVATE_KEY` and `JWT_PUBLIC_KEY`.
- Firebase service account key is loaded from `./serviceAccountKey.json` on server. Do not commit/deploy this file through public channels.

The older details below are reference material.

## Overview

License server at https://autojms-api.onrender.com handles license verification and heartbeat.

## Endpoints

### POST /api/verify-license

Verify a license key and issue a session JWT.

**Request:**
```json
{
  "licenseKey": "XXXX-XXXX-XXXX",
  "hwid": "computed-hwid-from-client",
  "exeHash": "sha256-hash-of-exe",
  "appVersion": "1.26.6"
}
```

**Response (200 OK):**
```json
{
  "payload": "<jwt-token>",
  "sid": "<session-uuid>",
  "license": {
    "status": "active",
    "tier": "ULTRA",
    "middleCode": "0000",
    "skipHashCheck": true,
    "modulePolicy": {
      "autoUpdate": false,
      "silentUpdate": true,
      "applyOnNextStartup": true
    }
  },
  "cfg": {
    "dataSpreadsheetId": "",
    "updateChannel": "stable"
  },
  "supabase": {
    "baseUrl": "https://valmbajjpkjccqslsuou.supabase.co",
    "manifests": {
      "versionLatest": "manifest/version-latest.json",
      "hashManifest": "manifest/hash-manifest.json",
      "tierDefinitions": "manifest/tier-definitions.json"
    }
  }
}
```

**Response (401 Unauthorized):**
```json
{
  "error": "Key bản quyền không tồn tại hoặc không còn khả dụng."
}
```

**Response (403 Forbidden):**
```json
{
  "error": "Phần mềm không nguyên bản hoặc phiên bản đã quá cũ."
}
```

### POST /api/heartbeat

Maintain session and receive server commands.

**Headers:**
```
Authorization: Bearer <jwt-token>
```

**Response (continue):**
```json
{
  "action": "continue",
  "payload": "<new-jwt-token>"
}
```

**Response (kill):**
```json
{
  "action": "kill",
  "reason": "Phiên làm việc đã bị Admin thu hồi."
}
```

### POST /api/logout

Invalidate a session.

**Request:**
```json
{
  "sid": "<session-uuid>"
}
```

**Response:**
```json
{
  "ok": true
}
```

## JWT Token

### Claims

| Claim | Description |
|-------|-------------|
| key | License key |
| hwid | Hardware ID |
| sid | Session ID |
| tier | BASE or ULTRA |
| jti | Unique token ID (replay prevention) |
| iss | "autojms-license-server" |
| aud | "autojms-desktop-client" |
| exp | Expiration (60 minutes) |

### Validation (Client-side)

```csharp
// RS256 with hardcoded public key
var rsa = RSA.Create();
rsa.ImportFromPem(JWT_PUBLIC_KEY);

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
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| JWT_PRIVATE_KEY | RS256 private key (PEM) |
| JWT_PUBLIC_KEY | RS256 public key (PEM) |
| SUPABASE_BASE_URL | Supabase project URL |
| DEFAULT_UPDATE_CHANNEL | "stable" or "beta" |
| VALID_EXE_HASHES | Comma-separated allowed hashes |
| PORT | Server port (default: 3000) |

## Rate Limiting

| Endpoint | Limit |
|----------|-------|
| /api/verify-license | 20 requests/minute |
| /api/heartbeat | 120 requests/minute |

## Error Handling

| Status | Meaning |
|--------|---------|
| 400 | Missing required fields |
| 401 | Invalid license key |
| 403 | Invalid exe hash |
| 500 | Server error |

