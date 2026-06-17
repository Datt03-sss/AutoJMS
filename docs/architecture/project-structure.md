# Project Structure

> Status 2026-06-03: this older structure note has been superseded by docs/migration/PROJECT_STRUCTURE_MIGRATION_REPORT.md. Prefer that migration report for the current workspace layout after the executed structure move.

## Current Structure After Migration

Verified on 2026-06-03 after executing structure migration.

```text
AutoJMS/
├── .agent/
├── docs/
├── src/
│   ├── AutoJMS/
│   └── AutoJMS.Abstractions/
├── backend/
│   └── render-license-server/
├── infra/
│   ├── supabase/
│   ├── firebase/
│   └── github-release/
├── installer/
│   └── inno/
├── release/
├── tools/
│   ├── maintenance/
│   └── reactor/
├── tests/
├── archive/
├── AutoJMS.slnx
└── README.md
```

Active build command:

```powershell
dotnet build src\AutoJMS\AutoJMS.csproj -c Debug
```

Latest verification: build succeeded with 0 errors and 3 warnings on the latest incremental run.

## Historical Structure Notes
## A. Current Structure

Verified from repository listing on 2026-06-03.

```txt
AutoJMS/
├── src/AutoJMS/AutoJMS.csproj
├── AutoJMS.slnx
├── Program.cs
├── Main.cs
├── Main.Designer.cs
├── FullStackOperation.cs
├── *Service.cs
├── ModuleSystem/
├── archive/old-module-system/
├── ServerStructure/
├── installer/inno/
├── release/
├── docs/
├── Resources/
└── Properties/
```

Verification notes:

- Production `.cs` files currently live mostly at repository root.
- `ModuleSystem/`, `archive/old-module-system/`, `ServerStructure/`, `installer/inno/`, `release/`, `docs/`, `Resources/`, and `Properties/` exist.
- `AutoJMS.slnx` exists; `AutoJMS.sln` is `NEED VERIFY` and should not be assumed.
- Root `modules/` is not present, but `src/AutoJMS/AutoJMS.csproj` now guards root `modules/*.json` content includes with `Exists(...)`.

## B. Target Vibe Coding Structure

This is the target future structure, not Phase 1 current state.

```txt
AutoJMS/
├── .agent/
├── docs/
├── ServerStructure/
├── installer/inno/
├── release/
├── archive/old-module-system/
├── src/
│   └── AutoJMS/
├── tests/
├── tools/
├── README.md
├── AGENTS.md
├── .gitignore
└── AutoJMS.sln
```

Notes:

- Do not move code into `src/` in Phase 1.
- `src/` is a future migration target only.
- WinForms Designer, Inno Setup, Velopack, and .NET Reactor paths may depend on the current root-level layout.
- Any move to `src/` must update project paths, designer resources, release scripts, installer scripts, Reactor config, and module references.

## C. Migration Plan

| Phase | Scope | Status |
| ----- | ----- | ------ |
| Phase 0 | Audit and document only | Done |
| Phase 1 | Create `.agent` and `docs` | In progress / documentation-only |
| Phase 2 | Fix build-blocking issues | Build blocker for root `modules/*.json` fixed on 2026-06-03 |
| Phase 3 | Normalize release/manual docs | In progress |
| Phase 4 | Extract services from MainForm gradually | Future only, explicit task required |
| Phase 5 | Consider `src/`, `tests/`, `tools` migration | Future only, explicit migration plan required |

The older sections below remain historical/supporting detail. If they conflict with sections A-C above, prefer sections A-C and mark the older detail `NEED VERIFY`.

## Current Verified Structure

Verified from repository listing on 2026-06-03.

