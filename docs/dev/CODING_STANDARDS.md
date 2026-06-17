# AutoJMS Coding Standards

These standards apply when a future task explicitly allows production code edits.

## C# / .NET

- Use async/await for I/O and long-running work.
- Do not use `.Result` or `.Wait()` on UI paths.
- Keep patches small and focused.
- Do not catch exceptions silently.
- Do not hardcode new URLs, tokens, keys, or tier names.
- Do not add secrets to repo.

## WinForms

- Update WinForms controls on the UI thread.
- Do not block the UI thread.
- Do not edit Designer-generated files manually unless the task specifically requires a UI Designer fix.
- Do not rebind large grids repeatedly in tight loops.
- Use stable grid dimensions and avoid unnecessary full refreshes.

## WebView2

- Access WebView2 only on the UI thread.
- Use `Invoke`, `BeginInvoke`, or `UiThread` helpers where needed.
- Prefer DOM wait conditions over fixed delays.
- Use the Vue/Element UI native input setter pattern for form automation.

## Security / Logging

- Do not log full production tokens.
- Mask JMS authToken and license JWT in logs.
- Do not confuse JMS authToken with license JWT.
- Do not print private keys, service account JSON, or service-role keys.

## Tier Policy

- Do not let BASE run background inventory sync.
- Do not let BASE run background database tracking.
- Do not let BASE auto-start FullStackOperation.
- Use `TierRuntimePolicy` instead of ad hoc tier string checks.

