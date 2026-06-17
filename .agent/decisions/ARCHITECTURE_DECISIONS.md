# Architecture Decisions

Current architecture decisions and constraints for AutoJMS.

## Keep production code in current layout for now

- Status: Active
- Decision: Do not move production code into `src/` in Phase 2.
- Reason: WinForms Designer, Inno Setup, Velopack, .NET Reactor and project paths depend on current layout.
- Risk: Root folder remains crowded.
- Follow-up: Revisit only after build/release pipeline is stable.

## Keep FullStackOperation as standalone ULTRA form

- Status: Active
- Decision: `FullStackOperation` remains a separate visible form, not a tab.
- Reason: Current tier policy and launch flow depend on standalone lifecycle.
- Risk: Lifecycle must handle close/reopen/cancel carefully.
- Follow-up: Use `.agent/tasks/05-fullstack-operation-lifecycle.md`.

## Split binary hosting from control plane

- Status: Active
- Decision: GitHub Releases host large Velopack binary assets; Supabase hosts small manifests/config/hash files.
- Reason: Supabase free storage limits make large binary hosting unreliable.
- Risk: Two-system release flow must stay synchronized.
- Follow-up: Use `docs/manual/MANUAL_OPERATIONS.md` and release report template.

## BASE tier remains manual-only

- Status: Active
- Decision: BASE can use HOME/DKCH/TRACKING/PRINT/ABOUT manual flows but must not run background inventory/database sync or FullStack realtime.
- Reason: Tier separation and product stability.
- Risk: New background tasks can accidentally bypass `TierRuntimePolicy`.
- Follow-up: Add minimal tests for `TierRuntimePolicy`.