```text
AutoJMS root
├── src/AutoJMS/AutoJMS.csproj
├── AutoJMS.slnx
├── Program.cs
├── Main.cs / Main.Designer.cs / Main.resx
├── FullStackOperation.cs / FullStackOperation.Designer.cs / FullStackOperation.resx
├── frmLogin.cs / frmLogin.Designer.cs / frmLogin.resx
├── App*.cs, *Service.cs, *Models.cs, Tier*.cs, WebView*.cs
├── ModuleSystem/
├── archive/old-module-system/
│   ├── AutoJMS.Abstractions/
│   ├── AutoJMS.RetryPolicy/
│   └── AutoJMS.Selectors/
├── ServerStructure/
│   ├── Render server/server.js
│   └── Supabase/autojms-modules/
├── installer/inno/
├── release/
├── Resources/
├── Properties/
├── docs/
└── .agent/
```

Important current facts:

- There is no `src/` folder in the current code layout.
- Most production `.cs` files are still at repository root.
- `src/AutoJMS.Abstractions` is referenced by the main project.
- `archive/old-module-system/AutoJMS.RetryPolicy` and `archive/old-module-system/AutoJMS.Selectors` exist as module projects.
- Build output folders such as `bin/`, `obj/`, `publish/`, and `release/output/` are not source structure.
- The project file references a root `modules/` folder, but those content includes are now conditional. A clean checkout can build without root `modules/*.json`; runtime behavior for absent module defaults remains `NEED VERIFY`.

The target clean structure and migration notes below are planning material only. They are not current state.

## Current Structure

```
AutoJMS/                                    ← Project root
├── AutoJMS.slnx                             ← Solution file
├── src/AutoJMS/AutoJMS.csproj                           ← Main project
├── AutoJMS.json                             ← User settings template
├── tools/reactor/AutoJMS_Reactor.nrproj                 ← .NET Reactor project
│
├── Program.cs                               ← Entry point
├── Main.cs / Main.Designer.cs              ← Main form
├── FullStackOperation.cs                   ← ULTRA-only form
├── frmLogin.cs / frmLogin.Designer.cs    ← Login dialog
│
├── [Core services - 60+ files]           ← All .cs files at root
│   ├── LicenseApiService.cs
│   ├── JmsApiClient.cs
│   ├── InventorySyncService.cs
│   ├── SupabaseDbService.cs
│   ├── VelopackUpdateService.cs
│   └── ... (all services at root level)
│
├── Properties/                             ← Resources
│   └── Resources.resx
│
├── ModuleSystem/                           ← Dynamic module loader
│   ├── ActiveModules.cs
│   ├── BuiltInModules.cs
│   ├── ModuleRegistry.cs
│   ├── ModuleStartup.cs
│   └── ...
│
├── archive/old-module-system/                         ← Sub-projects
│   ├── AutoJMS.Abstractions/
│   ├── AutoJMS.RetryPolicy/
│   └── AutoJMS.Selectors/
│
├── ServerStructure/                        ← Backend documentation
│   ├── Firebase/config-key.json
│   ├── Render server/server.js
│   ├── Supabase/autojms-modules/
│   └── Github release/
│
├── installer/inno/                             ← Inno Setup
│   ├── AutoJMS.iss
│   ├── build-installer.ps1
│   ├── README_INSTALLER.md
│   └── installer-output/
│
├── release/                               ← Build scripts
│   ├── build-release.ps1
│   ├── build-release.bat
│   └── output/
│
├── docs/                                  ← Documentation (NEW)
│   ├── README.md
│   ├── architecture/
│   ├── api/
│   ├── release/
│   ├── migration/
│   ├── troubleshooting/
│   └── audit/CODEBASE_AUDIT.md
│
├── .agent/                               ← Agent context (NEW)
│   ├── README.md
│   ├── context/
│   ├── rules/
│   ├── prompts/
│   ├── skills/
│   ├── workflows/
│   └── checklists/
│
├── skill/                                 ← (unknown purpose)
├── publish/                              ← Build output
├── bin/obj/                              ← Build artifacts
└── [MD files at root]
    ├── COMMERCIAL_HARDENING_REPORT.md
    ├── REACTOR_SAFE_CLASSES.md
    └── Structure.txt
```

---

## Current Structure After Migration

Verified on 2026-06-03 after executing structure migration.

