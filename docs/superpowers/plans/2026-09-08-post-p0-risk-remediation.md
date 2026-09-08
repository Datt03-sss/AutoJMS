# Post-P0 Risk Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **STATUS: EXECUTED — all four tasks complete (2026-09-08).** Commits `4196055..HEAD`: Task 1
> `d357c06`, Task 2 `c9bbc83`, Task 3 `acbe7d0`/`3d93c9d`/`b7a49f2`/`84a70ac`, Task 4 `6ce9217`/`5e5d18e`.
> The unchecked `- [ ]` boxes below are the plan **as written before execution** and are left
> as-authored on purpose — this file is the record of what was planned, not a live checklist.
> The same applies to every line number it cites (`RuntimeConfigurationHealthCheck.cs:48` and the
> `:33/:43/:48` arms in R1, the `Modify:` path in Task 1): those are coordinates in the
> **pre-change** tree at `4196055` and no longer point at the same lines. Read the code by symbol.
> R4's answer turned out to be **no** — the P0 log did not survive; see §12 of
> `docs/review/p0-report-2026-09-07.md`.

**Goal:** Close the four post-P0 items that are genuine defects fixable without an Owner decision, and recover the two missing G7/G8 metrics from evidence that may already exist — without starting P1.

**Architecture:** Four independent tasks, no shared state. Task 1 is a one-line branch fix in a health check, driven by a failing xUnit test. Task 2 is a `.gitignore` rule verified with `git check-ignore`. Task 3 builds an offline log aggregator, self-tested against a fixture with no network and no database. Task 4 is a read-only VPS probe that answers one open question and records the answer. Tasks 1–3 can be done in any order; Task 4 depends on Task 3's deliverable.

**Tech Stack:** C# / .NET (xUnit), Python 3 standard library only (no pytest, no new dependency), PowerShell harness (`eng/harness/verify.ps1`), git.

---

## Global Constraints

Copied from `CLAUDE.md`, `AGENTS.md`, and the P0 report. Every task's requirements implicitly include this section.

- The GitHub repo `Datt03-sss/AutoJMS` is **PUBLIC**. No VPS IP, account name, key path, container ID, or firewall threshold may appear in any tracked file. The hostname `dev.jmsauto.online` is already in 26 tracked files and is allowed.
- **Never commit** `.env`, service account keys, `*.pfx`, `*.pem`, or any token/key file. Mask tokens in logs as `first4...last4`.
- **Never `git add .`** — stage every path explicitly.
- **Never force push. Never rewrite history.** Work happens on `main`; commit and push only after a passing Release build.
- **Protected Files** — require an explicit Owner request and do not have one: `src/AutoJMS/Program.cs`, `src/AutoJMS/Forms/Main.cs`, `src/AutoJMS/Forms/Main.Designer.cs`, `src/AutoJMS/Licensing/TierRuntimePolicy.cs`, `src/AutoJMS/Licensing/LicenseApiService.cs`, `src/AutoJMS/Licensing/JmsAuthTokenService.cs`, `src/AutoJMS/Updates/VelopackUpdateService.cs`, `backend/datahub/docker-compose.yml`, DataHub production config, database schema migrations, `release/build-release.ps1`, `installer/inno/AutoJMS.iss`.
- **Minimal Edit Rule.** Apply the smallest change that fixes the defect. Do not refactor. Match existing naming, comment density, and formatting.
- **P1 stays 🔒 LOCKED.** No task here starts P1, proposes starting it, or signs any Owner Decision. OD-2 and OD-6 are unsigned; OD-1 is deferred to P6.
- **No load runs. No migrations. No writes to staging.** Task 4 is read-only (`logs`, `grep -c`) and nothing else.
- Do **not** re-enable `DATAHUB_ALLOW_STAGING_TEST_ISSUER=true` to make any test go green.
- Build gate before every push:
  ```
  dotnet build ./AutoJMS.slnx -c Release      → 0 Warning(s), 0 Error(s)
  powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1  → ALL GATES PASSED
  ```
- VPS access pattern — environment variables do **not** survive between Bash tool calls, so every VPS command must carry this prefix in the same call, and must never print the values:
  ```bash
  cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && <command>
  ```

---

## Risk register — what is open right now

Verified against the working tree at `4196055` on 2026-09-08. `origin/main...HEAD` = `0 0`.

