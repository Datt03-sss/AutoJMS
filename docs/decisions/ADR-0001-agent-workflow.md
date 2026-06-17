# ADR 0001: Safe Agent Developer Workflow

## Status
**Accepted**

---

## Context
The AutoJMS project wishes to utilize AI agents (such as Antigravity and Claude Code) for coding and refactoring tasks. However, allowing agents unrestricted write permissions to the master branch or licensing/release services introduces safety and stability risks.

---

## Decision
We establish a standardized, multi-tiered developer workflow for all AI agents:
1. **Isolated Development**: Agents must develop exclusively on feature branches prefixed with `agent/<name>/`.
2. **Quality Gates Check**: Before requesting review, agents must run a unified verification script (`verify.ps1`) checking:
   - Solution restore & compile
   - Test execution (if any)
   - Secret patterns scanning
   - Directory and project structure conformity
3. **No Direct Production Controls**:
   - Only the human project Owner is authorized to build and upload production releases (using `-Upload`).
   - All licensing (`LicenseApiService`), updates (`VelopackUpdateService`), and integrity checks (`HashVerifier`) files are classified as **frozen** and cannot be modified by agents.

---

## Consequences
* **Positive**:
  - Prevents security leaks (e.g. Google service account keys).
  - Assures codebase stability by automating restore and compile checks.
  - Clearly defines boundaries between BASE and ULTRA tiers.
* **Negative**:
  - Requires agents to run verification steps before every submission, which can add slight overhead to simple documentation edits.
