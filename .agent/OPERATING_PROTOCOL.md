# AutoJMS Agent Operating Protocol

## Every agent must start here

Before editing code, the agent must:

1. Read `AGENTS.md`.
2. Read `.agent/INDEX.md`.
3. Read `.agent/context/`.
4. Read `.agent/rules/`.
5. Read `docs/audit/CODEBASE_AUDIT.md`.
6. Read `.agent/task-board/NOW.md`.
7. Read the task file referenced in NOW.md.
8. Produce a change plan.
9. Wait for user confirmation if the task may affect production logic.

## Allowed action levels

### Level 0 — Documentation only

Allowed:
- `.agent/`
- `docs/`
- `README.md`
- `AGENTS.md`
- `NEXT_ACTIONS.md`

Not allowed:
- production code
- scripts
- project files

### Level 1 — Build/config fix

Allowed only when task explicitly says so:
- `.csproj`
- minimal config/default JSON
- docs troubleshooting

Not allowed:
- runtime logic change

### Level 2 — Bugfix

Allowed:
- targeted `.cs` files listed in change plan

Must include:
- root cause
- smallest patch
- acceptance criteria
- rollback note

### Level 3 — Feature

Allowed only after build is stable and user approves scope.

### Level 4 — Architecture refactor

Not allowed unless explicitly requested.

