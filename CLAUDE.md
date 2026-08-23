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

### Skills First Rule

Before starting work on any task:

1. Check `.agent/skills/` (curated project skills) and `.agents/skills/` (CLI-installed skills) for a local skill matching the task domain (WinForms, Excel export, Firebase license, Velopack release, Inno Setup, SunnyUI grid, DataHub manifest, WebView2, desktop-commander, superpowers, etc.) and follow it.
2. For any DataHub work (API endpoints, enrollment, migrations, manifest publish) follow [.agent/rules/05-datahub-firebase-github-rules.md](./.agent/rules/05-datahub-firebase-github-rules.md) and [.agent/skills/datahub-manifest-skill.md](./.agent/skills/datahub-manifest-skill.md); for Postgres SQL tuning follow [.agent/skills/postgres-best-practices/SKILL.md](./.agent/skills/postgres-best-practices/SKILL.md).
3. If no local skill matches, use the `find-skills` skill (`.agent/skills/SKILL.md`) to discover and install a suitable skill (`npx skills find [query]`) before falling back to general knowledge.
3. Skills are helpers — project rules in this file and `AGENTS.md` always take precedence over any skill guidance.

---

## Agent Tooling — desktop-commander & superpowers

Full rules: [.agent/rules/08-agent-tooling-rules.md](./.agent/rules/08-agent-tooling-rules.md).
Skills: [.agent/skills/desktop-commander-skill.md](./.agent/skills/desktop-commander-skill.md),
[.agent/skills/superpowers-skill.md](./.agent/skills/superpowers-skill.md).

| Tool | Kind | Who has it | Use it for |
|---|---|---|---|
| `desktop-commander` | MCP server, repo-scoped in `.mcp.json` | any client that loads `.mcp.json` | terminal + long-running processes, files **outside** the repo (runtime logs, `AppData\modules`, WebView2 captures), `list_processes`/`kill_process` for build file locks, streaming search, `write_pdf` |
| `superpowers` | Claude Code **plugin** (`.claude/settings.json`) | Claude Code CLI only | `brainstorm` before non-trivial work, `write-plan`/`execute-plan`, systematic debugging, TDD on pure-logic classes |

Use them proactively when they fit — but note:

- **Built-in `Read`/`Edit`/`Write`/`Grep` stay the default for repo files.** Reach for
  `desktop-commander` only when the built-ins genuinely cannot do the job.
- **No tool exempts you from this file.** Minimal Edit Rule, Protected Files, Secret Policy, the
  single-writer lock in `.agent-lock.md`, and "never push without a passing Release build" apply
  identically to `execute_command`, `edit_block` and any superpowers-generated plan.
- **Never** use `set_config_value` to widen desktop-commander's `allowedDirectories` /
  `blockedCommands` without an explicit owner request; never `git add .`; never delete files.
- `superpowers` TDD applies to pure-logic classes only (`DkchJourneyAnalyzer`, `Tab2Config`, the
  response parsers, `TierDefinitions`) — **not** to WinForms Designer code or WebView2 automation,
  which are verified by the Owner Manual Test Checklist instead.

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
- DataHub production config (VPS connection strings, device/admin token)
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
