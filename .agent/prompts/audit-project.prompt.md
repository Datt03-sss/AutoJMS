# Audit Project Prompt

Use this prompt when asked to audit the AutoJMS project.

## Context

You are auditing the AutoJMS codebase. This is a logistics automation desktop app (.NET 8 WinForms).

**DO NOT modify any production code. Only audit and document.**

## Audit Steps

### 1. Scan Project Structure

```bash
# List all files
Get-ChildItem -Recurse -File | Select-Object FullName, Length

# List folders
Get-ChildItem -Directory | Select-Object Name
```

### 2. Identify Key Files

Look for:
- Solution file (.sln or .slnx)
- Project file (.csproj)
- Entry point (Program.cs)
- Main form (Main.cs, Main.Designer.cs)
- FullStackOperation form
- Login form (frmLogin.cs)
- Service files (*Service.cs, *Client.cs)

### 3. Document Architecture

Create `docs/audit/CODEBASE_AUDIT.md` with:

1. **Solution/Project Map**
   - .sln / .csproj files
   - `src/AutoJMS.Abstractions` and `archive/old-module-system` module projects
   - Installer folder
   - Release folder
   - `backend/` and `infra/` folders

2. **Entry Points**
   - Program.cs flow
   - MainForm initialization
   - Login/license flow
   - Update flow

3. **UI/Forms Map**
   - All tabs and forms
   - Which are ULTRA-only
   - Navigation between forms

4. **Services Map**
   - List all service classes
   - Their responsibilities
   - Dependencies

5. **Background Jobs Map**
   - Timers
   - Async tasks
   - Tier restrictions

6. **Config/Data Map**
   - JSON files
   - Secure storage
   - Logs

7. **Release Pipeline Map**
   - Build scripts
   - Installer scripts
   - Update mechanism

8. **Known Issues**
   - Risks
   - Tech debt
   - Security concerns

## Output Format

```markdown
# AutoJMS Codebase Audit

## 1. Solution/Project Map

### Files
- File: path/to/file
- Description: what it does

### Structure
[folder tree]

## 2. Entry Points

### Program.cs
[flow description]

### Main.cs
[flow description]

...

## 3. UI/Forms Map

| Form | Tier | Purpose |
|------|------|---------|
| HOME | BASE | ... |
...

## 4. Services Map

| Service | File | Responsibilities |
|---------|------|-----------------|
| LicenseApiService | LicenseApiService.cs | ... |
...

## 5. Background Jobs Map

| Job | Tier | Trigger |
|-----|------|---------|
| _autoSyncTimer | ULTRA | Every 1 second |
...

## 6. Config/Data Map

| File | Location | Contents |
|------|----------|----------|
| AutoJMS.json | AppData\ | User settings |
...

## 7. Release Pipeline Map

### Build
[steps]

### Installer
[steps]

### Update
[steps]

## 8. Known Issues

### High Priority
[issues]

### Medium Priority
[issues]

### Low Priority
[issues]
```

## Rules

1. **Read files before summarizing** - Don't guess, read actual code
2. **Include line numbers for key code** - Makes it actionable
3. **Note UNKNOWN if unsure** - Don't make up facts
4. **Focus on structure** - Not deep implementation details
5. **Mark ULTRA vs BASE** - Important for understanding

## After Audit

Create:
1. `docs/audit/CODEBASE_AUDIT.md` - Main audit document
2. `docs/architecture/project-structure.md` - Structure with current vs target
3. Update `README.md` if missing structure info
4. Check `.gitignore` for completeness
