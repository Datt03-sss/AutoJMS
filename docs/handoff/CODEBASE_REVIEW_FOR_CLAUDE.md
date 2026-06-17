# AutoJMS Codebase Review For Claude

Generated: 2026-06-09
Branch: `debug/print-slow-submit`
Workspace: `D:\v1.2605.2(new-test)`
Purpose: provide enough context for Claude Chat to reason about AutoJMS architecture, risks, flows, and current print slowdown work without rediscovering the project.

## Required Reading Order

1. `AGENTS.md`
2. `.agent/README.md`
3. `.agent/context/project-overview.md`
4. `.agent/context/current-architecture.md`
5. `.agent/context/tier-policy.md`
6. `.agent/rules/07-do-not-break-existing-logic.md`
7. `docs/audit/CODEBASE_AUDIT.md`
8. `docs/handoff/CLAUDE_CHAT_HANDOFF.md`
9. `docs/debug/print-slowdown-investigation.md`

## Non-Negotiable Rules

- AutoJMS is `.NET 8 WinForms + SunnyUI + WebView2`.
- BASE tabs: HOME, DKCH, TRACKING, PRINT, ABOUT.
- ULTRA adds `FullStackOperation` as a separate form, not a tab.
- ABOUT must stay last.
- Firebase/Render license server controls license/session/tier.
- Supabase is for manifest/config/hash/tier/selector-update and legacy data paths.
- GitHub Releases host Velopack binary assets.
- Inno Setup is first install/reinstall wrapper.
- Velopack is in-app update.
- Do not let BASE start FullStack/background inventory sync.
- Do not access WebView2 off the UI thread.
- Do not log production tokens or keys in full.
- Do not modify HOME/DKCH/TRACKING/PRINT/ABOUT behavior unless explicitly requested.

## Current Source Map

Core app:

- `src/AutoJMS/Program.cs`: startup, Velopack init, license verify/offline fallback, service initialization.
- `src/AutoJMS/Forms/Main.cs`: main WinForms surface, WebView2, tabs, Tracking/Print/About, DASH entry to FullStack.
- `src/AutoJMS/Forms/Main.Designer.cs`: WinForms designer controls.
- `src/AutoJMS/Config/AppPaths.cs`: runtime writable path resolution.
- `src/AutoJMS/Config/SettingsManager.cs`: local `AppData\AutoJMS.json`.
- `src/AutoJMS/Config/AppConfig.cs`: secure runtime config.

License/auth/tier:

- `src/AutoJMS/Licensing/LicenseApiService.cs`
- `src/AutoJMS/Licensing/JmsAuthTokenService.cs`
- `src/AutoJMS/Licensing/JmsAuthStateService.cs`
- `src/AutoJMS/Licensing/TierRuntimePolicy.cs`
- `src/AutoJMS/Services/SiteContextProvider.cs`

JMS API:

- `src/AutoJMS/Services/JmsApiClient.cs`
- `src/AutoJMS/Tracking/WaybillTrackingService.cs`

Print:

- `src/AutoJMS/Printing/IPrintService.cs`
- `src/AutoJMS/Printing/PrintService.cs`
- `src/AutoJMS/Printing/PrintSafetyGuard.cs`
- `src/AutoJMS/Printing/PrintStatusSnapshot.cs`
- `src/AutoJMS/Printing/PrintApprovalInfo.cs`
- `src/AutoJMS/Printing/PrintReadinessContext.cs`
- `src/AutoJMS/Printing/TabPrintReprintPolicy.cs`

FullStack:

- `src/AutoJMS/Forms/FullStackOperation*.cs`
- `src/AutoJMS/FullStack/**`
- `src/AutoJMS/Data/LocalFullStack/**`
- `src/AutoJMS/Services/FullStack/**`

Release/install/update:

- `release/build-release.ps1`
- `installer/inno/AutoJMS.iss`
- `src/AutoJMS/Updates/VelopackUpdateService.cs`
- `src/AutoJMS/Updates/UpdateXmlManifestService.cs`
- `backend/render-license-server/server.js`

## Runtime Paths

Runtime data should live under the selected install root, especially:

```text
{InstallRoot}\AppData\
  AutoJMS.json
  service_account.json
  logs\
  secure\
  Downloads\
  BrowserData\
  FullStack\
```

Current desired installer default path from user:

```text
C:\AutoJMS
```

Installer policy:

- Inno Setup is a wrapper.
- Velopack is the actual installed/updatable app.
- Inno must not create a second Apps & Features entry.
- Inno should call Velopack setup with `--installto "{app}"`.
- User-facing first installer is `AutoJMS-Installer-<version>.exe`, not `AutoJMS-win-Setup.exe`.

## Tracking Flow

Tracking API uses:

```text
POST https://jmsgw.jtexpress.vn/operatingplatform/podTracking/inner/query/keywordList
```

Payload:

```json
{
  "keywordList": ["{waybillNo}"],
  "trackingTypeEnum": "WAYBILL",
  "countryId": "1"
}
```

Important fields:

- `data[].keyword`
- `data[].details[]`
- `details[].waybillNo`
- `details[].billCode`
- `details[].scanTime`
- `details[].uploadTime`
- `details[].scanTypeName`
- `details[].scanNetworkCode`
- `details[].waybillTrackingContent`

Do not self-translate or reconstruct tracking descriptions. Display backend/JMS fields as returned.

## Print Safety Flow

Current print safety rule:

