# AutoJMS Codebase Map

This document details the architectural modules, their respective folders, primary class files, developer editing rules, and identified risks.

---

## 1. Module Catalog

### App Bootstrap & Shell
* **Directory**: `src/AutoJMS/`
* **Key Files**: 
  - `Program.cs` (Entry point)
  - `Forms/Main.cs` / `Forms/Main.Designer.cs` (Shell Form)
  - `Forms/TabManager.cs` (Tab registration)
* **Role**: Configures high-DPI behavior, runs licensing checks, loads manifests, initializes WebView2 controls, and constructs the tab hierarchy.
* **Editing Permissions**:
  - **Owner**: Full access.
  - **AI Agents**: *Read-only*. Cannot modify without explicit design approval.

### Licensing & Tier Enforcement
* **Directory**: `src/AutoJMS/Licensing/`, `src/AutoJMS/Policies/`
* **Key Files**:
  - `LicenseApiService.cs` (手動 verification & heartbeat with Render license server)
  - `TierRuntimePolicy.cs` (Exposes feature flags based on client tier)
  - `TierDefinitions.cs` (Loads definitions from configuration)
  - `RuntimePolicyApplier.cs` (Applies tier limitations to WinForms tabs)
* **Role**: Authenticates users, validates RS256 JWT, caches license key locally using DPAPI, and restricts features on the BASE tier.
* **Editing Permissions**:
  - **Owner**: Full access.
  - **AI Agents**: *Read-only* (🔒 Frozen).

### Update & Integrity
* **Directory**: `src/AutoJMS/Updates/`
* **Key Files**:
  - `VelopackUpdateService.cs` (Automated major updates from GitHub via Velopack SDK)
  - `SmallUpdateService.cs` (Automated minor runtime selector/config updates)
  - `SupabaseManifestService.cs` (Fetches `version-latest.json` from Supabase)
  - `Licensing/HashVerifier.cs` (Validates executable checksums at startup)
* **Role**: Ensures application binaries remain up-to-date and untampered with.
* **Editing Permissions**:
  - **Owner**: Full access.
  - **AI Agents**: *Read-only* (🔒 Frozen).

### WebView2 & Browser Automation
* **Directory**: `src/AutoJMS/Automation/`, `src/AutoJMS/UI/`
* **Key Files**:
  - `WebViewAutomation.cs` (Handles JS injections and DOM queries)
  - `WebViewHost.cs` (Initializes WebView2 components)
  - `Automation/DevTools/` (Debug capture tools)
* **Role**: Renders the JMS web portal and executes script actions to automate shipment booking.
* **Editing Permissions**:
  - **Owner**: Full access.
  - **AI Agents**: *Allowed* (with caution). May update selectors in `WebViewAutomation.cs` if the JMS frontend structure changes.

### Waybill Tracking & JMS Integration
* **Directory**: `src/AutoJMS/Tracking/`, `src/AutoJMS/Services/`
* **Key Files**:
  - `WaybillTrackingService.cs` (Orchestrates queries)
  - `JmsApiClient.cs` (Sends HTTP requests to the JMS gateway API)
  - `JmsResponseClassifier.cs` (Parses api error and state structures)
* **Role**: Interacts with backend JMS HTTP APIs to fetch shipment history and waybill details.
* **Editing Permissions**:
  - **Owner**: Full access.
  - **AI Agents**: *Allowed*. Safe to add helper methods or refine response parsing logic.

### Label Printing
* **Directory**: `src/AutoJMS/Printing/`
* **Key Files**:
  - `PrintService.cs` (Extracts label data and routes printing)
  - `PrintSafetyGuard.cs` (Prevents invalid label prints)
  - `PrinterPreflightService.cs` (Checks paper and spool status)
* **Role**: Leverages PdfiumViewer to render label pdfs and direct them to physical or virtual printing drivers.
* **Editing Permissions**:
  - **Owner**: Full access.
  - **AI Agents**: *Read-only* (🔒 Frozen).

### Standalone Operation Center (FullStack)
* **Directory**: `src/AutoJMS/FullStack/`
* **Key Files**:
  - `Forms/FullStackOperation.cs` (Dashboard WinForms UI shell)
  - `FullStack/Services/` (Inventory fetches, workflows, risk assessments)
  - `FullStack/Repositories/` (LocalSQLite database operations)
  - `FullStack/LocalDb/` (SQLite Initialization, migrations, connection setups)
* **Role**: Standalone management portal for ULTRA tier users, handling waybill databases, Zalo notifications, and spreadsheet auto-updates.
* **Editing Permissions**:
  - **Owner**: Full access.
  - **AI Agents**: *Allowed*. This is a designated safe zone. Agents can implement repository extensions, add background workflow steps, and enhance metrics parsing.

---

## 2. Risk & Unknown Areas

1. **WebView2 Multithread Safety**:
   - WebView2 requires all calls to run strictly on the WinForms UI thread.
   - *Risk*: Spawning background task threads that invoke WebView2 controls directly will crash the app.
2. **Supabase Anon Key Abuse**:
   - The public Supabase key is hardcoded.
   - *Risk*: Relies entirely on Supabase Row Level Security (RLS) policies. If RLS is misconfigured, unauthorized clients could manipulate the global waybill tables.
3. **Obfuscation Conflict**:
   - Post-build obfuscation is done by .NET Reactor.
   - *Risk*: Refactoring/renaming classes or using Reflection might cause startup failures after Reactor obfuscation is applied.
