# AutoJMS Workspace Skills

This workspace has local agent skills under `.agents/skills`.

## Installed Skills

- `find-skills` from `vercel-labs/skills`

## AutoJMS Usage Policy

Use `find-skills` only when a task would benefit from discovering or installing an additional agent skill.

Before applying any external skill guidance, keep the project rules authoritative:

1. Read `AGENTS.md`.
2. Read `.agent/context/`.
3. Read `.agent/rules/`.
4. Preserve AutoJMS tier separation.
5. Do not change HOME/DKCH/TRACKING/PRINT/ABOUT logic unless explicitly requested.
6. Do not log production tokens or secrets.
7. Keep Velopack/GitHub/Supabase release flow intact:
   - GitHub Releases hosts Velopack binaries.
   - Supabase hosts manifests/config/hash only.
   - Do not upload `.nupkg` to Supabase.

External skills are helpers, not replacements for AutoJMS project rules.
