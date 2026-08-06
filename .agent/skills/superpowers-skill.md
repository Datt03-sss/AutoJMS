# Superpowers Skill (AutoJMS)

## Overview

`superpowers` (obra/superpowers, official marketplace) is a **Claude Code plugin** — a skills
framework for brainstorming, planning, TDD and systematic debugging. It is enabled for this repo in
`.claude/settings.json`.

It is **not** an MCP server. Cowork, Antigravity and ChatGPT sessions do not have it. Never write a
plan that depends on another agent having these commands.

Commands: `/superpowers:brainstorm`, `/superpowers:write-plan`, `/superpowers:execute-plan`.

## When to reach for it

| Situation | Use |
|---|---|
| New feature, or a business-rule change (DKCH conditions, licensing tiers, release flow) | `brainstorm` first — the spec lives in the owner's head, not the code |
| Work spanning more than ~3 files | `brainstorm` → `write-plan` → `execute-plan` |
| A bug whose cause is not yet understood | the debugging skill: root cause before any fix |
| Pure-logic class needs new behaviour | the TDD skill (see scope below) |
| One-line fix, typo, obvious null guard | none of the above — just do it |

## TDD scope in this codebase

Red-green-refactor works only where a class has no WinForms/WebView2/JMS dependency. Test project:
`tests/AutoJMS.Tests` (xUnit, net8.0-windows).

**In scope** — has or can have tests:

- `src/AutoJMS/Automation/DkchJourneyAnalyzer.cs`
- `src/AutoJMS/Automation/Tab2Config.cs`
- `WebViewAutomation`'s pure response parsers (`ReadJsonFlag`, `IsSaveSuccessResponse`,
  `IsFailureResponse`, `ExtractMessage`, `ExtractCode`, `ExtractRatio`, `LooksLikeActionEnvelope`)
- `Licensing/TierDefinitions.cs`, `Config/*` DTO loaders

**Out of scope** — do not invent a harness for these:

- `Forms/Main.Designer.cs` and any WinForms layout/theming
- WebView2 DOM automation (needs a live JMS session)
- Velopack update flow, Inno Setup, Firebase/Supabase production paths

For out-of-scope changes the verification artefact is the **Owner Manual Test Checklist** in the final
report, not a unit test.

Note: the test project has no `InternalsVisibleTo`, so a member must be `public` to be tested. Making
a pure helper public for testability is acceptable; do not add `InternalsVisibleTo` to the assembly —
it changes the .NET Reactor obfuscation posture for every internal in the project.

## Where superpowers' defaults must yield to repo rules

1. **Minimal Edit Rule beats refactoring.** CLAUDE.md: apply the smallest change that does the job;
   do not reorganise large files unless asked. Reject that part of a generated plan.
2. **Protected Files stay protected.** A plan naming `Program.cs`, `Main.cs`, `TierRuntimePolicy.cs`,
   `LicenseApiService.cs`, `JmsAuthTokenService.cs`, `VelopackUpdateService.cs`,
   `release/build-release.ps1` or `installer/inno/AutoJMS.iss` needs an explicit owner request for
   that specific file first.
3. **`execute-plan` finishing is not permission to push.** `dotnet build -c Release` and
   `eng/harness/verify.ps1` still gate the commit.
4. **Never guess a selector.** Any WebView2/JMS step must follow
   `.agent/skills/webview2-devtools-inspector-skill.md` — capture a debug bundle
   (`Ctrl+Shift+F12`) and read `route-state.json` / `selector-candidates.json` / `network-capture.json`
   before changing selectors or payload assumptions.
5. **Tab Boundary Rule.** Changes to one tab must not leak into another; `ABOUT` stays last.
6. **Single-writer lock.** `write-plan` may run read-only, but `execute-plan` writes — read
   `.agent-lock.md` and acquire the lock first.

## Practical flow for this repo

```
1. git switch main && git pull --ff-only origin main && git status
2. Read AGENTS.md, .agent/rules/, .agent/context/
3. /superpowers:brainstorm      ← settle the business rule with the owner
4. /superpowers:write-plan      ← scope it to the smallest file set
5. Acquire .agent-lock.md
6. /superpowers:execute-plan    ← TDD for pure-logic parts only
7. dotnet build -c Release + dotnet test + verify.ps1
8. Explicit-path git add, commit, push origin main
9. Release the lock, write the 10-section final report
```
