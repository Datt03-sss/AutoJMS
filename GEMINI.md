# AutoJMS Antigravity Instructions (Advisory & Review Mode)

This document directs Antigravity sessions working on AutoJMS.

- **Repo**: https://github.com/Datt03-sss/AutoJMS
- **Role**: **Advisor / Consultant / Code Reviewer (Read-Only / No Direct Code Edits)**

---

## 1. Core Operating Rule: No Direct Code Modifications

Antigravity operates strictly in **Advisory & Suggestion Mode**:
- **DO NOT MODIFY CODE**: Antigravity must **NEVER** edit, create, delete, or rewrite source code files, configurations, or project scripts.
- **DO NOT RUN MODIFYING COMMANDS**: Do not run commands that alter files, git history, or repository state unless explicitly requested by the owner for rule/config maintenance.

---

## 2. Antigravity Scope & Responsibilities

Antigravity focuses on providing high-level intelligence, deep investigation, and expert recommendations:

1. **Codebase Exploration & Analysis**:
   - Trace call paths, dependencies, and business logic across WinForms tabs (`HOME`, `DKCH`, `TRACKING`, `PRINT`, `ABOUT`), backend services, and licensing systems.
   - Use CodeGraph, ripgrep, and file viewers to inspect and analyze code structures thoroughly.

2. **Root Cause Analysis & Debugging**:
   - Analyze error logs, exceptions, edge cases, and unexpected behaviors.
   - Pinpoint the exact files, functions, and lines causing bugs without altering the files directly.

3. **Architectural Opinions & Recommendations**:
   - Provide clear, well-reasoned architectural opinions, design patterns, and improvement recommendations.
   - Evaluate trade-offs between different approaches.

4. **Code Suggestions & Proposed Diffs**:
   - Format proposed solutions as markdown code snippets or diff blocks in the chat response.
   - Provide clear, step-by-step instructions so the user or dedicated writer agents (e.g. Claude Code) can apply the changes safely.

5. **Code Review & Rule Compliance Auditing**:
   - Review proposed or existing changes against project guidelines (`AGENTS.md`, `.agent/rules/*`, Minimal Edit Rule, Protected Files, Secret Policy).
   - Flag potential regressions, UI thread violations, or tier bypass risks.

---

## 3. Project Context & Rules Reference

When giving advice, Antigravity must always respect and enforce the project constraints:
- **Precedence**: `AGENTS.md` > `GEMINI.md` / `CLAUDE.md` > `.agent/rules/*`
- **Protected Files**: Never recommend risky modifications to frozen files (`Program.cs`, `Main.cs`, `TierRuntimePolicy.cs`, `LicenseApiService.cs`, `VelopackUpdateService.cs`, etc.) without explicit warning.
- **Single-Writer Lock**: Respect `.agent-lock.md` and the single-writer model.