| # | Risk | Severity | Status | Handled by |
|---|---|---|---|---|
| R1 | `RuntimeConfigurationHealthCheck.cs:48` — no arm for *staging + RSA key material*, so a correctly-configured staging host reports `missing: "staging license verifier"`, `/health/ready` answers **503**, and `docker compose up -d` exits 1. Behaviour is correct (RS256 passed 24/24); only the indicator is wrong. | 🔴 High | **Open, verified** — the three-arm block is unchanged at `:33/:43/:48` | **Task 1** |
| R2 | `_verify/` and `.agents/` are untracked **and** unignored (`git check-ignore` → exit 1 for both). One `git add .` commits Velopack hash manifests and agent run scratch to a PUBLIC repo. | 🟠 Medium | **Open, verified** | **Task 2** |
| R3 | G7/G8 are missing `transaction_p95` and `commit_p95` at every load level. No aggregator for either exists anywhere in the repo (`git grep` → 0 hits). | 🟠 Medium | **Open, verified** | **Task 3** |
| R4 | Whether the `log_min_duration_statement = 0` log that Task 5 produced still exists has never been checked. If it does, R3's two metrics are recoverable from the **official** runs at zero re-run cost. | 🟠 Medium | **Unknown — never probed** | **Task 4** |
| R5 | RPO is undefined. No backup schedule, no offsite copy, and `backup-postgres.ps1:48` prints *"Encrypt and upload it outside this script."* — a step never executed or tested. OD-8 is unsatisfied. | 🔴 High | Open | **Owner decision — not in this plan** |
| R6 | `bulk-10 v2 p99 = 2198.7 ms` is a 5.6× outlier with no explanation. Becomes mandatory to explain if the Owner picks option A. | 🟠 Medium | Open | **Owner decision — not in this plan** |
| R7 | G4's restore never ran into a temp instance; G6 items 3.3/3.4 (API→Postgres TLS) were never measured. Both are evidence debt, not vulnerabilities. | 🟠 Medium | Open | **Owner decision — not in this plan** |
| R8 | `caddy` has no `healthcheck` block, so any "all services healthy" gate misjudges staging forever. | 🟠 Medium | Open | **Blocked — `docker-compose.yml` is a Protected File** |
| R9 | Secret gate part 4 is `INACTIVE` on any machine without a local infra denylist — the normal state of a fresh clone. | 🔵 Low | Accepted by design | Documented, no action |
| R10 | OD-2 and OD-6 unsigned; the G7/G8 A/B/C choice unmade ⇒ P1 locked. | 🔴 Blocking | Open | **Owner signature — not in this plan** |

**Explicitly out of scope:** R5, R6, R7, R8, R10, all of P1, re-running any load, and changing any published baseline number in `docs/review/p0-report-2026-09-07.md` §5.

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/AutoJMS.DataHub.Api/Health/RuntimeConfigurationHealthCheck.cs` (modify `:48`) | Report configuration readiness. Gains the missing staging+RSA arm. | 1 |
| `tests/AutoJMS.DataHub.Api.Tests/Health/RuntimeConfigurationHealthCheckTests.cs` (modify, append one `[Fact]`) | Regression coverage for the staging+RSA arm. | 1 |
| `.gitignore` (modify, append one block) | Keep per-machine scratch out of a PUBLIC repo. | 2 |
| `backend/datahub/tests/pg_log_metrics.py` (create) | Parse a Postgres server log; emit `commit_p*` and `transaction_p*` as JSON. Carries its own `--self-test`. | 3 |
| `backend/datahub/tests/fixtures/pg_log_sample.log` (create) | 14-line hand-written fixture pinning the log grammar the parser must accept. | 3 |
| `docs/review/p0-report-2026-09-07.md` (modify §12 only) | Record the answer to R4. No §5 number changes. | 4 |

`pg_log_metrics.py` sits beside `baseline_load.py` and `lock_wait_sampler.sql` because it belongs to the same hand-run measurement set. Like them it is **not** wired into `verify.ps1` — the harness has no Python runner, and adding pytest would be new infrastructure this plan does not need.

---

## Task 1: Staging + RSA arm in the runtime configuration health check

**Files:**
- Modify: `src/AutoJMS.DataHub.Api/Health/RuntimeConfigurationHealthCheck.cs:48`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Health/RuntimeConfigurationHealthCheckTests.cs` (append one `[Fact]` after the existing `Check_is_unhealthy_when_staging_has_no_available_assertion_validator` at `:192-209`)

**Interfaces:**
- Consumes: `Auth.RsaLicenseAssertionValidator.HasKeyMaterial(DataHubRuntimeOptions options) → bool` (defined at `src/AutoJMS.DataHub.Api/Auth/RsaLicenseAssertionValidator.cs:39-40`, already used by the production arm at `:40`). Also the test helpers already in the test file: `Check(DataHubRuntimeOptions options, ManifestRootState manifestRoot = ManifestRootState.Writable) → RuntimeConfigurationHealthCheck` at `:21-24`, and `StubManifestRootProbe` at `:15-19`.
- Produces: nothing new. No new type, method, or signature. The public surface is unchanged — only which inputs yield `Healthy`.

**Why this exact shape:** `IdentityServiceCollectionExtensions.AddDataHubIdentity` selects the validator with three arms — staging-test-issuer, `else if (RsaLicenseAssertionValidator.HasKeyMaterial(options))`, `else` unavailable. That middle arm carries **no channel guard**, so it already serves the staging channel. The health check mirrors the first and third arms but never the second. The fix is to give the check the same middle arm, which turns `else` into `else if (!…HasKeyMaterial(options))` — one line.

- [ ] **Step 1: Write the failing test**

Append to `tests/AutoJMS.DataHub.Api.Tests/Health/RuntimeConfigurationHealthCheckTests.cs`, immediately after the closing brace of `Check_is_unhealthy_when_staging_has_no_available_assertion_validator`:

