# Changelog

This directory holds a **vendored third-party Postgres skill** (MIT). Upstream:
`supabase/agent-skills` — attribution only; AutoJMS does not run that backend.

The upstream release history that used to sit here was rewritten by an automated find-and-replace
into links under `github.com/datahub/agent-skills` and `datahub.com/docs`. Neither exists: the
first is a repo that was never created, the second is an unrelated company's site. Fifteen dead
links that read as authoritative sources are worse than no changelog, so they are gone.

Vendored at version `1.1.1`. Local edits to this skill are tracked in git — use
`git log -- .agent/skills/postgres-best-practices/` for the real history.

Local changes so far:

- Removed the fabricated `author: datahub` / `organization: DataHub` attribution and the four
  fabricated reference URLs from `SKILL.md`; replaced them with upstream attribution and
  postgresql.org links.
- Added a scope note: the RLS / `SECURITY DEFINER` / `auth.role()` chapters describe a managed
  BaaS. AutoJMS runs plain PostgreSQL in Docker behind its own ASP.NET Core API — no RLS, no
  policies, no client-callable RPC. Read them for SQL mechanics only.
- `references/security-rls-basics.md` and `references/security-rls-performance.md` carry the same
  note.
