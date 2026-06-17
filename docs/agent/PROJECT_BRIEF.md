# AutoJMS — Project Brief

> Canonical reference for AI coding agents. See also: [AGENTS.md](../../AGENTS.md)

## What is AutoJMS?
AutoJMS is a **.NET 8 WinForms desktop application** built to automate Vietnamese logistics operations on the J&T Express (JMS) platform. It provides automated return-shipment registration (DKCH), waybill tracking, label printing, and a standalone operation center (ULTRA tier only) featuring Zalo alerts and Google Sheets synchronization.

---

## Ownership Model

| Role | Responsibility |
|---|---|
| **Owner (Human)** | Review PRs, run manual validation/testing, merge to main branch, build and upload production releases. |
| **AI Agents** | Work on feature branches or worktrees, run the harness validation locally, and open PRs. |

*Agents must never commit directly to the `main` branch, tag production releases, or upload binaries.*

---

## Technology Stack

| Component | Technology | Version |
|---|---|---|
| **Runtime** | .NET 8 (net8.0-windows) | 8.0 |
| **Architecture** | Windows Forms | Built-in |
| **UI Library** | SunnyUI | 3.9.6 |
| **Browser Control** | WebView2 | 1.0.3912.50 |
| **Cloud Database** | Supabase PostgreSQL | — |
| **Licensing DB** | Firebase Realtime DB | — |
| **License API** | Node.js/Express (Render hosting) | — |
| **Installer** | Inno Setup | 6.x |
| **Updater** | Velopack | 0.0.1297 (SDK package) |
| **Obfuscation** | .NET Reactor | — |
| **PDF Renderer** | PdfiumViewer | 2.14.5 |
| **Excel Parser** | ClosedXML | 0.105.0 |

---

## External Services

| Service | Identifier | Purpose |
|---|---|---|
| **JMS Portal** | `jms.jtexpress.vn` | Target browser for automation. |
| **JMS Gateway** | `jmsgw.jtexpress.vn` | Direct HTTP API integration. |
| **License Server** | `autojms-api.onrender.com` | Handshake, heartbeat, and license verify. |
| **Firebase RTDB** | `keyauthjms-default-rtdb` | Storage of active licenses, keys, and tiers. |
| **Supabase** | `bnsnnrlwfzxemmizknwy.supabase.co` | Storage of manifests, selectors, integrity hashes, and waybill records. |
| **GitHub Releases** | `Datt03-sss/AutoJMS-Update` | Hosting binary `.nupkg` releases for Velopack. |

---

## Solution Structure
- `AutoJMS.slnx` (Solution catalog)
- `src/AutoJMS/AutoJMS.csproj` (Main application, net8.0-windows, win-x64, self-contained)
- `src/AutoJMS.Abstractions/AutoJMS.Abstractions.csproj` (Shared module contracts)

---

## Current Version
- **Version**: `1.26.6`
- **AssemblyVersion**: `1.26.6.0`
- **Distribution**: self-contained win-x64 bundle.

---

## Two-Tier Capabilities

| Feature | BASE Tier | ULTRA Tier |
|---|:---:|:---:|
| **HOME Tab** (WebView2 browse) | ✓ | ✓ |
| **DKCH Tab** (Return registration) | ✓ | ✓ |
| **TRACKING Tab** (Waybill logs) | ✓ | ✓ |
| **PRINT Tab** (Label printer) | ✓ | ✓ |
| **ABOUT Tab** (Version & manual update) | ✓ | ✓ |
| **FullStackOperation Form** (Dashboard) | ✗ | ✓ |
| **Background Realtime Sync** | ✗ | ✓ |
| **Background Database Tracking** | ✗ | ✓ |
| **Auto-Sync Timer** | ✗ | ✓ |
| **Zalo Alerts** | ✗ | ✓ |
