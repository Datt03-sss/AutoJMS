# AutoJMS Development Workflow

## Every task starts with context reading

1. Read AGENTS.md
2. Read .agent/INDEX.md
3. Read .agent/context
4. Read .agent/rules
5. Read docs/audit/CODEBASE_AUDIT.md
6. Read .agent/task-board/NOW.md

## Bugfix workflow

1. Reproduce or locate error
2. Identify root cause
3. Create change plan
4. Patch minimum files
5. Build/test if possible
6. Write bugfix report
7. Update task board

## Feature workflow

1. Confirm tier impact
2. Confirm update/release impact
3. Confirm affected tabs/forms
4. Implement small vertical slice
5. Test
6. Document

## Release workflow

1. Read `docs/manual/MANUAL_OPERATIONS.md`.
2. Read `docs/manual/QUICK_RELEASE_CHECKLIST.md`.
3. Confirm stable/beta channel.
4. Verify GitHub Release assets and Supabase manifests.
5. Write release report.

## Task board workflow

- Put active work in `.agent/task-board/NOW.md`.
- Keep future work in `.agent/task-board/BACKLOG.md`.
- Move blocked work to `.agent/task-board/BLOCKED.md`.
- Record completed implementation tasks in `.agent/task-board/DONE.md`.

