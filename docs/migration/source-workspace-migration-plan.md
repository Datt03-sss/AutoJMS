# Source Workspace Migration Plan

## Goal

Tách workspace:

```txt
.agent = AI workspace
docs = documentation
src = production source
backend = external server/config
tools = build/release/installer scripts
artifacts = generated output
legacy = uncertain/obsolete
```

## Target structure

Planned target layout:

```txt
AutoJMS/
├── .agent/
│   ├── context/
│   ├── rules/
│   ├── prompts/
│   ├── skills/
│   ├── workflows/
│   ├── checklists/
│   ├── task-board/
│   ├── templates/
│   └── handoff/
│
├── docs/
│   ├── audit/
│   ├── architecture/
│   ├── api/
│   ├── dev/
│   ├── manual/
│   ├── migration/
│   ├── release/
│   ├── roadmap/
│   └── troubleshooting/
│
├── src/
│   ├── AutoJMS/
│   │   ├── src/AutoJMS/AutoJMS.csproj
│   │   ├── Program.cs
│   │   ├── Forms/
│   │   ├── Services/
│   │   ├── Models/
│   │   ├── Data/
│   │   ├── Config/
│   │   ├── Security/
│   │   ├── Update/
│   │   ├── WebView/
│   │   ├── Printing/
│   │   ├── Tracking/
│   │   ├── Resources/
│   │   └── Properties/
│   │
│   └── AutoJMS.Abstractions/
│
├── tests/
├── backend/
├── tools/
├── artifacts/
├── legacy/
├── README.md
├── AGENTS.md
├── .gitignore
└── AutoJMS.sln
```

This target structure is not executed in Step A. It is the approved migration destination after explicit user confirmation.

## Why now

- Project root đang rối.
- Agent dễ sửa nhầm file.
- Build/release/backend/docs lẫn với app source.
- Vibe coding cần guardrails rõ.

## Non-goals

- Không refactor logic.
- Không đổi behavior.
- Không đổi namespace nếu chưa cần.
- Không rewrite MainForm.
- Không rewrite FullStackOperation.
- Không sửa release/build/installer script trong plan phase.
- Không di chuyển file production nếu chưa có xác nhận `EXECUTE_STRUCTURE_MIGRATION`.

## Migration phases

### Phase M0 — Plan only

- Create maps.
- Create audit.
- No move.
- Create placeholder READMEs for target workspaces only.

### Phase M1 — Prepare folders

- Create empty folders.
- Add README placeholders.
- Update docs.
- Do not copy/move production files yet.

### Phase M2 — Move backend references

- Move/copy `ServerStructure` to `backend`.
- Keep old `ServerStructure` until verified.
- Do not move secrets.
- `infra/firebase/config-key.json` and any service account-like files require manual review before copy.

### Phase M3 — Move tooling

- Move `Release` to `tools/release`.
- Move `Installer` to `tools/installer`.
- Update docs only first.
- Do not change scripts until a path update task is approved.
- Redist installers can be large binary assets; confirm whether they should remain tracked.

### Phase M4 — Move app source to `src/AutoJMS`

High risk. Requires:

- Update `.sln` / `.slnx`.
- Update `.csproj`.
- Verify Designer dependent files.
- Verify `.resx`.
- Verify `Resources`.
- Verify `Properties`.
- Verify Inno path.
- Verify Release script path.
- Verify Reactor project path.
- Build Debug.
- Build Release.
- Launch app smoke test.

Do not execute this phase until user explicitly confirms and a branch/rollback path exists.

### Phase M5 — Cleanup root

- Keep only README, AGENTS, `.gitignore`, solution, top-level folders.
- Move uncertain files to `legacy` after verify.
- Do not delete original files until build/release smoke tests pass.

## Rollback plan

- Use git branch before migration.
- Move files back if build fails.
- Do not delete original until build passes.
- Commit per phase.
- If WinForms Designer breaks, immediately revert source move phase.
- If release/installer script path breaks, revert tool move phase and document required script updates.

## Execution gate

Step B must not start unless the user says exactly:

```txt
EXECUTE_STRUCTURE_MIGRATION
```

