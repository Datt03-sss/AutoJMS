# Current to Clean Structure Plan

## Current Structure

```
AutoJMS/
├── AutoJMS.slnx
├── src/AutoJMS/AutoJMS.csproj
├── AutoJMS.json
├── Program.cs, Main.cs, etc.
├── ModuleSystem/
├── archive/old-module-system/
├── ServerStructure/
├── installer/inno/
├── release/
├── docs/                    ← NEW
├── .agent/                  ← NEW
├── Properties/
├── Resources/
├── skill/
├── publish/
├── bin/, obj/
└── [other root files]
```

## Target Structure

```
AutoJMS/
├── src/
│   └── AutoJMS/
│       ├── AutoJMS.slnx
│       ├── src/AutoJMS/AutoJMS.csproj
│       └── [all .cs files]
├── tests/
│   └── AutoJMS.Tests/
├── tools/
├── docs/
├── .agent/
├── ServerStructure/
├── installer/inno/
├── release/
├── README.md
├── AGENTS.md
├── .gitignore
└── AutoJMS.sln
```

## Migration Phases

### Phase 0: Documentation Only

**Duration**: Immediate

**Actions**:
- [x] Create docs/ structure
- [x] Create .agent/ structure
- [x] Document current structure
- [ ] No code changes

**Deliverable**: This plan

### Phase 1: Standardize .agent/docs

**Duration**: Low effort

**Actions**:
- [x] Create .agent/README.md
- [x] Create .agent/context/
- [x] Create .agent/rules/
- [x] Create .agent/prompts/
- [x] Create .agent/skills/
- [x] Create .agent/workflows/
- [x] Create .agent/checklists/
- [x] Create docs/README.md
- [x] Create docs/architecture/
- [x] Create docs/api/
- [x] Create docs/release/
- [x] Create docs/migration/
- [x] Create docs/troubleshooting/
- [x] Create docs/audit/

**Deliverable**: Complete documentation

### Phase 2: Standardize Build/Release Docs

**Duration**: Low effort

**Actions**:
- [x] Document release process
- [x] Document update flow
- [x] Document versioning rules
- [x] Create AGENTS.md

**Deliverable**: Clear release documentation

### Phase 3: Service Extraction (Optional)

**Duration**: Medium effort

**Actions**:
- [ ] Extract services from Main.cs if needed
- [ ] Create service interfaces
- [ ] Move to dedicated folders

**When**: Only if Main.cs becomes unmanageable

**Risk**: Medium - requires testing

### Phase 4: src/tests/tools Split (Optional)

**Duration**: High effort

**Actions**:
- [ ] Move source to src/AutoJMS/
- [ ] Create tests project
- [ ] Create tools folder
- [ ] Update build scripts
- [ ] Update Velopack paths

**When**: Only if requested

**Risk**: High - many paths depend on current layout

### Phase 5: Obsolete File Cleanup

**Duration**: Low effort

**Actions**:
- [ ] Identify obsolete files
- [ ] Document obsolete patterns
- [ ] Archive (don't delete) until stable

**Candidates**:
- Old migration scripts
- Duplicate documentation
- Test-only scripts

## Path Updates Required

If moving to src/ structure:

| Path | Current | New |
|------|---------|-----|
| Project file | src/AutoJMS/AutoJMS.csproj | src/AutoJMS/AutoJMS.csproj |
| Publish | publish/win-x64 | src/AutoJMS/publish/win-x64 |
| Velopack pack | --packDir | src/AutoJMS/publish/win-x64 |
| ModuleProjects | archive/old-module-system/ | src/archive/old-module-system/ |

## Recommendations

1. **Start with Phase 0-1** - Already complete
2. **Phase 2** is low-risk documentation
3. **Phase 3-4** only if Main.cs grows significantly
4. **Phase 5** after new structure is stable

## Non-Goals

- NOT rewriting production code
- NOT changing existing behavior
- NOT breaking existing installers
- NOT changing update mechanism

