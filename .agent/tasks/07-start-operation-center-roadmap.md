# Start Operation Center Roadmap

## Required reading

- AGENTS.md
- .agent/context/*
- .agent/rules/*
- docs/audit/CODEBASE_AUDIT.md

## Goal

Plan Operation Center enhancements after stabilization phases are complete.

## Do not modify

- Current FullStackOperation behavior
- BASE tabs
- Background jobs
- Release pipeline

## Allowed files

- `docs/roadmap/*`
- `.agent/tasks/*`
- Future design docs

## Steps

1. Define dashboard goals.
2. Define SLA engine inputs and outputs.
3. Define risk engine rules.
4. Define realtime workflow expectations.
5. Define grid performance requirements.
6. Split implementation into small future tasks.

## Acceptance criteria

- Operation Center scope is documented.
- Dependencies on auth/tier/release stability are explicit.
- No production code changed.

## Rollback notes

Documentation-only. Revert the roadmap doc if scope is wrong.