```csharp
    [Fact]
    public async Task Staging_is_healthy_with_signed_assertion_key_material_and_no_test_issuer()
    {
        // Regression: the branch block had no arm for staging + RSA key material, so a
        // staging host configured exactly like production — test issuer off, real public
        // key installed — fell through to `else`, reported the staging license verifier
        // missing, and /health/ready answered 503 on a host that was serving correctly.
        // AddDataHubIdentity wires RsaLicenseAssertionValidator for this same case; its
        // key-material arm carries no channel guard. This is the production bug described
        // at RuntimeConfigurationHealthCheck.cs:35-39, repeated on the staging side.
        var check = Check(new DataHubRuntimeOptions
        {
            Channel = "staging",
            EnvironmentName = "Staging",
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test",
            DeviceTokenSigningKey = new string('d', 32),
            EnrollmentPepper = new string('e', 32),
            AllowStagingTestIssuer = false,
            LicenseAssertionPublicKeyPem = "-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----",
            ManifestAdminToken = new string('m', 32)
        });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~Staging_is_healthy_with_signed_assertion_key_material_and_no_test_issuer"
```
Expected: **FAIL**, 1 failed. The assertion message shows `Expected: Healthy` / `Actual: Unhealthy`, because the description reads `Missing or invalid configuration: staging license verifier (enable the staging test issuer or install the signed-assertion verifier)`.

If it *passes* at this step, stop and report — the defect is not what this plan describes.

- [ ] **Step 3: Write the minimal implementation**

In `src/AutoJMS.DataHub.Api/Health/RuntimeConfigurationHealthCheck.cs`, replace lines 48-51:

```csharp
        else
        {
            missing.Add("staging license verifier (enable the staging test issuer or install the signed-assertion verifier)");
        }
```

with:

```csharp
        // AddDataHubIdentity's key-material arm has no channel guard, so a staging host with
        // a real public key gets the RSA validator and enrolls correctly. Reporting it
        // missing here took /health/ready to 503 and failed the deploy gate on a host that
        // was working — the same fault fixed for production above.
        else if (!Auth.RsaLicenseAssertionValidator.HasKeyMaterial(options))
        {
            missing.Add("staging license verifier (enable the staging test issuer or install the signed-assertion verifier)");
        }
```

Change nothing else in the file.

- [ ] **Step 4: Run the full health-check suite to verify it passes and nothing regressed**

Run:
```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationHealthCheckTests"
```
Expected: **all pass**, 0 failed — **14 results**: the 11 existing `[Fact]` cases, the 2 `[InlineData]` rows of the existing `[Theory]`, and the new `[Fact]`.

The neighbouring case `Check_is_unhealthy_when_staging_has_no_available_assertion_validator` sets **no** key material, so it must still report `Unhealthy`. If it now fails, the implementation is wrong — the new arm must be `!HasKeyMaterial`, not `HasKeyMaterial`.

- [ ] **Step 5: Build and run the full gate**

Run:
```bash
dotnet build ./AutoJMS.slnx -c Release
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Run:
```bash
powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1
```
Expected: `OVERALL: ✅ ALL GATES PASSED`.

- [ ] **Step 6: Commit**

```bash
git add src/AutoJMS.DataHub.Api/Health/RuntimeConfigurationHealthCheck.cs tests/AutoJMS.DataHub.Api.Tests/Health/RuntimeConfigurationHealthCheckTests.cs
git commit -m "fix(datahub): report staging healthy when the signed-assertion key is installed"
```

---

## Task 2: Keep per-machine scratch out of a public repo

**Files:**
- Modify: `.gitignore` (append one block after the dump block that ends at `:78`)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. No code depends on this.

**Context:** `_verify/` holds Velopack release-verification output — `hash-*.json` and `update-*.xml`, ~180 KB, written by release verification runs. `.agents/` holds CLI-installed skills plus orchestrator run scratch (`BRIEFING.md`, `handoff.md`, `explorer_r*_1/`). Neither is source, neither is ignored, and both sit in `git status` as untracked. `CLAUDE.md` refers to `.agents/skills/` as the location for CLI-installed skills — those are per-machine installs, not repo content, which is why ignoring the directory is correct rather than lossy.

- [ ] **Step 1: Verify both are currently unignored**

Run:
```bash
git check-ignore -v _verify/hash-beta2.json .agents/BRIEFING.md; echo "exit=$?"
```
Expected: no output, `exit=1` — neither path matches any rule.

- [ ] **Step 2: Add the ignore rules**

Append to `.gitignore`, immediately after the `*.sql.gz` line at `:78` and before the `# OS` block:

```gitignore

# Per-machine agent and release-verification scratch. Neither is source: `_verify/` holds
# Velopack hash manifests and update XML written by release verification, and `.agents/`
# holds CLI-installed skills plus orchestrator run scratch. Both were untracked AND
# unignored, so a single `git add .` would have committed them to a PUBLIC repo. Same
# failure mode as the dump rule above — the fix is keeping them out of the tree's index,
# not hoping the secret gate catches them.
_verify/
.agents/
```

- [ ] **Step 3: Verify both are now ignored**

Run:
```bash
git check-ignore -v _verify/hash-beta2.json .agents/BRIEFING.md; echo "exit=$?"
```
Expected: `exit=0` and two lines, each naming `.gitignore` and the pattern that matched — `_verify/` for the first path and `.agents/` for the second. Check the **pattern**, not the line number `git check-ignore -v` prints beside it: that number shifts whenever anything above it in the file changes, and a stale line-number citation is a defect this repo has already had to correct once.

- [ ] **Step 4: Verify nothing already tracked was hidden**

Run:
```bash
git ls-files _verify/ .agents/ | wc -l
```
Expected: `0`. A non-zero count means the rule would shadow tracked files — stop and report instead of committing.

