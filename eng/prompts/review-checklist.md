# PR Review Checklist (Owner Copy)

Use this checklist when reviewing and merging PRs submitted by AI agents (Antigravity or Claude Code).

---

## 1. Automated Quality Gate Checks
- [ ] **Harness Passes**: Verify that the PR description contains the output of `verify.ps1` and that all gates passed (exit code 0).
- [ ] **Release Compile**: Run `powershell -ExecutionPolicy Bypass -File .\eng\harness\build.ps1` to ensure it compiles in Release mode.
- [ ] **No Secret Leaks**: Run `powershell -ExecutionPolicy Bypass -File .\eng\harness\check-secrets.ps1` to confirm no environment variables or service account keys have leaked.
- [ ] **Folder Structure**: Run `powershell -ExecutionPolicy Bypass -File .\eng\harness\check-project-structure.ps1` to verify directories.

---

## 2. Manual Integrity Checks
- [ ] **Zero Frozen File Changes**: Ensure no modifications were made to `Program.cs`, `Main.cs`, `JmsAuthTokenService.cs`, or `VelopackUpdateService.cs`.
- [ ] **Correct Branch Name**: Ensure the branch follows the `agent/<name>/<description>` naming scheme.
- [ ] **Tier Separation**:
  - [ ] BASE tier is not affected.
  - [ ] No background auto-sync, auto inventory fetches, or database tracking timers are enabled on the BASE tier.
  - [ ] All tier checks utilize `TierRuntimePolicy` flags.
- [ ] **WebView2 Thread Safety**: Ensure all WebView2 interactions are marshaled to the UI thread.
- [ ] **Token Logging**: Verify that any captured tokens logged to debug outputs are masked to `first4...last4`.
- [ ] **No Direct Merges**: Confirm that the PR was not self-merged by the agent.

---

## 3. Post-Merge Test Plan (Smoke Tests)
- [ ] Start the application on the BASE tier. Confirm the HOME tab loads `jms.jtexpress.vn` and no background tracking commences.
- [ ] Start the application on the ULTRA tier. Confirm the dashboard starts background auto-sync and tracking engines.
- [ ] Click the **ABOUT** tab and verify the version matches. Trigger a manual update check to ensure Velopack resolves the GitHub Release feed correctly.
