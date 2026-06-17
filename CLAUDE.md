# AutoJMS Claude Code Instructions (GitHub Shared-Main Mode)

This document directs Claude Code CLI sessions when executing tasks in this GitHub Shared-Main development workspace.

---

## Shared-Main Rules
1. **Sync Before Starting**: Always pull the latest state from GitHub before starting any task:
   ```bash
   git switch main
   git pull --ff-only origin main
   git status
   ```
2. **Build and Verify First**: Never commit code manually. Always use the helper script to verify compilation and secrets scan before saving and pushing commits:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\git\ai-commit.ps1 -Message "feat(module): description"
   ```
3. **Pushed to GitHub**: Successful commits are automatically pushed to `origin/main`.
4. **No History Rewrites**: Do **NOT** force-push (`--force`) or rewrite git history.

---

## Harness & Commands

Ensure all changes compile and pass security checks using these scripts:

- **Harness Status Check**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\git\status-main.ps1
  ```
- **Commit and Push**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\git\ai-commit.ps1 -Message "your commit message"
  ```
- **Revert Bad Commit**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\git\revert-last-commit.ps1
  ```

---

## Code Modification Rules

### 1. Minimal Edit Rule
- Do not refactor large files unless explicitly requested.
- Apply the minimal change required to fix a bug or add a feature.
- Maintain existing coding styles, variable names, and formatting.

### 2. Tab Boundary Rule
- Each tab in `Main.cs` is isolated. Changes to one tab must not leak or affect the logic of other tabs.
- Core Tabs: `HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`.
- The `ABOUT` tab must always remain the last tab in the UI collection.

### 3. Protected Files & Areas
Never edit these files without explicit owner permission:
- `src/AutoJMS/Program.cs`
- `src/AutoJMS/Forms/Main.cs` / `src/AutoJMS/Forms/Main.Designer.cs`
- `src/AutoJMS/Licensing/TierRuntimePolicy.cs`
- `src/AutoJMS/Licensing/LicenseApiService.cs`
- `src/AutoJMS/Licensing/JmsAuthTokenService.cs`
- `src/AutoJMS/Updates/VelopackUpdateService.cs`

---

## Final Task Report Format (Mandatory)
At the end of your task, output a report containing:
1. **Summary**: What was done.
2. **Files Modified**: Path to modified files.
3. **Verify Result**: Output of the compile checks.
4. **Commit Message**: Message for commit.
5. **Commit Hash**: Pushed commit hash.
6. **Pushed to**: Destination repo and branch.
7. **Behavior Changed**: Description of behavioral edits.
8. **Behavior Intentionally Unchanged**: Standard features preserved.
9. **Owner Manual Test Checklist**: Tabs and UI controls to smoke test.
10. **Risks**: Potential build or stability issues.
