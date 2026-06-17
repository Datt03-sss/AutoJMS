# Do Not Break Existing Logic Rules

## Core Principle

**Existing behavior is the contract.** If something works, don't break it.

## Phase 1 Non-Break Rules

- HOME/DKCH/TRACKING/PRINT/ABOUT are the core BASE surface. Do not modify them unless the task explicitly requires it.
- TRACKING manual must keep working on BASE.
- PRINT manual must keep working on BASE.
- FullStackOperation is ULTRA-only.
- FullStackOperation is not a tab.
- ABOUT must remain the last tab.
- Do not remove old fallback behavior unless there is a migration plan.
- Do not remove `ModuleSystem` unless there is a dedicated task for it.
- Do not change `AutoJMS.json` schema without migration.
- Do not copy files manually into `C:\AutoJMS\current`.
- Do not let app update open a GitHub browser page.

## HOME Tab

### What It Does

- JMS WebView2 browser
- URL bar with navigation
- DASH command for FullStackOperation
- Navigation buttons (back, forward, reload, home)

### What NOT to Change

- URL bar "DASH" detection pattern
- WebView2 shared BrowserData folder
- Navigation behavior
- Token capture via WebResourceRequested

### If You Must Change

1. Ensure DASH command still works
2. Ensure token capture still works
3. Test navigation thoroughly

## DKCH Tab

### What It Does

- Automated return shipment registration
- DKCH1 and DKCH2 modes
- Manual waybill input
- Real-time tracking history
- Counters (saved, total)

### What NOT to Change

- DkchManager.StartAsync flow
- WebViewAutomation selectors
- Error classification (NeedSwitchToDkch1Exception, NeedSwitchToDkch2Exception)
- DKCH1/DKCH2 mode distinction

### If You Must Change

1. Test both DKCH1 and DKCH2 modes
2. Verify error handling still works
3. Test manual waybill input

## TRACKING Tab

### What It Does

- Waybill search
- Results grid
- Export to Excel
- Upload BUMP to Google Sheet
- Manual sync

### What NOT to Change

- WaybillNormalization regex patterns
- Search logic (should accept 8XXXXXXXXXXX, letters, etc.)
- Grid column configuration
- BUMP upload sheet name

### If You Must Change

1. Test various waybill formats
2. Verify export still works
3. Test upload to BUMP sheet

## PRINT Tab

### What It Does

- Waybill search for printing
- Multiple print modes (chuyển hoàn, chuyển tiếp, lại đơn, reverse)
- PDF generation and download
- System printer integration
- Print count limiting (max 3)

### What NOT to Change

- PDF generation API call
- Printer integration (PdfiumViewer)
- Print count logic (max 3 per waybill)
- PDF retention (keep 500)
- Log retention (keep 3 days)

### If You Must Change

1. Test all print modes
2. Verify print count still enforced
3. Test PDF download

## ABOUT Tab

### What It Does

- Version display
- Update check button
- Velopack update flow

### What NOT to Change

- Update trigger (must be manual via button)
- Version display format
- Velopack GithubSource provider=github logic

### If You Must Change

1. Verify update check still works
2. Verify no browser opens

## Authentication Flow

### What It Does

- License verify via Render server
- JWT validation (RS256)
- Tier determination
- Offline-first with cached key
- Heartbeat to server

### What NOT to Change

- JWT validation (RS256 with hardcoded key)
- Offline-first behavior
- Heartbeat interval (2 minutes)
- Token format (32 hex)

### If You Must Change

1. Test online activation
2. Test offline fallback
3. Test heartbeat
4. Test HWID lock

## WebView2 Automation

### What It Does

- Vue/Element UI form filling
- DKCH submission
- Token capture from headers

### What NOT to Change

- Element UI selector patterns
- Vue input setter (nativeInputValueSetter)
- Form submission click pattern
- WaitForApiResponseAsync timeout

### If You Must Change

1. Test in browser DevTools first
2. Verify selector works with Vue
3. Test timeout scenarios

## Tier Policy

### What It Does

- BASE: Core features only
- ULTRA: Full features + FullStackOperation

### What NOT to Change

- TierRuntimePolicy resolution
- Tier guards in MainForm
- Background job restrictions for BASE

### If You Must Change

1. Test with BASE license
2. Test with ULTRA license
3. Verify no background jobs on BASE

## Background Jobs (ULTRA Only)

### What Runs on ULTRA

- _autoSyncTimer (1 second interval)
- InventorySyncService
- DatabaseTracking
- FullStackOperation realtime

### What MUST NOT Run on BASE

- Any of the above

### If You Must Change

1. Verify tier guards are correct
2. Check TierRuntimePolicy flags
3. Test both tiers

## Storage

### What Is Stored

- AutoJMS.json: User settings, LastAuthToken
- AutoJMS.secure: Encrypted config
- license.dat: Encrypted license key
- BrowserData: WebView2 state
- debug.log: Application logs

### What NOT to Change

- AutoJMS.json schema
- Encryption method (AES-CBC-HMACSHA256)
- Path separation (UserDataDir vs InstallDir)

### If You Must Change

1. Provide migration path
2. Test on existing installations
3. Document schema changes

## Module System

### What It Does

- Dynamic module loading
- Selector configuration
- Runtime config updates

### What NOT to Change

- Module discovery mechanism
- Active module selection
- Selector override priority

### If You Must Change

1. Test module loading
2. Verify selectors still work
3. Test selector updates

## Regression Testing Checklist

Before any change that affects existing features:

- [ ] HOME tab navigation works
- [ ] DKCH1 and DKCH2 work
- [ ] TRACKING search works
- [ ] PRINT all modes work
- [ ] ABOUT update check works
- [ ] Auth flow (online and offline) works
- [ ] Token capture works
- [ ] BASE has no background jobs
- [ ] ULTRA FullStackOperation works
