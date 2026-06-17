# Agent Runners & Tools

This directory contains automated runner scripts and tools for managing collaborative AI agent workflows (Antigravity and Claude Code) in the AutoJMS workspace.

---

## 1. run-claude-task.ps1

This script automates the handoff from Antigravity to Claude Code. It reads the active spec, pulls the latest changes from `origin/main` safely, and invokes Claude Code.

### How to Run:
Run the script using PowerShell from the root of the repository:

```powershell
powershell -ExecutionPolicy Bypass -File .\eng\agents\run-claude-task.ps1
```

### Pre-requisites:
- `claude` CLI must be installed and authenticated in the terminal context.
- Your terminal must be operating on the `main` branch.
