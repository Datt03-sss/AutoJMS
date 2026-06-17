# Project Overview

## Current Verified Baseline

Verified from the current repository on 2026-06-03.

AutoJMS is a .NET 8 WinForms desktop application for Vietnamese logistics automation. It uses SunnyUI for UI controls and WebView2 for JMS/Zalo browser surfaces.

Core modules in the current checkout:

- `Program.cs`: startup, single-instance mutex, HWID, license verification, service initialization, module sync, integrity check.
- `Main.cs`: main WinForms UI with BASE tabs `HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`.
- `FullStackOperation.cs`: ULTRA-only standalone form, not a tab. It is pre-created/shown only when `TierRuntimePolicy.EnableFullStackOperation` is true.
- `JmsApiClient.cs`, `JmsAuthTokenService.cs`, `JmsAuthStateService.cs`: JMS API and 401/token recovery.
- `SupabaseDbService.cs`, `InventorySyncService.cs`, `DatabaseTracking.cs`: ULTRA inventory/database sync path.
- `VelopackUpdateService.cs`, `SmallUpdateService.cs`, `MajorUpdateService.cs`: update flows.
- `ModuleSystem/`: dynamic module provider/loader and built-in fallback implementations.

External service roles:

- Firebase Realtime Database: license/session/tier data used by the Render server.
- Render `server.js`: license verification, heartbeat, logout.
- Supabase Storage: manifest/config/hash/tier/selector-update files.
- Supabase PostgreSQL: waybill tracking data and RPCs. SQL definitions for the waybill RPCs are not present in `supabase-migration.sql`; mark database bootstrap as `NEED VERIFY`.
- GitHub Releases: large Velopack binary hosting. Do not upload `.nupkg` to Supabase.
- Inno Setup: first install/reinstall/uninstall and runtime prerequisites.
- Velopack: in-app updates, major update from About tab only.

Known verified build note:

- Historical build issue: root `modules/*.json` content files referenced by `src/AutoJMS/AutoJMS.csproj` were missing.
- Current fix: the relevant `Content Include` entries now have `Condition="Exists('...')"` guards.
- Latest recorded verification: `dotnet build src/AutoJMS/AutoJMS.csproj -c Debug` succeeded with warnings only.

The older sections below remain useful context. If they conflict with this baseline, use this baseline and mark the conflicting point `NEED VERIFY`.

## What is AutoJMS?

AutoJMS is a **desktop logistics automation application** for Vietnamese logistics operations. It automates:

- **DKCH** (Đăng ký chuyển hoàn): Automated return shipment registration via WebView2 browser automation
- **TRACKING**: Waybill tracking and monitoring
- **PRINT**: Label printing (chuyển hoàn, chuyển tiếp, lại đơn, reverse)
- **FullStackOperation** (ULTRA only): Realtime dashboard, Thời hiệu monitoring, Zalo chat integration

## Tech Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Runtime | .NET 8 | 8.0 |
| UI Framework | WinForms | .NET 8 |
| UI Library | SunnyUI | 3.9.6 |
| Browser | WebView2 | 1.0.3912.50 |
| Database | Supabase (PostgreSQL) | - |
| Auth | Firebase Realtime Database | - |
| License Server | Render (Express/Node.js) | - |
| Installer | Inno Setup | 6.x |
| Auto-Update | Velopack | 0.0.1297 |
| Protection | .NET Reactor | - |

## Key External Services

| Service | Purpose | URL/Location |
|---------|---------|--------------|
| JMS Frontend | Browser automation target | jms.jtexpress.vn |
| JMS API Gateway | Backend API calls | jmsgw.jtexpress.vn |
| License Server | Verify license, heartbeat | autojms-api.onrender.com |
| Firebase | License/session data | keyauthjms-default-rtdb |
| Supabase | Waybill DB, manifests, storage | valmbajjpkjccqslsuou.supabase.co |
| GitHub Releases | Velopack binary hosting | Datt03-sss/AutoJMS-Update |

## Current Version

```
Version: 1.26.6
AssemblyVersion: 1.26.6.0
FileVersion: 1.26.6.0
InformationalVersion: 1.26.6
```

## Build Configuration

- **Self-contained**: Yes (includes .NET runtime)
- **Runtime Identifier**: win-x64
- **PublishSingleFile**: No
- **PublishTrimmed**: No

## Project Structure at a Glance

```
AutoJMS/ (root = project root)
├── AutoJMS.slnx                    # Solution file
├── src/
│   ├── AutoJMS/                    # Main WinForms project
│   │   ├── AutoJMS.csproj
│   │   ├── Program.cs
│   │   ├── Forms/
│   │   ├── Config/
│   │   ├── Licensing/
│   │   ├── Updates/
│   │   ├── Services/
│   │   ├── ModuleSystem/
│   │   ├── Resources/
│   │   └── Properties/
│   └── AutoJMS.Abstractions/       # Shared module contracts
│
├── archive/old-module-system/       # Legacy module projects
│   ├── AutoJMS.RetryPolicy/
│   └── AutoJMS.Selectors/
│
├── backend/render-license-server/  # Render license/heartbeat server
├── infra/
│   ├── firebase/
│   ├── supabase/
│   └── github-release/
│
├── installer/inno/                      # Inno Setup scripts
│   └── AutoJMS.iss
│
├── release/                       # Build/release scripts
│   └── build-release.ps1
│
├── docs/                          # Project documentation
├── .agent/                        # Agent context & rules
│
├── SupabaseDbService.cs           # Supabase waybill operations
├── InventorySyncService.cs        # JMS inventory fetch
├── JmsApiClient.cs               # JMS API HTTP client
├── JmsAuthTokenService.cs        # JMS session token management
├── LicenseApiService.cs           # License verify/heartbeat
├── VelopackUpdateService.cs      # In-app updates
├── SmallUpdateService.cs         # Selector/config updates
├── MajorUpdateService.cs         # Major version updates
├── HashVerifier.cs               # DLL integrity check
├── TierRuntimePolicy.cs          # BASE/ULTRA policy enforcement
└── AppPaths.cs                   # Path management
```

## Development vs Production

| Aspect | Development | Production |
|--------|-------------|------------|
| Debugger check | Bypassed | Exits if debugger attached |
| .NET Reactor | Disabled | Applied to AutoJMS.dll |
| Logs | Verbose | Minimal |
| Token logging | Full | Masked (TODO) |
| HWID | Computed | Computed |

## Critical Paths

1. **License Flow**: frmLogin → LicenseApiService.VerifyLicenseSecureAsync → JWT validation → Supabase config → InitializeServicesFromLicense
2. **AuthToken Flow**: WebView2 Navigation → CoreWebView2_WebResourceRequested → JmsAuthStateService → Main.CapturedAuthToken
3. **Update Flow**: tabAbout_btnCheckUpdate_Click → VelopackUpdateService → GithubSource (provider=github) → ApplyUpdatesAndRestart
4. **DKCH Flow**: tabDKCH_btnDKCH1_Click → DkchManager → WebViewAutomation → JMS API → tracking update