Run:
```bash
git status --porcelain | grep -E "^\?\? (_verify|\.agents)/"; echo "exit=$?"
```
Expected: no output, `exit=1` — both directories have left the untracked list.

- [ ] **Step 5: Run the gate**

Run:
```bash
powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1
```
Expected: `OVERALL: ✅ ALL GATES PASSED`. Part 5's untracked-file count drops, because those directories are no longer scanned.

- [ ] **Step 6: Commit**

```bash
git add .gitignore
git commit -m "chore(gitignore): ignore agent and release-verification scratch directories"
```

---

## Task 3: Recover commit_p95 and transaction_p95 from a Postgres log

**Files:**
- Create: `backend/datahub/tests/pg_log_metrics.py`
- Create: `backend/datahub/tests/fixtures/pg_log_sample.log`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces, for Task 4:
  - CLI: `python backend/datahub/tests/pg_log_metrics.py --log <path>` → prints a JSON object to stdout, exit 0.
  - CLI: `python backend/datahub/tests/pg_log_metrics.py --self-test` → runs the built-in unittest suite, exit 0 on pass.
  - JSON keys: `commit_samples`, `commit_p50_ms`, `commit_p95_ms`, `commit_p99_ms`, `commit_max_ms`, `transaction_samples`, `transaction_p50_ms`, `transaction_p95_ms`, `transaction_p99_ms`, `transaction_max_ms`, `unpaired_commits`, `unclosed_transactions`, `notes`.
  - Python: `parse(lines) → (commits: list[float], transactions: list[float], unpaired_commits: int, unclosed: int)` and `report(commits, transactions, unpaired_commits, unclosed) → dict`.

**Context the implementer needs:**

Task 5 of P0 set `log_line_prefix = '%m [%p] %a '` and `log_min_duration_statement = 0`, so every completed statement was logged with a millisecond timestamp, the backend PID, and a `duration:` field. The P0 report §1.2 records that `commit_p95` is one grep away from that log and `transaction_p95` is the `BEGIN`→`COMMIT` cadence per PID. This script is those two derivations, made reproducible.

The percentile helper **must be copied verbatim** from `backend/datahub/tests/baseline_load.py:306-307`. It is nested inside `main()` there, so it cannot be imported — duplication is forced. Using a different percentile method would silently make these numbers incomparable with the published §5 baselines, which is the whole point of computing them.

There is no pytest, no `conftest.py`, and no `requirements.txt` in this repo. Use **only** the Python standard library, and put the tests inside the script behind `--self-test`.

- [ ] **Step 1: Write the fixture**

Create `backend/datahub/tests/fixtures/pg_log_sample.log` with exactly these 14 lines. It pins the grammar: two complete transactions on PID 1841, one on PID 1902 interleaved with them, one `COMMIT` on PID 1999 whose `BEGIN` fell off the front of the log, and one `BEGIN` on PID 2001 that never commits. It also carries the extended-query-protocol `execute <unnamed>:` form Npgsql produces, a non-`duration:` line, and a continuation line — all three must be ignored without crashing.

```
2026-09-07 09:15:02.100 UTC [1841] AutoJMS.DataHub.Api LOG:  duration: 0.041 ms  statement: BEGIN
2026-09-07 09:15:02.180 UTC [1902] AutoJMS.DataHub.Api LOG:  duration: 0.038 ms  statement: BEGIN
2026-09-07 09:15:02.240 UTC [1841] AutoJMS.DataHub.Api LOG:  duration: 1.204 ms  execute <unnamed>: INSERT INTO waybill_events (id) VALUES ($1)
2026-09-07 09:15:02.300 UTC [1841] AutoJMS.DataHub.Api LOG:  duration: 0.812 ms  statement: COMMIT
2026-09-07 09:15:02.355 UTC [1902] AutoJMS.DataHub.Api LOG:  duration: 2.400 ms  statement: COMMIT
2026-09-07 09:15:02.360 UTC [1999] AutoJMS.DataHub.Api LOG:  duration: 5.500 ms  statement: COMMIT
2026-09-07 09:15:02.400 UTC [1841] AutoJMS.DataHub.Api LOG:  duration: 0.045 ms  statement: BEGIN
2026-09-07 09:15:02.512 UTC [1841] AutoJMS.DataHub.Api LOG:  duration: 0.900 ms  statement: COMMIT
2026-09-07 09:15:02.600 UTC [2001] AutoJMS.DataHub.Api LOG:  duration: 0.040 ms  statement: BEGIN
2026-09-07 09:15:02.700 UTC [1841]  LOG:  duration: 0.310 ms  statement: SELECT 1
2026-09-07 09:15:02.800 UTC [1841] AutoJMS.DataHub.Api LOG:  checkpoint starting: time
2026-09-07 09:15:02.900 UTC [1841] AutoJMS.DataHub.Api LOG:  duration: 0.250 ms  parse <unnamed>: SELECT 2
2026-09-07 09:15:03.000 UTC [1841] AutoJMS.DataHub.Api STATEMENT:  SELECT 3
	continuation line with no prefix at all
```

