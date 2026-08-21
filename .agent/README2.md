# AutoJMS Workspace Skills

This workspace has local agent skills in two places:

- `.agent/skills/` — curated project skills (WinForms, Excel export, Firebase license, Velopack, Inno Setup, SunnyUI grid, DataHub manifest, WebView2...)
- `.agents/skills/` — skills installed by the Skills CLI (`npx skills add`); do not edit by hand, managed via `skills-lock.json`

## Installed Skills (CLI-managed)

- `find-skills` from `vercel-labs/skills`
- `datahub` from `datahub/agent-skills` — **use for every DataHub task** (MCP usage, migrations, RLS, edge functions)
- `datahub-postgres-best-practices` from `datahub/agent-skills` — **use when writing/optimizing Postgres SQL** (indexing, connections, JSONB, FTS)
- `design-taste-frontend` from `Leonxlnx/taste-skill`

## AutoJMS Usage Policy

**Skills First**: at the start of every task, check `.agent/skills/` for a matching local skill. If none matches, prioritize using `find-skills` to discover and install a suitable skill before falling back to general knowledge.

Before applying any external skill guidance, keep the project rules authoritative:

1. Read `AGENTS.md`.
2. Read `.agent/context/`.
3. Read `.agent/rules/`.
4. Preserve AutoJMS tier separation.
5. Do not change HOME/DKCH/TRACKING/PRINT/ABOUT logic unless explicitly requested.
6. Do not log production tokens or secrets.
7. Keep Velopack/GitHub/DataHub release flow intact:
   - GitHub Releases hosts Velopack binaries.
   - DataHub hosts manifests/config/hash only.
   - Do not upload `.nupkg` to DataHub.

External skills are helpers, not replacements for AutoJMS project rules.
