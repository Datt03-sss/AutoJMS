# AutoJMS Onboarding

## What is AutoJMS?

AutoJMS is a .NET 8 WinForms logistics automation app using SunnyUI and WebView2. It automates JMS workflows, tracking, printing, update handling and ULTRA operation dashboards.

## Read these first

1. `AGENTS.md`
2. `.agent/INDEX.md`
3. `.agent/context/`
4. `.agent/rules/`
5. `docs/audit/CODEBASE_AUDIT.md`
6. `docs/architecture/project-structure.md`
7. `.agent/task-board/NOW.md`

## Do not change without explicit task

- `.cs`
- `.Designer.cs`
- `.csproj`
- `.sln` / `.slnx`
- `.iss`
- `.ps1` / `.bat`
- `server.js`
- secrets/service account files

## Build

Primary project:

```txt
AutoJMS.csproj
```

Typical verification command when allowed:

```bash
dotnet build src/AutoJMS/AutoJMS.csproj -c Debug
```

## Release

- Release scripts: `release/`
- Installer scripts: `installer/inno/`
- Manual operations: `docs/manual/MANUAL_OPERATIONS.md`
- Quick release checklist: `docs/manual/QUICK_RELEASE_CHECKLIST.md`

## Known risks

- WebView2 must run on UI thread.
- BASE must not run background inventory/database sync.
- JMS authToken and license JWT are different tokens.
- FullStackOperation lifecycle must handle close/reopen and disposed controls.
- GitHub Releases host binary; Supabase hosts small manifests/config.
- Service account material must not be committed or exposed.

## Next recommended task

Read:

```txt
.agent/tasks/01-fix-build-blockers.md
```

Then confirm build state before starting feature work.


