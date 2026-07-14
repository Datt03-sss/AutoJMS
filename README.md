# AutoJMS

Desktop logistics automation app (WinForms, .NET 8) for Vietnamese logistics operations.

- **Repo**: https://github.com/Datt03-sss/AutoJMS
- **Solution**: `AutoJMS.slnx`
- **AI agent instructions**: [`CLAUDE.md`](CLAUDE.md) (Claude Code) · [`AGENTS.md`](AGENTS.md) (all agents)

## Repo layout

```
AutoJMS/
├── CLAUDE.md            ← Rules for Claude Code sessions (read first)
├── AGENTS.md            ← Rules for all AI agents
├── AutoJMS.slnx         ← Solution file
├── src/                 ← Application source code
│   ├── AutoJMS/         ← Main WinForms app (tabs: HOME, DKCH, TRACKING, PRINT, ABOUT)
│   └── AutoJMS.Abstractions/
├── tests/               ← Test projects (AutoJMS.Tests)
├── backend/             ← License server (Node/Render) + Supabase migrations
├── docs/                ← All documentation → start at docs/START_HERE.md
│   ├── project/         ← Project charter, status, original request
│   ├── architecture/    ← System & client architecture
│   ├── dev/             ← Workflow, coding standards, codebase index
│   ├── roadmap/         ← Plans & backlog
│   └── ...              ← api/, release/, manual/, migration/, troubleshooting/, audit/
├── eng/                 ← Engineering scripts
│   ├── git/             ← Safe git helpers (checkpoint, sync-main, worktree...)
│   ├── harness/         ← verify.ps1 build/verify harness
│   └── hybrid-sync/     ← Supabase hybrid-sync migration scripts
├── tasks/               ← Task tracking (active / completed / plans)
├── .agent/              ← Agent operating hub: protocols, skills, context, templates
├── release/             ← Release build scripts (build-release.ps1)
├── installer/           ← Inno Setup installer (installer/inno)
├── tools/               ← Maintenance & reactor tools
├── external/            ← Git submodules (AutoJMS-API)
└── archive/             ← Historical material (old agent sessions, legacy modules)
```

## Quick start for AI sessions (vibe-coding)

1. Read [`CLAUDE.md`](CLAUDE.md) — workflow, protected files, permissions.
2. Read [`docs/START_HERE.md`](docs/START_HERE.md) and [`docs/VIBE_CODING_GUIDE.md`](docs/VIBE_CODING_GUIDE.md).
3. Before any task: `git pull --ff-only origin main`, verify clean tree.
4. Build check: `dotnet build .\AutoJMS.slnx -c Release` — never push on a failed build.

## Build

```powershell
dotnet restore .\AutoJMS.slnx
dotnet build .\AutoJMS.slnx -c Release
```

Build outputs (`bin/`, `obj/`, `artifacts/`, `release/output/`) are gitignored — do not commit them.
