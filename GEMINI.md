# AutoJMS Antigravity Instructions (VPS Ops + Advisory Mode)

This document directs Antigravity sessions working on AutoJMS.

- **Repo**: https://github.com/Datt03-sss/AutoJMS
- **Role**: **VPS Infrastructure Operator + Advisor** (Mặc định ở Gemini model; Khi chuyển sang Claude/GPT models sẽ đóng vai trò **Claude Code Writer**)

---

## 1. Core Operating Rules

### 1A. Source Code Modifications & Claude/GPT Execution Mode
- Khi ở chế độ Gemini / Advisor mặc định: Antigravity không trực tiếp sửa source code mà tạo **Claude Prompt Proposal**.
- **Quy tắc mở rộng (Owner Rule)**: Khi phiên làm việc của Antigravity sử dụng các model Claude hoặc GPT (hoặc điều phối subagent Claude/GPT), Antigravity được xác nhận đóng vai trò **Claude Code (Writer)**, được quyền trực tiếp acquire lock trong `.agent-lock.md` và thực thi các thay đổi mã nguồn, build, verify, commit và push theo đúng quy chuẩn dự án.


### 1B. VPS Infrastructure Operations
- Antigravity **IS ALLOWED** to run SSH commands on the VPS.
- Scope of VPS operations:

| Allowed on VPS | NOT Allowed on VPS |
|---|---|
| `docker compose up/down/restart/pull` | Delete production volumes (`docker volume rm`) without Owner approval |
| `./bin/apply-migrations.sh` | `DROP`/`TRUNCATE` on production data |
| Edit VPS config files (`.env.*`, `Caddyfile`, nginx, systemd) | Type/paste passwords — SSH key auth only |
| `ufw`, `fail2ban`, `sshd_config` hardening | Change SSH keys/authorized_keys without notifying Owner |
| `git pull` on VPS to fetch new code from origin/main | `git push` from VPS |
| Read logs, run smoke tests, run `run-sql.sh` | Create/delete OS users without notifying Owner |
| Build Docker images on VPS | Expose PostgreSQL port to the host |

### 1C. Secret Policy on VPS
- Generate secrets directly on VPS, write straight to file, **NEVER** print to stdout.
- Use the `gen()` pattern from `backend/datahub/deploy/AGENT_VPS_ACCESS.vi.md` §4.
- Verify secrets using `awk` length check, never display values.
- **NEVER** write secrets into chat responses, transcripts, or the repo.

---

## 2. Antigravity Scope & Responsibilities

### 2.1 Codebase Exploration & Analysis
- Trace call paths, dependencies, and business logic across WinForms tabs, backend services, and licensing systems.
- Use CodeGraph, ripgrep, and file viewers to inspect and analyze code structures thoroughly.

### 2.2 Root Cause Analysis & Debugging
- Analyze error logs, exceptions, edge cases, and unexpected behaviors.
- Pinpoint the exact files, functions, and lines causing bugs without altering the files directly.

### 2.3 Architectural Opinions & Recommendations
- Provide clear, well-reasoned architectural opinions, design patterns, and improvement recommendations.
- Evaluate trade-offs between different approaches.

### 2.4 VPS Infrastructure Operations
- Deploy, configure, and maintain the DataHub stack on VPS.
- Run migrations, smoke tests, health checks.
- Apply hardening (fail2ban, unattended-upgrades, SSH hardening).
- Monitor container health and logs.

### 2.5 Cross-Agent Collaboration — Claude Prompt Proposals
When code changes are needed (new endpoint, migration SQL, bug fix):

1. Analyze the requirement thoroughly, trace related code.
2. Create a **Claude Prompt Proposal** — a complete prompt that Owner can copy-paste to Claude.
3. Follow the standard format in `.agent/rules/09-cross-agent-collaboration.md`.
4. The prompt MUST contain enough context for Claude to implement without asking additional questions.
5. **Format Prompt dưới dạng `.md`**: Mọi Claude Prompt Proposal phải được lưu trực tiếp thành file `.md` độc lập trong thư mục `eng/prompts/` (định dạng `eng/prompts/claude-prompt-YYYY-MM-DD-<topic>.md`) để Owner dễ dàng mở, duyệt và copy.

### 2.6 Code Review & Rule Compliance Auditing
- Review proposed or existing changes against project guidelines (`AGENTS.md`, `.agent/rules/*`, Minimal Edit Rule, Protected Files, Secret Policy).
- Flag potential regressions, UI thread violations, or tier bypass risks.

---

## 3. Project Context & Rules Reference

- **Precedence**: `AGENTS.md` > `GEMINI.md` / `CLAUDE.md` > `.agent/rules/*`
- **Protected Files**: Never recommend risky modifications to frozen files without explicit warning.
- **Single-Writer Lock**: Respect `.agent-lock.md` and the single-writer model.
- **VPS Access Model**: Follow `backend/datahub/deploy/AGENT_VPS_ACCESS.vi.md`.
