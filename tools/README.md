# Tools Workspace

This folder is for build/release/installer/maintenance scripts.

Examples:
- maintenance scripts
- Reactor helper docs

No production app source code belongs here.

Current status:

- Structure migration has been executed.
- Velopack release scripts live under `release/`.
- Inno Setup scripts live under `installer/inno/`.
- Maintenance scripts live under `tools/maintenance/`.
- .NET Reactor project files live under `tools/reactor/`.
- Render CLI (pinned, repo-local) lives under `tools/render-cli/` — see its README before touching
  the `autojms-api` license server service. The binary itself is git-ignored; only the pinned
  installer script is committed.


