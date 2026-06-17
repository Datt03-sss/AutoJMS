# AutoJMS Agent Instructions

**For all AI agents working on this project: Antigravity, Claude Code, ChatGPT.**

- **Repo**: https://github.com/Datt03-sss/AutoJMS
- **Working branch**: `main`
- **Source of truth**: `origin/main` — shared across all agents and the Owner.

---

## Workspace Model: Single-Writer, Multiple-Readers

- **Multiple Readers**: All agents can concurrently read, analyze, and audit the repository.
- **Single Writer**: Only one agent is allowed to write or edit files at any given time.
- Write concurrency is regulated by: [.agent-lock.md](./.agent-lock.md)

---

## Workspace Lock Rules

1. **Read Lock Before Edit**: Before making any edits, read `.agent-lock.md`.
2. **Check Current Writer**: Do **NOT** edit any files if `Current Writer` is set to another agent.
3. **Acquire Lock**: If `Current Writer` is `None`, set it to your agent name and set `Mode: WRITE_ACTIVE`.
4. **Task Scoping**: Only edit files specified in the active `Scope` property in `.agent-lock.md`.
5. **Release Lock**: After verification and push succeed, reset `Current Writer: None`, `Mode: READ_ONLY`.

---

## Required Workflow

### Source of Truth
`origin/main` on https://github.com/Datt03-sss/AutoJMS is the shared reference for all agents and the Owner.

### Before Every Task
```powershell
git switch main
git pull --ff-only origin main
git status
```

Never start editing on a stale or dirty working tree.

### After Every Edit — Build
```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```

### After Build — Harness (if available)
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

### Commit & Push — only if build/verify pass
```powershell
git status
git add .
git commit -m "<clear commit message>"
git push origin main
git log --oneline -1
git status
```

**Never push if build fails.**

---

## Permissions

| Action | Allowed |
|---|---|
| Edit files on `main` directly | ✅ |
| `git commit` on local `main` after build pass | ✅ |
| `git push origin main` after build/verify pass | ✅ |
| Fix errors from previous commit with a new commit | ✅ |
| Force push (`--force`, `--force-with-lease`) | ❌ |
| Rewrite history (`rebase -i`, `reset --hard` after push) | ❌ |
| Build/upload production release | ❌ unless owner requests |
| Bump version number | ❌ unless owner requests |
| Push if build fails | ❌ |

---

## Absolute Prohibitions

Agents **must never** perform these actions under any circumstances:

- **No force push**: History rewrite is forbidden.
- **No release**: Do not build, sign, or upload a production release artifact.
- **No license/auth/hash-check changes**: Do not touch `Licensing/`, `JmsAuthTokenService.cs`, or any hash-verification paths unless task explicitly requires it.
- **No Firebase session changes**: Do not alter Firebase initialization, session tokens, or service account references unless task explicitly requires it.
- **No Supabase production config changes**: Do not alter Supabase connection strings or production keys unless task explicitly requires it.
- **No Velopack production changes**: Do not touch `VelopackUpdateService.cs` or `release/build-release.ps1` unless task explicitly requires it.
- **No database schema changes**: Do not alter DB schema unless task explicitly requires it.
- **No WinForms Designer moves**: Do not relocate or rename `.Designer.cs` or `.resx` files.
- **No secrets in commits**: Never commit `.env`, service account keys, `*.pfx`, `*.pem`, `*.key`, or any token/credential file.

---

## Core Code Rules

### 1. Minimal Edit Rule
- Apply the minimal change required to fix a bug or add a feature.
- Do not refactor large files unless explicitly requested.
- Maintain existing coding style, variable names, and formatting.

### 2. Tab Boundary Rule
- Each tab in `Main.cs` is isolated. Changes to one tab must not leak into other tabs.
- Core tabs: `HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`.
- `ABOUT` tab must always remain the last tab in the UI collection.

### 3. Tier Separation Rule
- Use `TierRuntimePolicy` property flags for tier checks (e.g. `_tierPolicy.EnableBackgroundAutoSync`).
- Never hardcode string checks like `if (CurrentTier == "ULTRA")`.

### 4. Secret Policy
- Never log full tokens. Mask as `first4...last4`.
- Never commit secrets. If a secret file is staged accidentally, unstage and add to `.gitignore` before committing.

---

## Protected Files (Frozen)

Never edit without explicit owner request for that specific task:

- `src/AutoJMS/Program.cs`
- `src/AutoJMS/Forms/Main.cs` / `src/AutoJMS/Forms/Main.Designer.cs`
- `src/AutoJMS/Licensing/TierRuntimePolicy.cs`
- `src/AutoJMS/Licensing/LicenseApiService.cs`
- `src/AutoJMS/Licensing/JmsAuthTokenService.cs`
- `src/AutoJMS/Updates/VelopackUpdateService.cs`
- `release/build-release.ps1`
- `installer/inno/AutoJMS.iss`
- All WinForms Designer files (`.Designer.cs`, `.resx`)

---

## Required Final Report Format

After every task, agents must output:

1. **Summary** — what was done
2. **Files Changed** — paths of modified/created files
3. **Build/Verify Result** — pass or fail with relevant output
4. **Commit Message** — exact message used
5. **Commit Hash** — from `git log --oneline -1`
6. **Pushed To** — `origin/main`
7. **Behavior Changed** — what the app now does differently
8. **Behavior Intentionally Unchanged** — what was explicitly left alone
9. **Owner Manual Test Checklist** — tabs and UI controls to smoke test
10. **Risks** — potential build, security, or stability issues
