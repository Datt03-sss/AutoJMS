# WebView2 DevTools Inspector

## Purpose

`WebViewDevToolsInspector` gives AutoJMS a DevTools-like capture path for WebView2 automation work. Use it before changing DKCH/TRACKING/PRINT selectors or JMS API assumptions.

It captures:

- DOM snapshot: URL, title, path/hash, body sample, visible inputs/buttons/selects, nearby labels, Element UI classes.
- Network capture: WebView2 requests/responses through Chrome DevTools Protocol.
- Selector candidates: ranked CSS/XPath candidates with confidence and reason.
- Route state: `Unknown`, `NotLoggedIn`, `WrongPage`, `Loading`, `Ready`, `Error`.
- Local/session storage keys only, not values.
- Console warnings/errors.

Sensitive values are redacted by `TokenRedactor` before export.

## How To Enable

Inspector is dev-only and disabled by default.

In Debug build, enable it with either option:

```powershell
$env:AUTOJMS_ENABLE_WEB_DEBUG_INSPECTOR = "1"
dotnet run --project src/AutoJMS/AutoJMS.csproj -c Debug
```

Or create this local flag file:

```text
{AppPaths.UserDataDir}\enable-web-debug-inspector.flag
```

Release builds do not attach the inspector.

## How To Export

1. Enable inspector.
2. Open AutoJMS Debug build.
3. Navigate the relevant WebView2 tab to the target JMS page.
4. Trigger the workflow manually so network calls are captured.
5. Press `Ctrl+Shift+F12`.

The active tab determines the captured surface:

- HOME -> HOME WebView2
- DKCH -> DKCH WebView2
- PRINT -> PRINT preview WebView2

Output folder:

```text
{AppPaths.UserDataDir}\debug\webview-captures\yyyyMMdd-HHmmss-{SURFACE}\
```

Typical installed path:

```text
C:\AutoJMS\AppData\debug\webview-captures\yyyyMMdd-HHmmss-DKCH\
```

## Bundle Files

- `dom-snapshot.json`: DOM controls and route metadata.
- `network-capture.json`: request/response metadata and safe text bodies.
- `selector-candidates.json`: ranked selector candidates.
- `route-state.json`: route guard state and DOM signals.
- `local-storage-keys.json`: local/session storage key names only.
- `console-errors.json`: warning/error console messages.
- `README.md`: bundle notes.

## Agent/Coder Workflow

1. Read `route-state.json` first.
   - If `NotLoggedIn`, do not change selectors.
   - If `WrongPage`, fix route/navigation guard first.
   - If `Loading`, improve wait conditions.

2. Read `dom-snapshot.json`.
   - Find the exact visible input/button/select.
   - Confirm nearby label, placeholder, id/name, and Element UI class.

3. Read `selector-candidates.json`.
   - Prefer stable id/name/placeholder/aria/label-scoped candidates.
   - Avoid global selectors like `input`, `.el-input__inner`, or `button.el-button--primary` unless scoped.
   - XPath fallback is acceptable for investigation, but CSS/runtime-config selector is preferred for production automation.

4. Read `network-capture.json`.
   - Identify endpoint URL, method, payload shape, response status, and response body.
   - Check if failure is API/auth/route related before changing DOM selectors.

5. Patch automation only after evidence is clear.
   - Use runtime selector config when available.
   - If a temporary hardcoded selector is unavoidable, add a TODO to move it into runtime config.

## Security Rules

- Do not commit capture bundles.
- Do not paste raw production token/cookie/payload into prompts.
- `TokenRedactor` masks `authToken`, `Authorization`, `Cookie`, `Set-Cookie`, JWTs, and 32-char JMS tokens.
- Storage export contains key names only, never values.
- Bundles are written under `AppData\debug`, not under Velopack `current`.

## Implementation Files

- `src/AutoJMS/Automation/DevTools/WebViewDevToolsInspector.cs`
- `src/AutoJMS/Automation/DevTools/NetworkCaptureService.cs`
- `src/AutoJMS/Automation/DevTools/DomSnapshotService.cs`
- `src/AutoJMS/Automation/DevTools/SelectorDiscoveryService.cs`
- `src/AutoJMS/Automation/DevTools/WebRouteDetector.cs`
- `src/AutoJMS/Automation/DevTools/DevToolsCaptureModels.cs`
- `src/AutoJMS/Diagnostics/WebDebugExportService.cs`
- `src/AutoJMS/Diagnostics/TokenRedactor.cs`

## Limitations

- Response bodies are captured only for safe textual content types.
- Body capture depends on CDP availability and may fail for cached/opaque/cross-origin responses.
- Selector candidates are evidence, not automatic patches.
- DKCH route markers are currently code-level markers and should move to runtime config when adopted by automation guards.
