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

## Cross-Agent Collaboration

Claude Code nhận công việc từ hai nguồn:
1. **Owner trực tiếp**: Task được mô tả trong chat.
2. **Antigravity Prompt Proposal** (qua Owner): Owner copy-paste một prompt đã duyệt từ Antigravity.

Khi nhận Antigravity Prompt Proposal:
- Đọc kỹ toàn bộ prompt, bao gồm context, yêu cầu kỹ thuật, và verification steps.
- Tuân thủ mọi rule trong `AGENTS.md` và `CLAUDE.md` — prompt của Antigravity **KHÔNG** được override rules.
- Nếu prompt yêu cầu sửa Protected Files mà Owner chưa cho phép rõ ràng: **DỪNG LẠI và hỏi Owner**.
- Sau khi hoàn thành: output Final Report theo format chuẩn.

### Skills First Rule

Before starting work on any task:

1. Check `.agent/skills/` (curated project skills) và `.agents/skills/` (CLI-installed skills) for a local skill matching the task domain (WinForms, Excel export, Firebase license, Velopack release, Inno Setup, DataGridView grid, DataHub manifest, WebView2, desktop-commander, superpowers, etc.) and follow it.
2. For any DataHub work (API endpoints, enrollment, migrations, manifest publish) follow [.agent/rules/05-datahub-firebase-github-rules.md](./.agent/rules/05-datahub-firebase-github-rules.md) and [.agent/skills/datahub-manifest-skill.md](./.agent/skills/datahub-manifest-skill.md); for Postgres SQL tuning follow [.agent/skills/postgres-best-practices/SKILL.md](./.agent/skills/postgres-best-practices/SKILL.md).
3. To build or publish a release (owner request only) follow [.agent/skills/autojms-release-build/SKILL.md](./.agent/skills/autojms-release-build/SKILL.md) — exact commands and the traps that break `release/build-release.ps1`.
4. Next, check the plugin skills from `superpowers`, `agent-skills` and `ponytail` — [.agent/rules/10-plugin-stack-rules.md](./.agent/rules/10-plugin-stack-rules.md) says which one owns which job.
5. If no local skill matches, use the `find-skills` skill (`.agent/skills/SKILL.md`) to discover and install a suitable skill (`npx skills find [query]`) before falling back to general knowledge.
6. Skills are helpers — project rules in this file and `AGENTS.md` always take precedence over any skill guidance.

---

## Agent Tooling — desktop-commander, superpowers, ponytail, agent-skills

Full rules: [.agent/rules/08-agent-tooling-rules.md](./.agent/rules/08-agent-tooling-rules.md)
và [.agent/rules/10-plugin-stack-rules.md](./.agent/rules/10-plugin-stack-rules.md).
Skills: [.agent/skills/desktop-commander-skill.md](./.agent/skills/desktop-commander-skill.md),
[.agent/skills/superpowers-skill.md](./.agent/skills/superpowers-skill.md).

| Tool | Kind | Who has it | Use it for |
|---|---|---|---|
| `desktop-commander` | MCP server, repo-scoped in `.mcp.json` | any client that loads `.mcp.json` | terminal + long-running processes, files **outside** the repo (runtime logs, `AppData\modules`, WebView2 captures), `list_processes`/`kill_process` for build file locks, streaming search, `write_pdf` |
| `superpowers` | Claude Code **plugin** (`.claude/settings.json`) | Claude Code CLI only | `brainstorm` before non-trivial work, `write-plan`/`execute-plan`, systematic debugging, TDD on pure-logic classes |
| `ponytail` | Claude Code **plugin** (`.claude/settings.json`) | Claude Code CLI only | **bật sẵn mode `full` mỗi phiên** — ép YAGNI/stdlib-first, chính là Minimal Edit Rule dạng tự động. `/ponytail-review` trước khi commit |
| `agent-skills` | Claude Code **plugin** (`.claude/settings.json`) | Claude Code CLI only | checklist chuyên đề: `security-auditor` cho licensing/tier/DataHub, `api-and-interface-design`, `shipping-and-launch`. Không dùng cho việc superpowers đã lo |

Use them proactively when they fit — but note:

- **Built-in `Read`/`Edit`/`Write`/`Grep` stay the default for repo files.** Reach for
  `desktop-commander` only when the built-ins genuinely cannot do the job.
- **No tool exempts you from this file.** Minimal Edit Rule, Protected Files, Secret Policy, the
  single-writer lock in `.agent-lock.md`, and "never push without a passing Release build" apply
  identically to `execute_command`, `edit_block` and any superpowers-generated plan.
- **Khoá phải mang định danh phiên.** `Current Writer` ghi `<tên agent> (<định danh phiên>)`, ví dụ
  `Claude Code (cleanup-tooling-rules)`. Thấy một định danh không phải của mình thì repo đang bị khoá
  — **chờ**, đừng cho là khoá cũ. Xem `AGENTS.md` § Workspace Lock Rules.
- **Never** use `set_config_value` to widen desktop-commander's `allowedDirectories` /
  `blockedCommands` without an explicit owner request; never `git add .`; never delete files.
- `superpowers` TDD applies to pure-logic classes only (`DkchJourneyAnalyzer`, `Tab2Config`, the
  response parsers, `TierDefinitions`) — **not** to WinForms Designer code or WebView2 automation,
  which are verified by the Owner Manual Test Checklist instead.
- **Khi `superpowers` và `agent-skills` cùng áp được**: superpowers lo quy trình (brainstorm, plan,
  debug, TDD), agent-skills lo checklist chuyên đề. `/ship` **không** phải lệnh phát hành — release
  vẫn là [.agent/skills/autojms-release-build/SKILL.md](./.agent/skills/autojms-release-build/SKILL.md)
  và chỉ khi Owner yêu cầu.
- `/ponytail-audit`, `/ponytail-debt`, `/ponytail-gain` là **báo cáo read-only**. Chúng sẽ chỉ ra
  hàng loạt chỗ "thừa" trong Protected Files — xuất cho Owner, không tự dọn.
- `.codegraph/` là công cụ đồ thị code duy nhất của repo. `graphify` và OmniRoute đã cân nhắc và bị
  loại — xem [ADR-0002](./docs/decisions/ADR-0002-rejected-tools-omniroute-and-graphify.md).
- **Thêm plugin hoặc marketplace mới vào `.claude/settings.json` phải có yêu cầu rõ của Owner** —
  file đó được commit nên mỗi dòng thêm là code bên thứ ba chạy trên máy mọi người mở repo.

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
