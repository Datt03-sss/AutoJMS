# Agent Tooling Rules — desktop-commander & superpowers

Applies to every agent: Claude Code, Cowork, Antigravity, ChatGPT.

## Precedence — read this first

```
AGENTS.md  >  CLAUDE.md  >  .agent/rules/*  >  any MCP tool or plugin skill
```

A tool being *able* to do something is never permission to do it. `desktop-commander` can run any
shell command and `superpowers` will happily propose a large refactor — both are still bound by the
Minimal Edit Rule, the Protected Files list, the Secret Policy and the build-before-push gate.

---

## 1. What is actually installed

| Name | Kind | Available to |
|---|---|---|
| `desktop-commander` | MCP server (27 tools: files, terminal, processes, search) | Any client that loads `.mcp.json` or has it in user scope |
| `superpowers` | Claude Code **plugin** (skills framework, not an MCP server) | Claude Code CLI only — enabled in `.claude/settings.json` |

`superpowers` is a plugin, so Cowork / Antigravity / ChatGPT sessions do **not** get it. Do not write
instructions that assume every agent has it. `desktop-commander` is repo-scoped in `.mcp.json`; if an
agent does not see the tools, the client needs a restart — do not silently fall back to guessing.

---

## 2. desktop-commander — when to use it, when NOT to

### Default stays the built-in tools

For anything inside the repo, prefer the client's own `Read` / `Write` / `Edit` / `Grep` / `Glob`.
They are the tools the harness, the diff review and the single-writer lock were designed around.
Reaching for `desktop-commander` on a plain file edit adds risk and buys nothing.

### Use desktop-commander when the built-ins genuinely cannot do it

| Need | Tool |
|---|---|
| Long-running / interactive process (`dotnet watch`, a REPL, an installer) | `start_process` → `interact_with_process` → `read_process_output` → `force_terminate` |
| Read files **outside** the repo — runtime logs, Velopack install root, WebView2 capture bundles | `read_file`, `read_multiple_files`, `list_directory` |
| A stuck `AutoJMS.exe` or MSBuild holding a file lock so the build fails | `list_processes` → `kill_process` |
| Search a huge or binary tree where `Grep` times out | `start_search` → `get_more_search_results` → `stop_search` |
| Surgical in-place patch when the client has no `Edit` tool | `edit_block` |
| Export a report as PDF | `write_pdf` |

Useful non-repo paths on the owner's machine:

- Runtime logs: `%LocalAppData%\AutoJMS\AppData\logs\debug.log`
- User settings / modules: `%LocalAppData%\AutoJMS\AppData\AutoJMS.json`, `…\AppData\modules\`
- WebView2 debug bundles: `%LocalAppData%\AutoJMS\AppData\debug\webview-captures\…`

### Hard prohibitions

1. **Never bypass the build/verify/push gate.** `execute_command` must not run `git push` unless
   `dotnet build -c Release` passed in the same session. No `--force`, no `reset --hard` after push,
   no history rewrite. See CLAUDE.md § Permissions.
2. **Never widen the sandbox.** Do not call `set_config_value` on `allowedDirectories`,
   `blockedCommands` or `telemetry` without an explicit owner request for that specific change.
   Log the current values with `get_config` if you need to explain a failure.
3. **Never read or echo secrets.** `.env*`, `*.pfx`, `*.pem`, `*.sec`, `service_account.json`,
   `AutoJMS.secure`, `AutoJMS.config.enc`, `license.dat`. If a token must be referenced, mask it as
   `first4...last4`.
4. **Never delete.** No `rm`, `del`, `Remove-Item`, no emptying trash, no `move_file` that clobbers a
   tracked file. This repo's rule is "do not delete old files".
5. **Never edit Protected Files** via `edit_block`/`write_file` — the list in CLAUDE.md applies
   identically regardless of which tool does the writing.
6. **Never `git add .`** — the working tree is routinely dirty with other agents' work. Add explicit
   paths only.
7. **Respect the lock.** Read `.agent-lock.md` before the first write of any kind. If
   `Current Writer` is another agent, you are read-only — `desktop-commander` does not exempt you.
8. **Do not commit capture bundles** produced by `start_process` or the WebView2 inspector.

### Reporting

Any `execute_command` / `start_process` invocation that changes state (build, git, installer, file
move) must appear in the final report's *Files Changed* or *Build/Verify Result* section with the
exact command used.

---

## 3. superpowers — when to use it

Claude Code CLI only. Commands: `/superpowers:brainstorm`, `write-plan`, `execute-plan`.

### Use it for

- **Brainstorm before non-trivial work.** New feature, a rule change with business impact (e.g. the
  DKCH block conditions), or anything touching more than ~3 files. Refining the spec through
  questions before writing code is exactly right for this codebase, where business rules come from
  the owner and not from the code.
- **Systematic debugging.** Root-cause first, fix second. Matches
  `.agent/rules/07-do-not-break-existing-logic.md`.
- **TDD on pure-logic classes.** `src/AutoJMS/Automation/DkchJourneyAnalyzer.cs`,
  `Tab2Config.cs`, `Licensing/TierDefinitions.cs` and similar have no WinForms/WebView2 dependency
  and are covered by `tests/AutoJMS.Tests` (xUnit). Red-green-refactor works there.

### Where superpowers' defaults must yield

- **TDD does not apply to WinForms UI.** `Main.Designer.cs`, WebView2 automation and anything needing
  a real JMS session cannot be unit-tested here. Do not invent a test harness for them; verify by the
  Owner Manual Test Checklist instead.
- **"Refactor" is not free here.** CLAUDE.md's Minimal Edit Rule wins: apply the smallest change that
  fixes the bug or adds the feature. Do not accept a superpowers plan that reorganises a large file
  unless the owner asked for it.
- **Do not let a plan skip the gate.** `execute-plan` finishing is not permission to push; build and
  verify still have to pass first.
- **Selectors are evidence-based.** For any WebView2/JMS change, follow
  `.agent/skills/webview2-devtools-inspector-skill.md` — capture first, never guess a selector, no
  matter how confident a plan sounds.

---

## 4. Skills-first still holds

The order in AGENTS.md § Skills First Rule is unchanged. Project skills in `.agent/skills/` describe
*this* codebase and beat generic methodology. Relevant here:

- `.agent/skills/desktop-commander-skill.md`
- `.agent/skills/superpowers-skill.md`
- `.agent/skills/webview2-devtools-inspector-skill.md`
- `.agent/skills/csharp-winforms-skill.md`
