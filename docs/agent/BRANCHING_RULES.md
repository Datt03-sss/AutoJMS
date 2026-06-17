# AutoJMS Branching Rules

To ensure a clean and auditable history, developers and AI agents must strictly adhere to these branching rules.

---

## 1. Branch Naming Convention

All feature branches created by AI agents must follow this scheme:
```
agent/<agent-name>/<short-description>
```
*Examples*:
- `agent/claude/refactor-ui-control-service`
- `agent/antigravity/fix-datagrid-flickering`

Human developer branches should follow this scheme:
```
dev/<name>/<short-description>
fix/<issue-id>/<short-description>
```

---

## 2. Protected Branches
* **`main` is a protected branch**.
* Direct commits to `main` are strictly blocked.
* Merges to `main` can only be performed by the **Owner** after reviewing and approving a PR.

---

## 3. Pull Request (PR) Workflow

### Step 1: Sync with Main
Before opening a PR, merge or rebase your branch with the latest changes from `main`:
```bash
git checkout agent/claude/your-branch
git fetch origin
git rebase origin/main
```

### Step 2: Run Verification Harness
Run the full verification suite locally:
```powershell
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```
Confirm that:
- Code builds cleanly.
- Tests (if any) succeed.
- Secret scanner detects no issues.
- Project structure check passes.

### Step 3: Check git status
Verify that no untracked files are staged (e.g. `service_account.json` or debug log folders).

### Step 4: Submit PR
Provide a description containing:
- **What changed**: List of files.
- **Why**: Rationale.
- **Verification Output**: Copy-paste of the summary block from `verify.ps1`.
- **Risks**: Any areas of potential concern.
