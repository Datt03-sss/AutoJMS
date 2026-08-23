# System Overview

## Current Verified Baseline

AutoJMS is a .NET 8 WinForms desktop logistics automation app using SunnyUI and WebView2.

Current system boundaries:

- Desktop client: WinForms UI, WebView2 automation, JMS API client, print/export, config, update, and dynamic module loading.
- License backend: Render `server.js` with Firebase Realtime Database.
- Data/control backend: the DataHub API on the VPS (`dev.jmsauto.online`) — ASP.NET Core behind Caddy — owning device enrollment, JMS ingest, the change feed, the fetch lease, the SignalR hub, and the manifest/config JSON. PostgreSQL sits behind it on a private Docker network with no client access.
- Binary distribution: Inno Setup for first install and Velopack/GitHub Releases for in-app updates.

Tier boundary:

- BASE: `HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT` manual workflows.
- ULTRA: BASE plus standalone `FullStackOperation` form and background inventory/database sync.

Current verification gaps:

- Historical build blocker from missing root `modules/*.json` content files was fixed with conditional content includes; latest recorded Debug build succeeded with warnings only.
- Resolved 2026-08-23: there are no waybill RPCs. `DataHubClient` calls REST endpoints only, and the schema lives in `backend/datahub/migrations/` (twelve tables, five forward-only files).
- Some older sections may describe intended structure; mark unverified details `NEED VERIFY`.

## What is AutoJMS?

AutoJMS is a **desktop logistics automation application** that streamlines Vietnamese logistics operations by automating:

- **DKCH** (Đăng ký chuyển hoàn): Automated return shipment registration via browser automation
- **Tracking**: Real-time waybill tracking and monitoring
- **Printing**: Label generation and printing for various shipment types
- **FullStackOperation** (ULTRA only): Advanced dashboard, SLA monitoring, and Zalo integration

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AUTOJMS CLIENT                                  │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  WinForms UI (SunnyUI)                                       │   │
│  │  Main.cs (TabControl) │ FullStackOperation.cs (ULTRA)        │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Services Layer                                             │   │
│  │  LicenseApiService │ JmsApiClient │ VelopackUpdateService   │   │
│  │  InventorySyncService │ DataHubClient │ PrintService    │   │
│  └─────────────────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │  Module System (Dynamic Loading)                            │   │
│  │  ModuleSystem/ │ archive/old-module-system/                           │   │
│  └─────────────────────────────────────────────────────────────┘   │
└───────────────────────────────┬─────────────────────────────────────┘
                                │
        ┌─────────────────────┼─────────────────────┐
        │                     │                     │
        ▼                     ▼                     ▼
┌───────────────┐    ┌───────────────┐    ┌───────────────┐
│  JMS Website │    │  License     │    │  DataHub   │
│  (jtexpress)│    │  Server      │    │  API (VPS)  │
│  via WebView2│    │  (Render)    │    │             │
└───────────────┘    └───────┬───────┘    └───────────────┘
                              │
                              ▼
                      ┌───────────────┐
                      │  Firebase    │
                      │  (License   │
                      │   Data)      │
                      └───────────────┘
```

## Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET 8 | 8.0 |
| UI Framework | WinForms | Built-in |
| UI Library | SunnyUI | 3.9.6 |
| Browser | WebView2 | 1.0.3912.50 |
| Database | PostgreSQL 16 in Docker (private) | - |
| Auth | Firebase Realtime DB | - |
| License Server | Node.js/Express | - |
| Installer | Inno Setup | 6.x |
| Auto-Update | Velopack | 0.0.1297 |
| Protection | .NET Reactor | - |

## Two-Tier Model

### BASE Tier

Core features available to all users:
- HOME: JMS browser
- DKCH: Return shipment automation
- TRACKING: Manual waybill tracking
- PRINT: Label printing
- ABOUT: Version info and updates

### ULTRA Tier

Advanced features for power users:
- All BASE features
- FullStackOperation form (dashboard, SLA, Zalo)
- Automatic inventory synchronization
- Background database tracking
- Realtime updates

## Update Mechanism

| Update Type | Trigger | Tool | Binary Source |
|------------|---------|------|---------------|
| Small Config | Auto | SmallUpdateService | DataHub |
| Major Version | Manual | Velopack | GitHub Releases |
| First Install | Manual | Inno Setup | Bundled |

## Security Model

- **License JWT**: RS256 signed, issued by Render server, stored in memory only
- **JMS AuthToken**: 32-char hex, captured from WebView2, persisted in AutoJMS.json
- **Config Encryption**: AES-CBC-HMACSHA256 for sensitive config
- **HWID Lock**: License bound to hardware ID

## Development vs Production

| Aspect | Development | Production |
|--------|-------------|------------|
| Debugger Check | Bypassed | Exits if detected |
| .NET Reactor | Disabled | Applied to AutoJMS.dll |
| Token Logging | Full | Masked (TODO) |
| Logs | Verbose | Minimal |

