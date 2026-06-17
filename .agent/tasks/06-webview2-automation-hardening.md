# WebView2 Automation Hardening

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Make WebView2 automation safer and less brittle without changing business flow.

## Do not modify

- Selector behavior without verifying DOM flow
- WebView2 from background thread
- DKCH1/DKCH2 business distinction
- Token capture contract

## Allowed files

- `WebViewAutomation.cs`
- `Main.cs` only for WebView2 UI-thread orchestration if needed
- Runtime selector config docs

## Steps

1. Identify failing selector or script.
2. Verify DOM in WebView2/DevTools.
3. Marshal WebView2 calls to UI thread.
4. Prefer DOM wait over fixed delay.
5. Use Vue/Element UI native input setter pattern.
6. Keep fallback selectors where possible.
7. Test DKCH1/DKCH2.

## Acceptance criteria

- `ExecuteScriptAsync` runs on UI thread.
- Automation waits for DOM readiness.
- DKCH1 and DKCH2 still work.
- Token capture still works.
- No unrelated selector rewrite.

## Rollback notes

Restore previous selector/script if new selector fails in production; document exact JMS page version if known.

