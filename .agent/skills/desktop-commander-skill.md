# Desktop Commander Skill (AutoJMS)

## Overview

`desktop-commander` is an MCP server giving terminal control, filesystem access outside the repo,
process management and streaming search. Use it for the jobs the client's built-in file tools cannot
do. Rules of engagement live in `.agent/rules/08-agent-tooling-rules.md` and always win.

Package: `@wonderwhy-er/desktop-commander@latest` (repo-scoped in `.mcp.json`).

## Tool map

| Group | Tools |
|---|---|
| Config | `get_config`, `set_config_value` |
| Read/write | `read_file`, `read_multiple_files`, `write_file`, `create_directory`, `list_directory`, `move_file`, `get_file_info`, `write_pdf` |
| Edit | `edit_block` |
| Terminal | `start_process`, `interact_with_process`, `read_process_output`, `read_output`, `force_terminate`, `list_sessions`, `execute_command` |
| Process | `list_processes`, `kill_process` |
| Search | `start_search`, `get_more_search_results`, `list_searches`, `stop_search` |
| Telemetry | `get_usage_stats`, `get_recent_tool_calls` |

## Decision rule

```
Is the target inside the repo, and is it a plain read/edit/grep?
  YES → use the client's own Read / Edit / Write / Grep. Stop.
  NO  → is it one of: outside-repo path, long-running process, process kill,
        huge/binary search, PDF export?
          YES → desktop-commander
          NO  → you probably do not need it
```

## AutoJMS-specific recipes

### Build and verify (Windows, PowerShell)

```
execute_command: dotnet restore .\AutoJMS.slnx
execute_command: dotnet build .\AutoJMS.slnx -c Release
execute_command: dotnet test .\tests\AutoJMS.Tests\AutoJMS.Tests.csproj
execute_command: powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

Long builds: prefer `start_process` + `read_process_output` so the call does not time out.

### "File is locked" build failure

A previous debug run of the app usually still holds the DLL.

```
list_processes            → find AutoJMS / MSBuild / VBCSCompiler
kill_process <pid>        → then re-run the build
```

### Read runtime state the repo does not contain

Paths are under the Velopack install root, deliberately outside `current\`:

```
%LocalAppData%\AutoJMS\AppData\logs\debug.log             ← AppLogger output
%LocalAppData%\AutoJMS\AppData\AutoJMS.json               ← user settings
%LocalAppData%\AutoJMS\AppData\modules\                   ← tab2config.json, selectors.json, manifests
%LocalAppData%\AutoJMS\AppData\debug\webview-captures\    ← WebView2 inspector bundles
```

Reading `debug.log` is the fastest way to confirm a DKCH decision path: look for the
`[DKCH]`, `[DKCH Guard]` and `[tabDKCH_result] case=…` lines.

### Search where Grep struggles

`start_search` streams, so use it for `bin/`, `obj/`, capture bundles or the whole drive:

```
start_search  pattern="scanTypeName"  path=<capture dir>  type=content
get_more_search_results ... / stop_search
```

### Editing with `edit_block`

Same discipline as the built-in `Edit`: one surgical change, unique search text, keep existing style,
never touch a Protected File. Use `expected_replacements` when a pattern legitimately repeats — never
to blanket-replace across a file.

## Prohibitions (short form — full list in the rules file)

- No `git push` without a passing Release build; no `--force`, no history rewrite.
- No `set_config_value` on `allowedDirectories` / `blockedCommands` without an explicit owner request.
- No reading or echoing `.env*`, `*.pfx`, `*.pem`, `*.sec`, `service_account.json`,
  `AutoJMS.secure`, `AutoJMS.config.enc`, `license.dat`. Mask tokens as `first4...last4`.
- No deletes, no `git add .`.
- Read `.agent-lock.md` before the first write; another agent holding the lock means read-only.
- Never commit capture bundles or anything under `AppData\`.

## Report

Every state-changing command goes into the final report with its exact text, under
*Build/Verify Result* or *Files Changed*.
