# AutoJMS Implementation Plan

## Step 1 — Build blockers

Goal:
- `dotnet build` passes.

Do:
- Fix missing `modules/*.json`.

Do not:
- Change runtime logic.

## Step 2 — AuthToken 401

Goal:
- WebView2-valid session does not cause false 401/login prompts.

Do:
- Fix response classifier.
- Refresh WebView2 token on UI thread.
- Retry once.

## Step 3 — BASE background policy

Goal:
- BASE does not auto fetch/inventory/database sync.

Do:
- Centralize policy.
- Guard timers/jobs.

## Step 4 — FullStack lifecycle

Goal:
- FullStack UI creates controls before data update.

Do:
- UI ready flag.
- Null/disposed grid guard.
- Cancel background tasks on close.

## Step 5 — Release pipeline

Goal:
- Major update uses GitHub Releases binary + Supabase manifest.

Do:
- Validate manifest.
- Validate About tab update flow.

## Step 6 — Tests

Goal:
- Add minimal tests for policy/token/manifest.

## Step 7 — Feature development

Start operation center improvements only after stabilization.