Expected derivations from this fixture, which Step 3's tests assert:
- **4 COMMIT durations**: `0.812`, `2.400`, `5.500`, `0.900`
- **3 paired transactions**: PID 1841 `02.100 → 02.300` = **200.0 ms**; PID 1902 `02.180 → 02.355` = **175.0 ms**; PID 1841 again `02.400 → 02.512` = **112.0 ms**
- **1 unpaired commit** (PID 1999) and **1 unclosed transaction** (PID 2001)
- Line 10 has an empty `%a`, so the prefix collapses to two spaces — it must still parse. It is a `SELECT`, so it contributes to neither list.

- [ ] **Step 2: Write the script with its self-test**

Create `backend/datahub/tests/pg_log_metrics.py`:

```python
#!/usr/bin/env python3
"""Recover commit_p95 and transaction_p95 from a PostgreSQL server log.

Why this exists: docs/review/p0-report-2026-09-07.md records that metrics 2 and 3 of the
runbook's five were never computed for G7/G8, and that both were already sitting in the log
`log_min_duration_statement = 0` produced -- commit_p95 is one grep away, and
transaction_p95 is the BEGIN->COMMIT cadence per backend PID. This is those two
derivations, made reproducible and testable instead of typed once at a prompt.

Reads a log written with `log_line_prefix = '%m [%p] %a '` (the value P0's Task 5 set) and
reports:

  commit_*       -- the duration PostgreSQL itself reports for the COMMIT statement
  transaction_*  -- wall time from a PID's BEGIN line to its next COMMIT line

Read-only: it opens one file, connects to nothing, and writes only JSON on stdout.

  python pg_log_metrics.py --log /path/outside/the/repo/pg.log
  python pg_log_metrics.py --self-test

SECURITY: a `log_min_duration_statement = 0` capture contains every statement the API ran.
Keep the log file OUTSIDE this repository's working tree. `*.log` is gitignored, but the
secret gate's untracked-file pass still reads anything sitting in the tree.
"""

import argparse
import json
import re
import statistics
import sys
import unittest
from datetime import datetime

# `%m [%p] %a ` then PostgreSQL's own `LOG:  duration: N ms  <kind>: <sql>`.
# %a is the application_name and is empty for some backends, so the gap between the PID and
# LOG: is matched loosely. The `<kind>` is `statement` for the simple query protocol and
# `execute`/`parse`/`bind <name>` for the extended protocol Npgsql uses.
LINE_RE = re.compile(
    r"^(?P<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})"
    r".*?\[(?P<pid>\d+)\]"
    r".*?\bduration:\s(?P<ms>\d+\.\d+)\sms"
    r"(?:\s+(?:statement|execute[^:]*|parse[^:]*|bind[^:]*):\s*(?P<sql>.*))?$"
)

TS_FORMAT = "%Y-%m-%d %H:%M:%S.%f"


def pct(values, q):
    # Copied verbatim from baseline_load.py:306-307, where it is nested inside main() and so
    # cannot be imported. It must stay identical: a different percentile method would make
    # these numbers quietly incomparable with the published baselines in report section 5.
    # If one changes, change both.
    return round(statistics.quantiles(values, n=100)[q - 1], 1) if len(values) > 100 else None


def parse(lines):
    """Return (commit_durations_ms, transaction_spans_ms, unpaired_commits, unclosed)."""
    commits = []
    transactions = []
    open_txn = {}
    unpaired_commits = 0

    for line in lines:
        match = LINE_RE.match(line)
        if match is None:
            continue
        sql = (match.group("sql") or "").strip().rstrip(";").upper()
        if not sql:
            continue
        pid = match.group("pid")
        stamp = datetime.strptime(match.group("ts"), TS_FORMAT)

        if sql.startswith("BEGIN"):
            # A second BEGIN without a COMMIT means the first transaction's end is not in
            # this log; the later one wins and the earlier is counted as unclosed at the end.
            open_txn[pid] = stamp
        elif sql.startswith("COMMIT"):
            commits.append(float(match.group("ms")))
            began = open_txn.pop(pid, None)
            if began is None:
                unpaired_commits += 1
            else:
                transactions.append((stamp - began).total_seconds() * 1000.0)

    return commits, transactions, unpaired_commits, len(open_txn)


def report(commits, transactions, unpaired_commits, unclosed):
    return {
        "commit_samples": len(commits),
        "commit_p50_ms": pct(commits, 50),
        "commit_p95_ms": pct(commits, 95),
        "commit_p99_ms": pct(commits, 99),
        "commit_max_ms": round(max(commits), 1) if commits else None,
        "transaction_samples": len(transactions),
        "transaction_p50_ms": pct(transactions, 50),
        "transaction_p95_ms": pct(transactions, 95),
        "transaction_p99_ms": pct(transactions, 99),
        "transaction_max_ms": round(max(transactions), 1) if transactions else None,
        "unpaired_commits": unpaired_commits,
        "unclosed_transactions": unclosed,
        "notes": (
            "commit_* is the duration PostgreSQL reports for the COMMIT statement itself. "
            "transaction_* is the interval between a PID's BEGIN log line and its COMMIT log "
            "line; both timestamps mark statement COMPLETION, so the span excludes BEGIN's "
            "own duration and is a slight underestimate. Percentiles are None below 101 "
            "samples, matching baseline_load.py. unpaired_commits and unclosed_transactions "
            "count transactions straddling the ends of the log; a large value means the "
            "capture window clipped the run and the percentiles cover a biased subset."
        ),
    }


class SelfTest(unittest.TestCase):
    FIXTURE = "fixtures/pg_log_sample.log"

    def fixture_lines(self):
        import os
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), *self.FIXTURE.split("/"))
        with open(path, encoding="utf-8") as handle:
            return handle.readlines()

    def test_commit_durations_are_every_commit_and_only_commits(self):
        commits, _, _, _ = parse(self.fixture_lines())
        self.assertEqual([0.812, 2.400, 5.500, 0.900], commits)

    def test_transaction_spans_pair_begin_to_commit_per_pid(self):
        _, transactions, _, _ = parse(self.fixture_lines())
        # Interleaved PIDs must not cross-pair: 1841's first COMMIT closes 1841's BEGIN,
        # not 1902's, even though 1902 began in between.
        self.assertEqual([200.0, 175.0, 112.0], [round(t, 1) for t in transactions])

    def test_transactions_straddling_the_log_edges_are_counted_not_guessed(self):
        _, _, unpaired, unclosed = parse(self.fixture_lines())
        self.assertEqual(1, unpaired)   # PID 1999 commits, its BEGIN predates the log
        self.assertEqual(1, unclosed)   # PID 2001 begins, its COMMIT postdates the log

    def test_non_statement_lines_are_ignored_without_crashing(self):
        # checkpoint, STATEMENT:, a bare continuation line, and an empty application_name.
        commits, transactions, _, _ = parse(self.fixture_lines())
        self.assertEqual(4, len(commits))
        self.assertEqual(3, len(transactions))

    def test_percentiles_are_none_below_the_sample_floor(self):
        self.assertIsNone(pct([1.0, 2.0, 3.0], 95))

    def test_percentiles_match_the_baseline_formula_above_the_floor(self):
        # Fixed expected values, not a re-derivation: asserting round(quantiles(...)[94], 1)
        # would compare this function against its own body and pass for any formula. 201
        # evenly spaced points under the exclusive method give p50 = 101.0 and p95 = 191.9.
        # If baseline_load.py's pct ever stops producing these, the two have diverged and
        # the numbers are no longer comparable with report section 5.
        values = [float(n) for n in range(1, 202)]
        self.assertEqual(101.0, pct(values, 50))
        self.assertEqual(191.9, pct(values, 95))

    def test_report_is_json_serialisable_on_an_empty_log(self):
        payload = report([], [], 0, 0)
        self.assertIsNone(payload["commit_max_ms"])
        self.assertEqual(0, payload["commit_samples"])
        json.dumps(payload)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--log", help="path to a PostgreSQL server log, OUTSIDE this repo")
    parser.add_argument("--self-test", action="store_true", help="run the built-in tests")
    args = parser.parse_args()

    if args.self_test:
        suite = unittest.TestLoader().loadTestsFromTestCase(SelfTest)
        result = unittest.TextTestRunner(verbosity=2).run(suite)
        sys.exit(0 if result.wasSuccessful() else 1)

    if not args.log:
        parser.error("--log is required unless --self-test is given")

    with open(args.log, encoding="utf-8", errors="replace") as handle:
        commits, transactions, unpaired, unclosed = parse(handle)

    print(json.dumps(report(commits, transactions, unpaired, unclosed), indent=2))


if __name__ == "__main__":
    main()
```

