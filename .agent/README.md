# .agent/ - AI Agent & Developer Context

## Current Verified Baseline

This agent context is documentation-only. Agents may create or update Markdown in `.agent/` and `docs/`, but must not treat this folder as permission to modify production code.

Verified from the current repository:

- Product: AutoJMS, a .NET 8 WinForms desktop application using SunnyUI and WebView2.
- Main project: `src/AutoJMS/AutoJMS.csproj`, target `net8.0-windows`, runtime `win-x64`.
- Main UI: `Main.cs` with BASE tabs `HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`.
- ULTRA UI: `FullStackOperation.cs`, separate form, not a tab, launched through the `DASH` command after tier policy allows it.
- License backend: `backend/render-license-server/server.js` exposes `/api/verify-license`, `/api/heartbeat`, and `/api/logout`.
- Firebase: used by the Render server for license/session records through Firebase Admin SDK.
- Supabase Storage: manifests/config/hash/tier/selector-update control plane.
- GitHub Releases: large Velopack binary assets. Do not upload `.nupkg` to Supabase.
- Inno Setup: first install/reinstall/uninstall and runtime prerequisites.
- Velopack: in-app update flow, triggered manually from the About tab for major updates.

Current verification findings:

- `dotnet build src/AutoJMS/AutoJMS.csproj -c Debug` was fixed on 2026-06-03 by adding `Exists(...)` conditions to root `modules/*.json` content includes. Latest recorded result: build succeeded with warnings only.
- `service_account.json` exists in the workspace. Treat it as sensitive; rotate if it was ever exposed outside the trusted environment.
- Full JMS auth token logging exists in code and must not ship to production.
- WebView2 must be accessed on the UI thread.
- BASE must not run background inventory/database sync.

This folder contains context, rules, prompts, skills, workflows, and checklists for AI agents working on the AutoJMS project.

**DO NOT place production code in this folder.**

## Structure

```
.agent/
├── README.md              ← You are here
├── INDEX.md               ← Read-order index for agents
├── context/               ← Project understanding
├── rules/                 ← Mandatory rules for all agents
├── prompts/               ← Reusable prompts for specific tasks
├── tasks/                 ← Concrete task backlogs and acceptance criteria
├── task-board/            ← Backlog, now, done, blocked
├── templates/             ← Change/report/risk templates
├── decisions/             ← Decision logs and architecture decisions
├── handoff/               ← Current state and next-agent briefs
├── skills/               ← Specialized knowledge documents
├── workflows/            ← Step-by-step workflow guides
└── checklists/           ← Pre-action safety checklists
```

## Mandatory Task Start

Every coding task must start with:

1. Read `AGENTS.md`
2. Read `.agent/context`
3. Read `.agent/rules`
4. Read `docs/audit/CODEBASE_AUDIT.md`
5. State planned changes
6. Only then edit files

## Quick Start

Before making ANY code changes, an AI agent MUST:

1. Read `AGENTS.md`
2. Read `.agent/README.md` (this file)
3. Read `.agent/context/project-overview.md`
4. Read `.agent/context/current-architecture.md`
5. Read `docs/audit/CODEBASE_AUDIT.md`
6. Read relevant `.agent/rules/` files
7. Explain planned files to change before editing production code
8. Then proceed with the task

## Core Principles

- **DO NOT rewrite production code** without explicit user request
- **DO NOT modify namespaces, classes, or controls** without justification
- **DO NOT change existing logic** (HOME/DKCH/TRACKING/PRINT/ABOUT) unless requested
- **DO NOT delete files**
- **DO NOT move production code** without a migration plan
- **DO NOT hardcode additional URLs/tokens/tiers**
- **DO NOT log full tokens in production**
- **DO NOT block UI thread**
- **DO NOT access WebView2 outside UI thread**
- **DO NOT let BASE run background inventory/database sync**

## Bugfix Output Requirements

Every bugfix response or PR description must include:

- Root cause
- Patch summary using the smallest practical change
- Files touched
- Test or acceptance criteria
- Any `UNKNOWN / NEED VERIFY` items

## Tier Policy

- **BASE**: Core stable. HOME, DKCH, TRACKING, PRINT, ABOUT tabs only.
  - Manual tracking on demand only
  - No auto background sync
  - No FullStackOperation form
- **ULTRA**: BASE + FullStackOperation form + background realtime/inventory/database sync

## Update Policy

- **Inno Setup**: First install/reinstall/uninstall only
- **Velopack**: In-app updates
- **GitHub Releases**: Hosts large Velopack binaries (RELEASES/.nupkg/Setup.exe)
- **Supabase Storage**: Hosts small control-plane manifests (version-latest.json)
- **Major update**: Only via user click on tab About
- **No browser** is opened during update

## Known Sensitive Areas

1. WebView2 automation selectors (Vue/Element UI input setter)
2. authToken/JMS token 401 handling
3. Background jobs running on BASE tier
4. FullStackOperation lifecycle (pre-create, show, close)
5. DataGridView performance with large datasets
6. Update/hash integrity verification
7. local path vs AppData vs current separation

