# AutoJMS Claude Code Instructions (GitHub origin/main Mode)

This document directs Claude Code CLI sessions working on AutoJMS.

- **Repo**: https://github.com/Datt03-sss/AutoJMS
- **Working branch**: `main`
- **Source of truth**: `origin/main` — shared with Antigravity, Claude Code, and ChatGPT.

---

## Workflow: Before Every Task

```powershell
git switch main
git pull --ff-only origin main
git status
```

Never start editing on a dirty or stale working tree.

---

## Workflow: After Every Edit

### 1. Build
```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```

### 2. Harness (if available)
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

### 3. Commit & Push — only if build/verify pass
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
| `git commit` on local `main` | ✅ |
| `git push origin main` after build pass | ✅ |
| Fix errors from the previous commit with a new commit | ✅ |
| Force push | ❌ |
| Rewrite history (`rebase -i`, `reset --hard` after push) | ❌ |
| Build/upload production release | ❌ unless owner requests |
| Bump version number | ❌ unless owner requests |

---

## Protected Files & Areas

Never edit these without explicit owner request for that specific task:

- `src/AutoJMS/Program.cs`
- `src/AutoJMS/Forms/Main.cs` / `src/AutoJMS/Forms/Main.Designer.cs`
- `src/AutoJMS/Licensing/TierRuntimePolicy.cs`
- `src/AutoJMS/Licensing/LicenseApiService.cs`
- `src/AutoJMS/Licensing/JmsAuthTokenService.cs` (Firebase session logic)
- `src/AutoJMS/Updates/VelopackUpdateService.cs` (Velopack production flow)
- Supabase production config (connection strings, anon keys)
- Database schema migrations
- `release/build-release.ps1` / `installer/inno/AutoJMS.iss`

---

## Code Modification Rules

### 1. Minimal Edit Rule
- Apply the minimal change required to fix a bug or add a feature.
- Do not refactor large files unless explicitly requested.
- Maintain existing coding style, variable names, and formatting.

### 2. Tab Boundary Rule
- Each tab in `Main.cs` is isolated. Changes to one tab must not leak into other tabs.
- Core tabs: `HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`.
- `ABOUT` tab must always remain the last tab in the UI collection.

### 3. Secret Policy
- Never commit `.env`, service account keys, `*.pfx`, `*.pem`, or any token/key file.
- Mask tokens in logs as `first4...last4` format.

---

## Required Final Report Format

After every task, output:

1. **Summary** — what was done
2. **Files Changed** — paths
3. **Build/Verify Result** — pass or fail output
4. **Commit Message** — exact message used
5. **Commit Hash** — from `git log --oneline -1`
6. **Pushed To** — `origin/main`
7. **Behavior Changed** — what the app now does differently
8. **Behavior Intentionally Unchanged** — what was explicitly left alone
9. **Owner Manual Test Checklist** — tabs/controls to smoke test
10. **Risks** — potential build or stability issues
