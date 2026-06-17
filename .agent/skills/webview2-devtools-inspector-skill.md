# WebView2 DevTools Inspector Skill

Use this skill before changing AutoJMS WebView2 automation selectors, route guards, or JMS API assumptions.

## Rule

Do not guess selectors. Capture and inspect WebView2 evidence first.

## Required Inputs

Ask for or create a WebView2 debug bundle when possible:

```text
{AppPaths.UserDataDir}\debug\webview-captures\yyyyMMdd-HHmmss-{SURFACE}\
```

The bundle should contain:

- `route-state.json`
- `dom-snapshot.json`
- `selector-candidates.json`
- `network-capture.json`
- `local-storage-keys.json`
- `console-errors.json`

## Capture Steps

1. Run Debug build with inspector enabled:

   ```powershell
   $env:AUTOJMS_ENABLE_WEB_DEBUG_INSPECTOR = "1"
   dotnet run --project src/AutoJMS/AutoJMS.csproj -c Debug
   ```

2. Navigate the relevant tab to the target JMS page.
3. Trigger the real user flow once.
4. Press `Ctrl+Shift+F12` on the active tab.
5. Read the exported JSON files.

Alternative enablement:

```text
{AppPaths.UserDataDir}\enable-web-debug-inspector.flag
```

## Analysis Order

1. `route-state.json`
   - `NotLoggedIn`: do not patch selectors.
   - `WrongPage`: fix navigation/route guard.
   - `Loading`: fix DOM wait/readiness.
   - `Ready`: continue selector/API analysis.

2. `dom-snapshot.json`
   - Confirm visible input/button/select.
   - Record nearby label, placeholder, name/id, Element UI classes.

3. `selector-candidates.json`
   - Prefer stable id/name/placeholder/aria/label-scoped selector.
   - Avoid global selectors.
   - Treat XPath as fallback or debugging evidence.

4. `network-capture.json`
   - Confirm endpoint, method, payload, status, response body.
   - Use this before changing JMS API payload logic.

5. `console-errors.json`
   - Check SPA/Vue/runtime errors before changing automation code.

## Patch Rules

- Keep changes small.
- Do not rewrite DKCH/TRACKING/PRINT automation unless explicitly requested.
- Use runtime selector config if available.
- If a hardcoded selector is temporarily required, add `TODO(runtime-config)`.
- Do not log full tokens.
- Do not commit capture bundles.

## Security

`TokenRedactor` masks sensitive headers and token-like values, but bundles can still reveal business data. Treat every bundle as local-only diagnostic material.