- [ ] **Step 3: Run the self-test to verify it passes**

Run:
```bash
python backend/datahub/tests/pg_log_metrics.py --self-test
```
Expected: `Ran 7 tests`, `OK`, exit 0.

If `test_transaction_spans_pair_begin_to_commit_per_pid` fails with `[200.0, 175.0]`, the `open_txn.pop` is keyed wrongly and PID 1841's second transaction was lost. If it fails with four spans, PID 1999's unpaired COMMIT is being paired with something.

- [ ] **Step 4: Verify the tool runs end to end on the fixture**

Run:
```bash
python backend/datahub/tests/pg_log_metrics.py --log backend/datahub/tests/fixtures/pg_log_sample.log
```
Expected: JSON on stdout with `"commit_samples": 4`, `"transaction_samples": 3`, `"commit_max_ms": 5.5`, `"transaction_max_ms": 200.0`, `"unpaired_commits": 1`, `"unclosed_transactions": 1`, and every `*_p50_ms`/`*_p95_ms`/`*_p99_ms` equal to `null` (4 and 3 samples are both below the 101-sample floor). Exit 0.

That `null` is the correct answer, not a bug: it is the same floor `baseline_load.py` applies.

- [ ] **Step 5: Confirm the gate still passes with a new `.py` and a new `.log` fixture in the tree**

Run:
```bash
powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1
```
Expected: `OVERALL: ✅ ALL GATES PASSED`.

`.py` is not in `$sourceExtensions`, so the pattern pass skips the script — the same treatment `baseline_load.py` already gets. The fixture is named `.log`, which `.gitignore:15` ignores as `*.log`, so **it will not be staged by the command in Step 6**. Verify that explicitly before committing:

```bash
git check-ignore -v backend/datahub/tests/fixtures/pg_log_sample.log; echo "exit=$?"
```
Expected: `exit=0`, matching `.gitignore:15:*.log`. Because the fixture must be tracked for the self-test to work on a fresh clone, force-add it in Step 6 — this is the one place in this plan where `-f` is correct, and it is safe because the file is 14 hand-written lines containing no real data.