1. Normalize input waybill.
2. If input has suffix such as `861822407381-001`, tracking uses base `861822407381`.
3. Load `MiddleCode` from license/session/local config through `SiteContextProvider`.
4. Fetch fresh tracking.
5. Check all `details[].scanNetworkCode` sorted/considered across the full journey.
6. If any `scanNetworkCode == MiddleCode`, allow.
7. Only if scanNetworkCode does not match, fallback to `MaDoan2 == MiddleCode`.
8. Do not allow by substring in content/raw JSON/cache.
9. Fail closed on missing config, tracking failure, empty journey, mismatch.

Current accepted reason codes include:

- `SCAN_NETWORK_CODE_MATCHED`
- `MADOAN2_MATCHED`
- `MIDDLE_CODE_MISSING_CONFIG`
- `TRACKING_FAILED`
- `JOURNEY_EMPTY`
- `MADOAN2_MISSING_IN_TRACKING`
- `MADOAN2_MISMATCH`
- `WAYBILL_MISMATCH`

Audit file:

- `AppData/logs/print-safety-audit.log` in this handoff package is sanitized.
- Actual runtime audit may live under app runtime `AppData\logs`.

## Print Status / Approval Flow

Print status API:

```text
POST https://jmsgw.jtexpress.vn/operatingplatform/rebackTransferExpress/pringListPage
```

Important: field names are intentionally `pringFlag` and `pringType`, not `printFlag` / `printType`.

Used fields from `data.records[]`:

- `statusName` -> approval status
- `senderNetworkCode` -> segment after print
- `printCount` -> JMS print count
- `applyStaffName` -> print/apply staff

This API is display-only. It must not bypass `PrintSafetyGuard`.

## Current Print Performance Architecture

Search:

- Calls tracking keywordList and pringListPage.
- Runs `PrintSafetyGuard`.
- Stores `PrintReadinessContext` with TTL 60 seconds.

Print:

- If readiness is valid, print preflight skips tracking API.
- If readiness is missing/stale, preflight only calls keywordList and guard.
- It does not call pringListPage before print.
- Post-print refresh runs in background.

Reprint:

- JMS `printCount` is display/source signal.
- AutoJMS has local reprint policy:
  - initial print does not count as reprint
  - same waybill reprints through AutoJMS max 5
  - over 5 should be blocked and user must print manually in JMS
- Store file: `AppData\logs\tab-print-reprints.tsv`.

## Current Print Slowdown Diagnosis

See `docs/debug/print-slowdown-investigation.md`.

Summary:

- `PrintSafetyGuard` is not the main delay in current logs.
- `PrintDocument.Print()` is usually 200-700 ms.
- Slow total time is explained by `DownloadPdf` waiting for the remote PDF returned by `printWaybill`.
- Example:
  - `GetPdfUrl` = 212 ms
  - `DownloadPdf` = 42183 ms
  - `PrintSubmit` = 255 ms
  - `PrintTotal` = 42755 ms
- This means the server accepted/created a PDF URL quickly, but the file may not be downloadable immediately.

Safety concern:

- If AutoJMS times out too quickly after `printWaybill` returned a URL, the server may already count one print.
- Retrying `printWaybill` can increment print count again.
- Safer direction: call `printWaybill` once, then wait longer for the returned PDF URL, without duplicate print requests.

## FullStackOperation Summary

FullStackOperation is separate from BASE tabs. It is code-first UI. Recent direction:

- Local-first SQLite for operational waybill/inventory workflow.
- Supabase remains for manifest/config/update only.
- Inventory grid single-click previews detail.
- Double-click opens journey workspace.
- Journey fetch uses tracking API JSON and `details.db`.
- Do not break tab `Thời hiệu`.
- Do not make MainForm depend on FullStack.

Important FullStack local paths:

- `AppData\FullStack\autojms_fullstack.db`
- `AppData\FullStack\details.db`

## Update / Release Summary

Desired split:

- `update.xml`: metadata for About UI, channel/link/version/release notes.
- Velopack update source: GitHub Releases through `GithubSource`.

GitHub repo:

```text
Datt03-sss/AutoJMS-Update
```

Raw manifest:

```text
https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml
```

Release assets:

- `RELEASES`
- `AutoJMS-<VelopackVersion>-full.nupkg`
- `AutoJMS-win-Setup.exe`
- `AutoJMS-Installer-<VelopackVersion>.exe`

Versioning:

- Velopack version must be SemVer 3-part or prerelease, e.g. `1.26.6`, `1.26.6-beta.1`.
- Display version may be 4-part, e.g. `1.26.6.1`.

## Known Risks

- Do not log full JMS `authToken`.
- Do not log license keys/JWTs.
- WebView2 access must remain on UI thread.
- Print safety is fail-closed and must stay fail-closed.
- `printWaybill` can have server-side side effects before local PDF download/printing completes.
- Persistent cache must not allow print safety decisions.
- BASE must not start FullStack background jobs.
- Installer must not produce duplicate Apps & Features entries.

## Suggested Next Work

Round 1, diagnose:

- Keep code behavior stable.
- Read `docs/debug/print-slowdown-investigation.md`.
- Inspect whether slow PDF download is network/server readiness versus local antivirus/file IO/spooler.
- Add instrumentation only if needed.

Round 2, patch:

- Keep guard.
- Keep readiness fast path.
- Keep post refresh background.
- Tune PDF download wait and user status.
- Avoid duplicate `printWaybill` calls for the same click.

Round 3, verify:

- Build Debug.
- User runs real print repro.
- Read new `PrintPerf` logs.
- Confirm `printCount` behavior and no duplicate server-side print requests.

