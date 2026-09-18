# ADR 0002: Rejected Tooling — OmniRoute and Graphify

## Status
**Rejected** (both tools). Decided 2026-09-18.

---

## Context
The Owner asked for four tools to be installed at project scope and written into the shared agent
rules: [ponytail](https://github.com/DietrichGebert/ponytail),
[OmniRoute](https://github.com/diegosouzapw/OmniRoute),
[Graphify](https://github.com/Graphify-Labs/graphify) and
[agent-skills](https://github.com/addyosmani/agent-skills).

Only `ponytail` and `agent-skills` are Claude Code plugins — each ships a
`.claude-plugin/marketplace.json`, so `claude plugin install … --scope project` adds one line to
`.claude/settings.json` and every session that opens the repo picks it up. The other two are not
plugins, and each turned out to carry a cost the repo does not need to pay. Both were rejected;
this ADR records why, so the question is not re-opened every time someone reads the four-tool list.

---

## Decision

### OmniRoute — rejected
OmniRoute is an **AI gateway proxy**, not a Claude Code plugin. Using it means routing model traffic
— and therefore the prompt context, which on this repo includes `Licensing/`, `JmsAuthTokenService`
and DataHub connection code — through a third-party endpoint. AutoJMS gets nothing from it: the cost
routing and provider fallback it exists for are not problems this project has. It is not installed
and no rule refers to it.

### Graphify — rejected
Graphify is a **Python CLI** (`pip install graphifyy`) with a Claude Code *skill*, not a plugin.
Three reasons it loses to `.codegraph/`, which this repo already has:

1. **The hook breaks on Windows.** `graphify install --project` registers a `PreToolUse` hook on
   `Bash|Grep|Read|Glob` that shells out to a bare `graphify` binary. On the Owner's machine that
   binary lands in `…\Python\pythoncore-3.14-64\Scripts\`, which is not on `PATH` — the hook returned
   `command not found` on **every** tool call. Because `.claude/settings.json` is committed, that
   failure would reach every machine that opens the repo, including any that never ran `pip install`.
2. **It duplicates `.codegraph/`.** Both answer the same question — "where is this symbol and what
   calls it". The Owner's global rule already puts CodeGraph first. Two graph systems both
   intercepting `Read`/`Grep` is redundant, and their answers can disagree.
3. **It adds a Python dependency to a .NET + Node repo.** Nothing else here needs Python at build or
   run time, and the dependency is per-machine — it cannot be pinned by anything in git.

The skill directory, the generated `.claude/CLAUDE.md`, the hook block and the section it appended
to `CLAUDE.md` were all removed. `.claude/settings.json.graphify-bak` stays on disk (the repo does
not delete files) and stays gitignored.

---

## Consequences
* **Positive**:
  - No committed hook depends on a binary that may be absent — a missing command in
    `.claude/settings.json` breaks every tool call in the session, for everyone.
  - `.codegraph/` is the single graph source for code questions; no ambiguity about which to trust.
  - No Python dependency is introduced into a .NET + Node repo.
  - No model traffic carrying licensing/Firebase/DataHub source leaves the sanctioned providers.
* **Negative**:
  - No knowledge graph that spans PDFs, images and docs alongside code, and no visual `graph.html`
    export. If that is ever needed, run `graphify` ad hoc **outside** the repo working tree rather
    than re-installing it at project scope — and never point it at `docs/manual/samples/`, which
    holds real customer data.
  - Re-running `graphify install --project` would silently restore all four artefacts. Anyone who
    does so must revert them before committing.

---

## Related
- [.agent/rules/10-plugin-stack-rules.md](../../.agent/rules/10-plugin-stack-rules.md) — rules for
  the plugins that *were* installed (`ponytail`, `agent-skills`) and §5's constraints on any future
  plugin, including "never commit a hook that calls a binary you cannot guarantee is on `PATH`".
- [AGENTS.md](../../AGENTS.md) § Agent Tooling Rule.
