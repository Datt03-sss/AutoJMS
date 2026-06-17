# Claude Code: Worker Rules & Execution Guide

This document defines strict operational rules and workflows for **Claude Code** when executing implementation tasks on the AutoJMS repository.

---

## 1. Task Spec Alignment

Before writing any code, Claude Code must read the active task specification:
**Active Spec Location**: `tasks/active/claude-task.md`

- **Align Scope**: Only edit files specified under the `Scope` property of the task and `.agent-lock.md`.
- **Constraint**: Do not refactor files, add features, or change business logic outside the boundaries of the spec.

---

## 2. Workspace Lock Protocol

1. **Verify Lock**: Check `.agent-lock.md`. If `Current Writer` is another agent, stop immediately.
2. **Acquire Lock**: If `Current Writer` is `None`, write:
   - `Current Writer: ClaudeCode`
   - `Mode: WRITE_ACTIVE`
   - `Scope: <targeted file paths>`
3. **Release Lock**: Upon successful build, harness verify, commit, and push, restore `.agent-lock.md` to:
   - `Current Writer: None`
   - `Mode: READ_ONLY`
   - `Scope: None`

---

## 3. Mandatory Build and Verification Pipeline

After making code edits, you must execute the following pipeline in order. **Do not skip any steps.**

### Step 1: Restore and Build
```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```
*If compile fails, resolve the errors before proceeding. Never commit broken code.*

### Step 2: Verification Harness
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```
*All checks in the verification summary (Build, Tests, Secrets, Structure) must pass with `✅`.*

---

## 4. Git Commit and Push Rules

If build and harness verify succeeded, stage and push the changes:

```powershell
git status
git add .
git commit -m "<type>(scope): <descriptive commit message>"
git push origin main
git log --oneline -1
```

### Absolute Restrictions:
- **No Force Push**: Never run `git push --force` or `--force-with-lease`.
- **No Stale Trees**: Ensure you ran `git pull --ff-only origin main` before writing code to prevent merge conflicts.
- **Never Push on Fail**: If the build or harness fails, fix it. Pushing compile-broken code is unacceptable.

---

## 5. Frozen / Protected Files

Never modify these files unless the active task spec explicitly requests changes in their paths:
- `src/AutoJMS/Program.cs`
- `src/AutoJMS/Forms/Main.cs` / `src/AutoJMS/Forms/Main.Designer.cs`
- `src/AutoJMS/Licensing/` (all files)
- `src/AutoJMS/Updates/` (all files)
- `release/build-release.ps1`
- `installer/inno/AutoJMS.iss`
- All `.Designer.cs` and `.resx` files (unless UI changes are required).
- `Licensing/` or `JmsAuthTokenService.cs` (security critical).