- [ ] **Step 6: Commit**

```bash
git add backend/datahub/tests/pg_log_metrics.py
git add -f backend/datahub/tests/fixtures/pg_log_sample.log
git commit -m "test(datahub): derive commit and transaction percentiles from the postgres log"
```

Then confirm the fixture really is tracked:
```bash
git ls-files backend/datahub/tests/fixtures/
```
Expected: `backend/datahub/tests/fixtures/pg_log_sample.log`.

---

## Task 4: Answer whether the P0 log survives, and record the answer

**Files:**
- Modify: `docs/review/p0-report-2026-09-07.md` §12 only (the `Điểm chưa xác nhận` list beginning at `:899`)

**Interfaces:**
- Consumes from Task 3: `python backend/datahub/tests/pg_log_metrics.py --log <path>` and its JSON keys.
- Produces: nothing consumed by later tasks. This is the last task.

**Why this matters more than its size suggests:** §13 asks the Owner to choose between accepting an incomplete baseline (A), a new measurement programme (B), and re-running the same four tests with better instruments (C). Nobody has checked whether the log that would make two of the three missing metrics recoverable **from the official runs** still exists. If it does, the gap narrows without re-running anything, and the Owner's choice gets cheaper. If it does not, option C becomes strictly necessary for a complete baseline. Either answer is worth having before the Owner decides.

**Read-only.** Nothing in this task writes to the VPS, runs load, changes a setting, or touches a container. Do not `docker compose restart`, do not `ALTER SYSTEM`, do not re-enable logging.

- [ ] **Step 1: Confirm staging is idle and untouched before probing**

Run:
```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging ps --format "{{.Service}} {{.State}}"'
```
Expected: three lines — `api running`, `caddy running`, `postgres running`. `caddy` never reports healthy; that is R8, not a fault.

If any service is not running, stop and report. Do not start anything.

- [ ] **Step 2: Count surviving `duration:` lines from the measurement window**

Run:
```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs postgres --since 2026-09-07T00:00:00 2>/dev/null | grep -c "duration:"'
```
Expected: a single integer. Only the count comes back — no statement text, so nothing sensitive crosses the wire.

