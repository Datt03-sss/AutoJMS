# Tab & Form Ownership Boundaries

To prevent regressions, the tabs in `Main.cs` and the standalone `FullStackOperation` form must respect strict structural boundaries. Modifications to one tab's workflow must never leak into or compromise other modules.

---

## Tab Boundaries

### 1. HOME Tab
* **Primary Scope**: Main browsing area rendering `jms.jtexpress.vn`.
* **Related Files**:
  - `Forms/Main.cs` (WebView2 home instance, initialization, navigation handles)
  - `UI/WebViewHost.cs` (Host wrapper)
* **Rules**: 
  - Do not inject scripts that alter DOM structures used by the DKCH tab.
  - Do not change navigation triggers without design approval.

### 2. DKCH Tab
* **Primary Scope**: Return shipment registration.
* **Related Files**:
  - `Forms/Main.cs` (DKCH tab events, button triggers)
  - `Automation/WebViewAutomation.cs` (JS automation routines)
  - `Services/JmsResponseClassifier.cs` (Parses automation status)
* **Rules**:
  - Script automation logic must remain encapsulated inside `WebViewAutomation.cs`.
  - Do not access tracking services directly from this tab.

### 3. TRACKING Tab
* **Primary Scope**: Waybill status logs.
* **Related Files**:
  - `Forms/Main.cs` (Search textbox events, datagrid layouts)
  - `Tracking/WaybillTrackingService.cs` (Tracking API client wrapper)
  - `Tracking/ITrackingService.cs` (Abstraction contract)
* **Rules**:
  - All HTTP requests to retrieve waybill tracking history must flow through `WaybillTrackingService`.
  - Keep the datagrid double-buffering active to avoid visual flickering.

### 4. PRINT Tab
* **Primary Scope**: Thermal barcode printing.
* **Related Files**:
  - `Forms/Main.cs` (Print tab layout, button handles)
  - `Printing/PrintService.cs` (PDF processing and print spools)
  - `Printing/PrintSafetyGuard.cs` (Pre-print validation)
  - `Printing/PrinterPreflightService.cs` (Physical driver checks)
* **Rules**:
  - Do not alter the PdfiumViewer loading routines to prevent memory leakage.
  - Do not bypass `PrintSafetyGuard` checks (e.g. dimensions and labels counts).

### 5. ABOUT Tab
* **Primary Scope**: App info, update trigger, logs viewer.
* **Related Files**:
  - `Forms/Main.cs` (Displays metadata, launches Update checks)
  - `Updates/VelopackUpdateService.cs` (Velopack checks & apply)
* **Rules**:
  - The `ABOUT` tab **must always remain the last tab** in the SunnyUI TabControl collections.
  - Updates must be manual and require explicit user confirmation.

### 6. FullStackOperation Form (ULTRA Tier)
* **Primary Scope**: Standalone dashboard, Zalo chat, SLA monitoring.
* **Related Files**:
  - `Forms/FullStackOperation.cs` (Form shell)
  - All files in `Forms/FullStackOperation.*.cs` (Dashboard, chatbot, workspace, layout)
  - All folders under `FullStack/` (Services, repositories, SQLite local DB)
* **Rules**:
  - This form operates entirely separate from the BASE tab shell.
  - The form is hidden at startup and shown only if the user types `"DASH"` into the Home URL bar.
  - Keep all dashboard, chat, and SLA automation logic isolated to this form and the `FullStack/` services.

---

## Rules for Shared Services

When modifying shared services (e.g. `AppConfig`, `SettingsManager`, `JmsApiClient`, `SupabaseDbService`):
1. **Backward Compatibility**: Ensure your edits do not break existing JSON serialization schemas or encrypted storage paths.
2. **Side Effects**: Verify that adding configuration keys or properties does not cause null reference exceptions in other tabs during initialization.
3. **No Direct UI Coupling**: Shared services must never reference UI control states directly. They should return data objects and let UI event handlers marshal the updates.
4. **Log Masking**: Ensure any token or key handled by a shared service is masked when writing to debug logs.
