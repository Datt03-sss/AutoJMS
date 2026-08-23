# AutoJMS Workspace Skills

This workspace has local agent skills in two places:

- `.agent/skills/` — curated project skills (WinForms, Excel export, Firebase license, Velopack, Inno Setup, SunnyUI grid, DataHub manifest, WebView2...)
- `skills-lock.json` — records skills pulled in with the Skills CLI (`npx skills add`). The files land under `.agent/skills/`; there is no `.agents/` directory in this repo.

## Skills From Outside The Project

- `find-skills` from `vercel-labs/skills` → `.agent/skills/SKILL.md` (in `skills-lock.json`)
- `design-taste-frontend` from `Leonxlnx/taste-skill` → `.agent/skills/design-taste-frontend.md` (in `skills-lock.json`)
- `postgres-best-practices` (third-party, MIT) → `.agent/skills/postgres-best-practices/` — **use when writing/optimizing Postgres SQL** (indexing, connections, JSONB, FTS). Vendored and locally edited, so it is no longer CLI-managed and carries no lock entry. Generic Postgres only; its RLS / `SECURITY DEFINER` / `auth.role()` chapters describe a managed BaaS that AutoJMS does not run.

## DataHub Work

There is no vendor skill for the backend. DataHub is our own ASP.NET Core API on a VPS — see
[.agent/rules/05-datahub-firebase-github-rules.md](rules/05-datahub-firebase-github-rules.md) and
[.agent/skills/datahub-manifest-skill.md](skills/datahub-manifest-skill.md).

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