```text
AutoJMS/
├── .agent/
├── docs/
├── src/
│   ├── AutoJMS/
│   └── AutoJMS.Abstractions/
├── backend/
│   └── render-license-server/
├── infra/
│   ├── supabase/
│   ├── firebase/
│   └── github-release/
├── installer/
│   └── inno/
├── release/
├── tools/
│   ├── maintenance/
│   └── reactor/
├── tests/
├── archive/
├── AutoJMS.slnx
└── README.md
```

Active build command:

```powershell
dotnet build src\AutoJMS\AutoJMS.csproj -c Debug
```

Latest verification: build succeeded with 0 errors and 3 warnings on the latest incremental run.

## Historical Structure Notes
## A. Current Structure Analysis

### Root-Level Files Issue

All C# service files are at the project root (~60 files). This is a **historical artifact** from before project restructuring was completed.

**Impact**:
- Solution explorer can be cluttered
- Harder to find related files
- No clear separation of concerns
- But: currently functional

### Module Projects

Located in `archive/old-module-system/`:
- AutoJMS.Abstractions
- AutoJMS.RetryPolicy  
- AutoJMS.Selectors

Only `AutoJMS.Abstractions` is referenced in the main project.

### Build/Release Structure

- `release/build-release.ps1` - Main release script
- `installer/inno/AutoJMS.iss` - Inno Setup
- `ServerStructure/` - Documentation only (not deployed)

---

## B. Target Clean Structure

```
AutoJMS/
├── src/                                   ← Source code
│   └── AutoJMS/
│       ├── AutoJMS.slnx
│       ├── src/AutoJMS/AutoJMS.csproj
│       ├── Program.cs
│       ├── Main.cs / Main.Designer.cs
│       ├── FullStackOperation.cs
│       ├── frmLogin.cs
│       ├── Properties/
│       │   └── Resources.resx
│       │
│       ├── Services/                      ← Organized services
│       │   ├── License/
│       │   │   ├── LicenseApiService.cs
│       │   │   ├── JmsAuthStateService.cs
│       │   │   └── JmsAuthTokenService.cs
│       │   ├── Api/
│       │   │   ├── JmsApiClient.cs
│       │   │   ├── InventorySyncService.cs
│       │   │   └── WaybillTrackingService.cs
│       │   ├── Update/
│       │   │   ├── VelopackUpdateService.cs
│       │   │   ├── SmallUpdateService.cs
│       │   │   └── MajorUpdateService.cs
│       │   ├── Data/
│       │   │   ├── SupabaseDbService.cs
│       │   │   └── GoogleSheetService.cs
│       │   └── Print/
│       │       └── PrintService.cs
│       │
│       ├── Infrastructure/               ← Cross-cutting concerns
│       │   ├── AppConfig.cs
│       │   ├── AppPaths.cs
│       │   ├── AppLogger.cs
│       │   ├── AppVersion.cs
│       │   ├── SecureConfigCrypto.cs
│       │   └── TierRuntimePolicy.cs
│       │
│       ├── ModuleSystem/
│       │   ├── ModuleStartup.cs
│       │   ├── ModuleRegistry.cs
│       │   └── ...
│       │
│       ├── Models/
│       │   ├── WaybillModels.cs
│       │   ├── SupabaseModels.cs
│       │   └── ...
│       │
│       └── Resources/
│           └── Resources.resx
│
├── tests/                                 ← Test projects
│   └── AutoJMS.Tests/
│
├── tools/                                 ← Build/dev tools
│
├── archive/old-module-system/                        ← Keep at root (referenced)
│   ├── AutoJMS.Abstractions/
│   ├── AutoJMS.RetryPolicy/
│   └── AutoJMS.Selectors/
│
├── ServerStructure/                      ← Backend documentation
├── installer/inno/                            ← Inno Setup scripts
├── release/                              ← Release scripts
│
├── docs/                                 ← All documentation
│
├── .agent/                              ← Agent context
│
├── README.md                             ← Project README
├── AGENTS.md                            ← Agent instructions
└── .gitignore
```

---

## C. Migration Phases

### Phase 0: Documentation Only (COMPLETED)

- [x] Create docs/ structure
- [x] Create .agent/ structure
- [x] Document current structure
- [x] Create migration plan