**Decision rule, applied exactly:**
- **`0`** → the log did not survive (Docker's json-file log rotated, or the container was recreated during the Task 6 reset). Go to Step 5, branch NO.
- **`> 0`** → some of the window survives. Go to Step 3.

- [ ] **Step 3 (only if Step 2 returned > 0): Extract the log to a path outside the repo**

Run:
```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && export VPS="$VPS_IP" SSHK="$VPS_KEY" SSHU="$VPS_USER" && mkdir -p "$HOME/autojms-p0-logs" && ssh -i "$SSHK" "$SSHU@$VPS" 'cd ~/AutoJMS/backend/datahub && docker compose --env-file .env.staging logs postgres --since 2026-09-07T00:00:00 --no-log-prefix 2>/dev/null' > "$HOME/autojms-p0-logs/pg-2026-09-07.log" && wc -l "$HOME/autojms-p0-logs/pg-2026-09-07.log"
```

`$HOME/autojms-p0-logs/` is **outside** the repository working tree. This is deliberate: a `log_min_duration_statement = 0` capture contains every statement the API executed, and it must not sit where the secret gate's untracked pass or a careless `git add` can reach it. Do not move it into the repo, and do not commit it.

- [ ] **Step 4 (only if Step 3 ran): Compute the two missing metrics**

Run:
```bash
python backend/datahub/tests/pg_log_metrics.py --log "$HOME/autojms-p0-logs/pg-2026-09-07.log"
```
Expected: the JSON object from Task 3. Record `commit_p50_ms`/`commit_p95_ms`/`commit_p99_ms`/`commit_max_ms`, the matching `transaction_*` values, `commit_samples`, `transaction_samples`, `unpaired_commits`, and `unclosed_transactions`.

Read the result honestly before writing it down:
- If `commit_samples` ≤ 100, the percentiles are `null` and the log is **too short to answer the question** — treat this as the NO branch, not as a partial success.
- If `unpaired_commits + unclosed_transactions` is a large fraction of `transaction_samples`, the capture window clipped the run; say so, and do not present the percentiles as covering the whole run.
- The log covers whatever `docker compose logs` still holds, which is **not** necessarily the four official runs. Do not claim these numbers belong to a specific named run unless the timestamps line up with the run windows recorded in report §5.

- [ ] **Step 5: Record the answer in §12 — one bullet, either branch**

**Append one new bullet to the end of the §12 list**, after the existing final bullet (the one beginning `- **Bài diễn tập restore vào instance TẠM`) and before the `---` separator that closes the section. Modify no existing bullet, and change nothing outside §12.

**Branch YES** (Step 4 produced percentiles over more than 100 samples):

```markdown
- **`commit_p95` và `transaction_p95` — đã khôi phục được một phần từ log còn sót (kiểm
  2026-09-08).** Log `log_min_duration_statement = 0` của 07/09 **vẫn còn** trong
  `docker compose logs postgres`, và `backend/datahub/tests/pg_log_metrics.py` đọc được từ đó:
  `commit_p95 = <giá trị> ms` (n=<commit_samples>) và `transaction_p95 = <giá trị> ms`
  (n=<transaction_samples>, <unpaired_commits> commit không ghép được, <unclosed_transactions>
  transaction chưa đóng). **Đây KHÔNG phải là metric của bốn lượt chính thức ở §5** trừ khi mốc
  thời gian trùng khớp — log này là những gì Docker còn giữ, không phải một lượt đo có tên. Ghi ở
  đây như **bằng chứng bổ sung**, không thay đổi phán quyết G7/G8 và không thay bất kỳ con số nào
  ở §5. Việc chọn A/B/C ở §13 vẫn là quyết định của Owner.
```

**Branch NO** (Step 2 returned 0, or the sample count was at or below 100):

```markdown
- **`commit_p95` và `transaction_p95` — xác nhận KHÔNG khôi phục được (kiểm 2026-09-08).** Log
  `log_min_duration_statement = 0` của 07/09 đã không còn trong `docker compose logs postgres`
  (<số dòng> dòng `duration:` còn lại), nên hai metric này **không** suy ra được từ bằng chứng
  hiện có. §1.2 nói đúng rằng chúng *từng* nằm sẵn trong log đó; điều chưa nói là log ấy có thời
  hạn. **Hệ quả cho §13:** lựa chọn **A** vào P1 với một baseline thiếu hai metric mà **không còn
  đường nào lấy lại**, và **C** là con đường duy nhất còn lại để có baseline đầy đủ. Dụng cụ đã sẵn
  sàng: `backend/datahub/tests/pg_log_metrics.py`.
```

Fill in every `<...>` with the real value. Leaving a placeholder in the report is the exact failure the report exists to prevent.

- [ ] **Step 6: Verify no infra identifier entered the report**

Run:
```bash
cd "D:/v1.2605.2(new-test)" && eval "$(grep -E '^(VPS_IP|VPS_USER|VPS_KEY)=' backend/vps/VPS_STATUS_REPORT.private.md)" && git grep -q -- "$VPS_IP" docs/review/p0-report-2026-09-07.md; echo "ip_hit=$?"; git grep -qE "(ssh -i|IdentityFile|/home/|\.pem)" docs/review/p0-report-2026-09-07.md; echo "path_hit=$?"
```
Expected: `ip_hit=1` and `path_hit=1` — exit 1 means `git grep` found nothing, which is the pass condition.

Use `git grep -q` and read its exit code directly. Do **not** pipe through `head` and read `$?` — that captures `head`'s status, not `git grep`'s, and reports a clean result regardless of what was found.

- [ ] **Step 7: Run the gate**

Run:
```bash
powershell -ExecutionPolicy Bypass -File ./eng/harness/verify.ps1
```
Expected: `OVERALL: ✅ ALL GATES PASSED`.

- [ ] **Step 8: Commit**

```bash
git add docs/review/p0-report-2026-09-07.md
git commit -m "docs(review): record whether the P0 postgres log still yields the two missing metrics"
```

- [ ] **Step 9: Delete the extracted log**

If Step 3 ran:
```bash
rm -f "$HOME/autojms-p0-logs/pg-2026-09-07.log" && ls -la "$HOME/autojms-p0-logs/"
```
Expected: the directory is empty. The metrics are in the report; the raw statement log has no further use and should not linger on disk.

---

## After all four tasks

- [ ] Push, once and explicitly:
  ```bash
  git push origin main && git log --oneline -4 && git status --porcelain
  ```
- [ ] Produce the `CLAUDE.md` Final Report — all ten numbered sections.
- [ ] Leave P1 locked. Nothing in this plan signs OD-2, OD-6, or the §13 A/B/C choice.

---

## Self-review

**Spec coverage.** R1→Task 1, R2→Task 2, R3→Task 3, R4→Task 4. R5, R6, R7, R10 are Owner decisions and are named as out of scope rather than silently dropped. R8 is blocked by the Protected Files rule and says so. R9 is accepted by design. Every row of the risk register resolves to a task or to an explicit non-action with a stated reason.

**Placeholder scan.** No "TBD", no "add error handling", no "similar to Task N". Every code step carries the literal text to write. The one intentional fill-in is Task 4 Step 5's `<...>` values, which cannot be known before Step 4 runs and which Step 5 explicitly forbids leaving unfilled.

**Type consistency.** `parse` returns a 4-tuple in Task 3 Step 2 and is unpacked as a 4-tuple in Step 2's `main()` and in all three self-tests. `report` takes those same four values in the same order in both call sites. `pct(values, q)` has one signature. `HasKeyMaterial(options)` matches `RsaLicenseAssertionValidator.cs:39`. The test helper `Check(...)` matches the existing signature at the test file's `:21-24`.

**Known coupling, accepted deliberately.** `pct` is duplicated between `baseline_load.py:306-307` and `pg_log_metrics.py`. The source is a closure inside `main()` and cannot be imported without restructuring a file that produced published numbers — a change with more risk than the duplication. The duplicate carries a comment naming its source and the requirement to change both together.

**One assumption worth stating.** Task 4 Step 2 assumes the Postgres container logs to stderr under Docker's default `json-file` driver, which is how `postgres:16-alpine` behaves with no `logging:` override. `docker-compose.yml` is a Protected File and was not opened to confirm there is no override. If Step 2 errors rather than returning an integer, that assumption is wrong — stop and report instead of hunting for the log elsewhere.
