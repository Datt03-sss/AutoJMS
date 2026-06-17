# Local Shared-Project Workflow Guide

This document describes the workflow for multiple AI agents (Antigravity, Claude Code, ChatGPT) operating on the same AutoJMS repository.

- **Repo**: https://github.com/Datt03-sss/AutoJMS
- **Working branch**: `main`
- **Source of truth**: `origin/main`

---

## 1. Concurrency Rule: Single Writer, Multiple Readers

- **Multiple Readers**: All agents can simultaneously analyze, read, and audit the repository.
- **Single Writer**: Only one agent is allowed to write files at any given time.
- Write access is governed by [.agent-lock.md](../../.agent-lock.md) at the repository root.

---

## 2. Lock Management (.agent-lock.md)

### Acquire the Write Lock
1. Read the current `.agent-lock.md`.
2. If `Current Writer` is `None`, update:
   - `Current Writer`: your agent name
   - `Mode`: `WRITE_ACTIVE`
   - `Scope`: short description of files you will edit

### Release the Write Lock
After push succeeds:
1. Reset:
   - `Current Writer`: `None`
   - `Mode`: `READ_ONLY`
   - `Scope`: `None`

---

## 3. Workflow Steps

### Step 1: Sync from origin
```powershell
git switch main
git pull --ff-only origin main
git status
```

### Step 2: Acquire the Lock
Update `.agent-lock.md` to declare yourself as the current writer.

### Step 3: Implement
Edit only files within the declared `Scope`.

### Step 4: Build & Verify
```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

### Step 5: Commit & Push (only if build/verify pass)
```powershell
git status
git add .
git commit -m "<clear commit message>"
git push origin main
git log --oneline -1
git status
```

### Step 6: Release the Lock
Reset `.agent-lock.md` to `READ_ONLY`. Inform the Owner that the task is ready for testing.

---

## 4. Prohibited Actions (All Agents)

| Prohibited | Why |
|---|---|
| Force push | Protects shared history on origin/main |
| Merge branches | Owner controls all merges |
| Build/upload production release | Release pipeline is owner-only |
| Edit `Licensing/`, `JmsAuthTokenService.cs` | License/auth/hash-check are security boundaries |
| Edit Firebase session logic | Session token rotation is owner-controlled |
| Edit Supabase production config | Production keys must not be overwritten |
| Edit `VelopackUpdateService.cs` or `release/build-release.ps1` | Velopack production flow is frozen |
| Move/rename WinForms Designer files | Designer file paths are hard-coded in `.csproj` |
| Commit `.env`, service account keys, or `*.pfx` | Secrets must never enter version control |
| Push if build fails | Broken builds must not reach origin/main |

---

## 5. Scope Discipline
Before acquiring the write lock, set the `Scope` field in `.agent-lock.md` to a concise description of the specific files or area you will edit. Only files within that scope may be modified during the lock period.

---

## 6. ChatGPT Review Cycle
After each push to `origin/main`, ChatGPT reads the latest GitHub state to review, guide, or suggest next steps. This is a read-only audit role — ChatGPT does not write directly to the repo.
