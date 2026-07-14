## 2026-06-22T03:41:57Z
You are the tabDash Layout Worker.
Your working directory is: d:\v1.2605.2(new-test)\.agents\teamwork_preview_worker_tabdash_layout_1
Your parent is: teamwork_preview_orchestrator (id: e2e16d0d-18ae-4bdf-845d-e27b4b1c48a0)

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Your objective is to fix the UI layout of the tabDash WebView2 integration by following these exact steps:

Step 1: Check and Acquire the Lock
- Read `.agent-lock.md`. Ensure that `Current Writer` is "None".
- Acquire the lock by writing:
```markdown
# Agent Lock
Current Writer: teamwork_preview_worker
Mode: WRITE_ACTIVE
Scope: Fix WebView2 UI architecture on tabDash
```
to `.agent-lock.md`.

Step 2: Sync main branch
- Pull latest changes: run `git switch main`, `git pull --ff-only origin main`, and `git status`.

Step 3: Modify Code/Assets
- In `src/AutoJMS/Web/index.html`, remove the fake HTML top bar (lines 24-36). Ensure the parent layout remains valid.
- In `src/AutoJMS/Forms/FullStackOperation.Layout.cs`, change `Text = "Điều phối Vận hành Bưu cục Realtime";` (around line 30) to `Text = "AutoJMS - Điều phối Vận hành Bưu cục Realtime";`.

Step 4: Build & Test Verify
- Restore: `dotnet restore .\AutoJMS.slnx`
- Build: `dotnet build .\AutoJMS.slnx -c Release`
- Verify using harness script: `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1`

Step 5: Git Commit & Push
- Check status: `git status`
- Add and commit: `git commit -am "Fix tabDash WebView2 UI integration: remove fake top bar and style native title bar"`
- Push to origin: `git push origin main`

Step 6: Release Lock
- Reset lock in `.agent-lock.md`:
```markdown
# Agent Lock
Current Writer: None
Mode: READ_ONLY
Scope: None
```

Deliver a handoff report at `d:\v1.2605.2(new-test)\.agents\teamwork_preview_worker_tabdash_layout_1\handoff.md` and send a message back to the orchestrator (id: e2e16d0d-18ae-4bdf-845d-e27b4b1c48a0) when completed.
