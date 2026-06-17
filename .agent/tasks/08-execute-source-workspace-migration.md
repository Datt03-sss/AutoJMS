# Execute Source Workspace Migration

## Required reading

- AGENTS.md
- .agent/OPERATING_PROTOCOL.md
- docs/migration/source-workspace-migration-plan.md
- docs/migration/source-workspace-file-map.md
- docs/migration/compile-inclusion-audit.md

## Precondition

User must explicitly say:
`EXECUTE_STRUCTURE_MIGRATION`

## Goal

Move files according to the approved migration map.

## Do not change

- app logic
- namespaces unless required
- control names
- Designer content
- server secrets

## Steps

1. Create branch or confirm git clean state.
2. Create target folders.
3. Move backend docs/config references.
4. Move tools only after path references are documented.
5. Move source only if plan says approved.
6. Update solution/project paths.
7. Run build.
8. Record result.

## Acceptance criteria

- Project builds.
- WinForms Designer still opens.
- Release docs updated.
- Agent/docs remain outside src.
- Backend outside src.
- No secrets moved into tracked paths.