**Deliverable**: This document + all docs/.agent files created.

---

### Phase 1: Standardize Agent Context (COMPLETED)

- [x] Create .agent/README.md
- [x] Create .agent/context/
- [x] Create .agent/rules/
- [x] Create .agent/prompts/
- [x] Create .agent/skills/
- [x] Create .agent/workflows/
- [x] Create .agent/checklists/

---

### Phase 2: Standardize Documentation (COMPLETED)

- [x] Create docs/README.md
- [x] Create docs/architecture/
- [x] Create docs/api/
- [x] Create docs/release/
- [x] Create docs/migration/
- [x] Create docs/troubleshooting/
- [x] Create docs/audit/CODEBASE_AUDIT.md

---

### Phase 3: Standardize Build/Release Docs (COMPLETED)

- [x] Document release process
- [x] Document update flow
- [x] Document versioning rules
- [x] Create AGENTS.md

---

### Phase 4: Service Extraction (OPTIONAL - Only if Main.cs grows)

**When to do**: If Main.cs exceeds 5000 lines or becomes unmanageable.

**Actions**:
- [ ] Create Services/ folders at root
- [ ] Move service files in batches
- [ ] Update namespaces
- [ ] Update project file references
- [ ] Test thoroughly

**Risk**: Medium - requires testing all services.

**Path updates needed**:
```xml
<Compile Include="Services\License\LicenseApiService.cs" />
```

---

### Phase 5: src/ Split (OPTIONAL - Only if explicitly requested)

**When to do**: Only if developer team explicitly requests this.

**Actions**:
- [ ] Create src/AutoJMS/
- [ ] Move all source files
- [ ] Update .csproj location
- [ ] Update build scripts (Velopack pack paths)
- [ ] Update release/build-release.ps1
- [ ] Update archive/old-module-system/ references
- [ ] Update .gitignore

**Risk**: High - many build/release paths depend on current layout.

**Path updates needed**:
```powershell
# build-release.ps1
$Project = "src/AutoJMS/AutoJMS.csproj"
$PublishDir = "src/AutoJMS/publish/win-x64"

# src/AutoJMS/AutoJMS.csproj
<Compile Remove="archive\old-module-system\**\*.cs" />
<!-- Would need new path -->
```

---

### Phase 6: Obsolete File Cleanup (OPTIONAL)

**Candidates**:
- [ ] Old migration scripts
- [ ] Duplicate documentation
- [ ] Test-only scripts
- [ ] Unknown files in skill/

**Rule**: Archive don't delete until new structure is stable.

---

## D. Recommendations

### Immediate (Done)

- [x] Create documentation
- [x] Create agent context
- [x] Document current structure
- [x] Create migration plan

### Short-term

- [ ] Implement token masking in logs (TODO in code)
- [ ] Review and clean up skill/ folder
- [ ] Verify all documentation is accurate

### Long-term (Only if needed)

- [ ] Service extraction (Main.cs > 5000 lines)
- [ ] src/ split (only if team requests)

### DO NOT DO

- [ ] NOT rewrite production code
- [ ] NOT change existing behavior
- [ ] NOT break existing installers
- [ ] NOT change update mechanism
- [ ] NOT delete files without archiving

---

## E. Key Paths Reference

### Current Build Paths

| Path | Used By |
|------|---------|
| `src/AutoJMS/AutoJMS.csproj` | dotnet, build scripts |
| `publish/win-x64/` | Velopack pack |
| `release/output/` | Release script output |
| `installer/inno/installer-output/` | Inno Setup output |

### Current Source Paths

| Path | Used By |
|------|---------|
| Root `.cs` files | Project compilation |
| `ModuleSystem/` | Module loading |
| `archive/old-module-system/` | Project references |

### Module Paths

| Path | Purpose |
|------|---------|
| `AppPaths.InstallDir` | AppContext.BaseDirectory |
| `AppPaths.InstallRoot` | Parent of InstallDir |
| `AppPaths.UserDataDir` | `InstallRoot\AppData` |
| `AppPaths.BrowserDataDir` | WebView2 data |




