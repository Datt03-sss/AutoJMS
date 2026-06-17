# Firebase License Schema

## Database URL

```
https://keyauthjms-default-rtdb.asia-southeast1.fireasedatabase.app
```

## Schema

### Licenses Node

```
Licenses/
└── {license-key}/
    ├── status: string ("active" | "revoked" | "expired")
    ├── tier: string ("BASE" | "ULTRA")
    ├── hwid: string | null
    ├── activatedAt: number (timestamp)
    ├── middleCode: string
    ├── skipHashCheck: boolean
    ├── modulePolicy/
    │   ├── autoUpdate: boolean
    │   ├── silentUpdate: boolean
    │   └── applyOnNextStartup: boolean
    ├── dataSpreadsheetId: string
    └── updateChannel: string ("stable" | "beta")
```

### Sessions Node

```
Sessions/
└── {session-uuid}/
    ├── licenseKey: string
    ├── hwid: string
    ├── tier: string
    ├── status: string ("active")
    ├── appVersion: string
    ├── ip: string
    ├── createdAt: number (timestamp)
    └── lastPing: number (timestamp)
```

## License Record Example

```json
{
  "XXXX-XXXX-XXXX": {
    "status": "active",
    "tier": "ULTRA",
    "hwid": "a1b2c3d4...",
    "activatedAt": 1748000000000,
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
```

## Session Record Example

```json
{
  "550e8400-e29b-41d4-a716-446655440000": {
    "licenseKey": "XXXX-XXXX-XXXX",
    "hwid": "a1b2c3d4...",
    "tier": "ULTRA",
    "status": "active",
    "appVersion": "1.26.6",
    "ip": "203.0.113.42",
    "createdAt": 1748000000000,
    "lastPing": 1748001200000
  }
}
```

## Operations

### Read License

```javascript
const ref = admin.database().ref(`Licenses/${licenseKey}`);
const snap = await ref.once("value");
const data = snap.val();
```

### Create Session

```javascript
const sessionId = crypto.randomUUID();
await admin.database().ref(`sessions/${sessionId}`).set({
    licenseKey,
    hwid,
    tier,
    status: "active",
    appVersion,
    ip: getClientIp(req),
    createdAt: Date.now(),
    lastPing: Date.now()
});
```

### Update Session Ping

```javascript
await admin.database().ref(`sessions/${sid}`).update({
    lastPing: Date.now()
});
```

### Delete Session

```javascript
await admin.database().ref(`sessions/${sid}`).remove();
```

### Delete Old Sessions (Same License/Device)

```javascript
const sessionsSnap = await sessionsRef
    .orderByChild("licenseKey")
    .equalTo(licenseKey)
    .once("value");

sessionsSnap.forEach(child => {
    if (child.val().hwid === hwid) {
        child.ref.remove();
    }
});
```

## Security Rules

Only server (Render) has access. Client never connects directly to Firebase.

```json
{
  "rules": {
    "Licenses": {
      ".read": false,
      ".write": false
    },
    "Sessions": {
      ".read": false,
      ".write": false
    }
  }
}
```
