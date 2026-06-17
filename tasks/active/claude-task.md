# Claude Code Task Specification

This file contains the active task specification for **Claude Code**. Modify this file to define a task, obtain Owner approval, and run `run-claude-task.ps1` to execute it.

---

## Task Metadata
* **Status**: IDLE
* **Spec Writer**: None
* **Scope**: None
* **Approved By**: None
* **Timestamp**: None

---

## 1. Objective
Describe the overall goal of the task and the desired outcome.

---

## 2. Targeted Files
List the precise file paths to create, modify, or delete:
- `[MODIFY]` `path/to/file.cs`
- `[NEW]` `path/to/new_file.cs`
- `[DELETE]` `path/to/deleted_file.cs`

---

## 3. Detailed Step-by-Step Instructions
Provide specific implementation instructions, algorithms, layout updates, and C# code changes:
1. `Step 1`: Edit file X to implement function Y.
2. `Step 2`: Update form styling.

---

## 4. Verification Plan
Specify the commands to run to verify the implementation:
- `[ ]` `dotnet build .\AutoJMS.slnx -c Release`
- `[ ]` `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1`
- `[ ]` Manual verification checklist.
