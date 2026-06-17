# AutoJMS Agent Instructions (GitHub Shared-Main Workflow Mode)

**For AI coding agents working on this project (Antigravity and Claude Code).**

---

## Workspace Model: GitHub Shared-Main
To support cooperative development across multiple AI platforms (Antigravity, Claude Code, and ChatGPT):
- **Source of Truth**: The GitHub repository `origin/main` is the shared source of truth.
- **Commit Authorization**: AI agents are authorized to commit directly to the local `main` branch, but **only** after build/verify checks pass successfully.
- **Push Authorization**: AI agents are authorized to push commits to `origin/main` (via `ai-commit.ps1`) after successful verification.

---

## Developer Rules

1. **Sync Before Coding**: Before making any edits, always synchronize with the latest remote state:
   ```powershell
   git switch main
   git pull --ff-only origin main
   git status
   ```
2. **Verify & Push**: Never commit or push manually. Always use the automated commit helper:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\ai-commit.ps1 -Message "your commit message"
   ```
   This script runs local compilation checks, stages all modifications, commits them, and pushes them to `origin/main` automatically.
3. **No Force Pushes**: Do not overwrite Git history. If an edit is buggy, add a fixing commit or use the rollback script. Never run `git reset --hard` on commits that have already been pushed.
4. **Task Scope Compliance**: Only edit files within the scope defined in `.agent-lock.md`.

---

## Protected Areas (Frozen Files)
The following files are critical to licensing, updates, and releases, and must **not** be modified:
- `src/AutoJMS/Program.cs`
- `src/AutoJMS/Forms/Main.cs` / `src/AutoJMS/Forms/Main.Designer.cs`
- `src/AutoJMS/Licensing/` (Handshakes, tiers, policies)
- `src/AutoJMS/Updates/VelopackUpdateService.cs`
- `release/` and `installer/` (Release engines and Inno Setup definitions)
- WinForms Designer files (`.Designer.cs`, `.resx`)

---

## Required Workflow Sequence
1. **Sync**: Run `git switch main`, `git pull --ff-only origin main`, and `git status`.
2. **Lock Workspace**: Set lock state in `.agent-lock.md` (`Current Writer: Antigravity` or `Claude Code`, `Mode: SHARED_MAIN_WRITE`).
3. **Implement**: Make code edits within the allowed scope.
4. **Compile & Push**: Run `ai-commit.ps1 -Message "..."` to compile, commit, and push.
5. **Unlock**: Reset lock state back to `None` in `.agent-lock.md` and request Owner testing.
6. **Task Report**: Output the completion report details.

---

## Agent Report Format (Mandatory)
After every push, you must output a report containing:

## Summary
Key achievements of the task.

## Files changed
List of modified files.

## Build/verify result
Output summary from the verification script.

## Commit message
The message used for the commit.

## Commit hash
The full hash of the pushed commit.

## Pushed to
Target remote URL and branch (e.g. `origin/main`).

## Behavior changed
List of behavioral updates made.

## Behavior intentionally unchanged
List of functionalities preserved.

## Owner manual test checklist
Checklist of tabs and controls the owner must smoke test.

## Risks
Any identified design or security risks.
