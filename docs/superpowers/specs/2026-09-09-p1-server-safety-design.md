# P1 Server Safety Design

> Status: owner-approved 2026-09-09. This document is the implementation source of truth
> for phase P1 of the DataHub streaming programme. It records the owner signatures that
> unlock P1 and the two contract deviations the owner adjudicated after the contract was
> checked against the real code.
>
> Governing contract: `docs/review/streaming-v4.6-contract.vi.md` (§19.3 schema delta,
> §20 backup gate, §22 rollback asymmetry, §24 phase gates, §26 do-not-rewrite list,
> §27 test assignment, §29 final constraints). Task spec: `docs/review/p1-prompt-proposal.vi.md`.
> Both are currently untracked; see "Documentation debt" below.

## Goal

Give the server the safety machinery the later streaming phases depend on — terminal
state, an anti-resurrection ledger, and an ingest time horizon — without changing what
the system does today. Every new code path in P1 is reachable but inert: with
`jms_event_policies` empty no scan type is terminal, so no projection is ever marked
terminal, no tombstone is ever written, and no purge is ever eligible.

## Owner decisions signed for this phase

These signatures exist nowhere else on disk. They are the gate that opened P1.

| ID | Decision | Note |
|---|---|---|
| **OD-1** | Deferred past P4 (restates OD-A = A3, signed 2026-09-08). P1 runs with `jms_event_policies` **empty**, fail-closed. | No scan type code may be seeded — not even temporarily for a test. |
| **OD-2** | Ingest horizon **45 days**, future skew **15 minutes**. Event/dedupe retention 60 days unchanged. Terminal purge 90 days. Tombstone retention ≥ 2 years. | Deviation from §514's proposed 5-minute skew, accepted: the binding invariant is `ingest < event_retention` (45 < 60 ✅) and skew is not part of it. |
| **OD-6** | **B — server observed time.** `terminal_at` is stamped by the server when it observes the terminal event, not by `source_event_at`. | Consequence: test `AT-TERM-LATE` is **not required**; §27 gates it on OD-6 = A. |
| **G7/G8 p50** | Accept the measured upper bound ≤ 279.4 ms together with the clean zero at 1 worker/site, in place of a recovered `counter_lock_wait` p50. | Option C recovered `transaction_p95` and `commit_p95` but could not recover the p50; that is instrument physics, not a missing run. |

### Deviations adjudicated 2026-09-09

**D-1 — reopen route moves under the admin prefix.**
`AdminAuthenticationMiddleware` returns early for any path outside
`AdminAuthenticationContext.PathPrefix = "/api/v1/admin"`
(`src/AutoJMS.DataHub.Api/Auth/AdminAuthenticationMiddleware.cs:35`, prefix at `:13`) and
therefore never sets the marker that `IsAdminAuthenticated()` reads. The contract's route
`POST /api/v1/sites/{siteId}/waybills/{waybillNo}/reopen` sits outside that prefix, so as
specified the endpoint would answer 401 to every caller including a correctly
authenticated operator. The route becomes:

```
POST /api/v1/admin/sites/{siteId}/waybills/{waybillNo}/reopen
```

The security middleware is not modified. The endpoint inherits the per-IP admin rate
limiter and the constant-time token comparison unchanged. The contract's actual
requirement — operator token, not `DeviceCapability` — is satisfied exactly; only the URL
string differs.

**D-2 — the new tombstone gets its own name.**
`DataHubRuntimeOptions.TombstoneRetention` (`:85`, default 90 days, bounded 30–365, env
`DATAHUB_TOMBSTONE_RETENTION_DAYS`) already exists and means something else: the lifetime
of the `delete` marker that retention publishes into `dashboard_changes` before removing a
projection (`src/AutoJMS.DataHub.Api/Infrastructure/RetentionRepository.cs:129-134`), which
the client consumes in `DataHubClient.ProjectChangeItems`. That is a change-feed deletion
notice, not the anti-resurrection ledger of migration 008. OD-2's ≥ 2 years (730 days) also
exceeds that property's 365-day maximum.

The anti-resurrection retention is therefore a separate setting named
`WaybillTombstoneRetention` / `DATAHUB_WAYBILL_TOMBSTONE_RETENTION_DAYS`, default 730,
bounded 730–3650. `TombstoneRetention` is left untouched.

**The property is not added in P1.** Nothing in P1 reads it: P1 never writes a tombstone
row, because rows are written by the retention purge, which is P6. Adding an unread
setting now would be dead configuration. The name is reserved here so P6 cannot reuse
`TombstoneRetention` by accident.

## Scope

| Block | Content | Active in P1? |
|---|---|---|
| **A** | Migrations `007_event_metadata.sql`, `008_terminal_tombstone.sql`, `009_terminal_index_notx.sql` | Written; **not applied** — see "Sequencing" |
| **B** | Fail-closed terminal policy | Present, inert (empty policy table) |
| **C** | Minimal ingest delta in `IngestRepository` | Horizon active; terminal paths inert |
| **D** | Reopen endpoint | Active |

### Out of scope

Redis, Dashboard Epoch, any client-side change, rewriting snapshot / change-feed /
idempotency / retention, changing the purge predicate (P6), seeding
`retention_policies('waybill_projections')`, historical replay, `fingerprint_policies`,
the advisory-lock function, and refactoring `IChangeSequenceAllocator`. The §26
do-not-rewrite list holds in full: the 1-transaction all-or-nothing bulk of ≤ 200 items,
full idempotency, 3-checkpoint lease fencing, `ON CONFLICT DO NOTHING RETURNING id`
dedupe, `change_seq` allocation under `FOR UPDATE`, `RepeatableRead` snapshots, and
prefix-only pruning all stay exactly as they are.

## A — Schema delta

Migrations 001–006 exist, so 007–009 are free. The delta is exactly §19.3, nothing more.

**`007_event_metadata.sql`** adds to `waybill_scan_events`: `event_kind`,
`reducer_version`, `normalizer_version`, `source_schema_version`.

**`008_terminal_tombstone.sql`** adds to `waybill_projections`: `is_terminal`,
`terminal_at`, `terminal_state_code`, `last_change_seq`; creates `waybill_tombstones` with
primary key `(site_id, waybill_no)`.

**`009_terminal_index_notx.sql`** creates
`ix_waybill_projections_terminal_retention ... WHERE is_terminal = true` with
`CREATE INDEX CONCURRENTLY`, run outside a transaction.

Forbidden in A: creating `fingerprint_policies` or `get_waybill_advisory_lock_key`,
touching `site_change_counters`, and altering `event_occurred_at`, `ingested_at`,
`fingerprint_version` (already present as `smallint NOT NULL DEFAULT 1`,
`001_core.sql:58`), or the `jms_event_policies` primary key (already
`(reducer_version, scan_type_code)`, `001_core.sql:122-130`).

Two preflight rules bind every migration in this set:

1. **Per-object preflight (§19.1).** Check all seven object properties — table, column,
   type, nullability, default, index, constraint. Any mismatch stops the run and is
   reported per object. Skipping an entire migration because one object already exists is
   forbidden.
2. **Invalid-index trap (§19.2).** `IF NOT EXISTS` does not prove an index is usable.
   After 009, query `pg_index.indisvalid`; if false, `DROP` and retry once, then stop and
   report.

## B — Terminal policy

`TerminalPolicy` is a pure function over the loaded policy set: a scan type is terminal
only if `jms_event_policies` says so. The table is empty, so nothing is terminal,
`is_terminal` stays `false` for every row, no tombstone is written, and no purge becomes
eligible. Seeding any code — including 9001, 9002, 9004, and including "just for a test" —
is forbidden until OD-1 is signed.

## C — Ingest delta

Four insertions into `IngestRepository.IngestAsync`. Everything else in the method is
untouched.

**C1 — horizon guardrail.** Runs before `OpenConnectionAsync`
(`IngestRepository.cs:42`), so a violation costs no transaction. It parses each item's
scan time independently and decides only the horizon question; items whose scan time fails
to parse are left to the existing in-loop path at `:152-157` so parse-error semantics stay
byte-identical. A violation is a hard error for the whole batch: HTTP 422, no partial
acceptance. The problem code is a new one, `INGEST_HORIZON_VIOLATION`, rather than the
existing `VALIDATION_FAILED` — an operator needs to tell "this device's clock or backlog is
outside the window" apart from "this payload is malformed", and they are fixed by different
actions. No client change is needed: unknown codes are already handled generically, and
client work is out of scope for P1. Time comes from the injected
`TimeProvider` (already registered in `AddDataHubIdentity`), which keeps the decision
testable without a clock hack.

**C2 — anti-resurrection, in this exact order.** Inside the item loop, *after*
`InsertEventAsync` succeeds (`:165-172`), read the tombstone and `is_terminal`. The event
is inserted first on purpose: dedupe and history must stay complete regardless of the
block decision. If blocked: count into `terminalLocked`, `continue` — no projection
mutation, no version bump, no `change_seq` allocation, no `dashboard_changes` row, and
**no rollback of the batch**.

**C3 — `last_change_seq`.** Set only for rows this transaction actually writes, inside
`UpsertProjectionAsync` (`:218`). No backfill of existing rows, ever.

**C4 — terminal lifecycle.** When the reducer produces a terminal code, set `is_terminal`,
`terminal_at`, `terminal_state_code`. `terminal_at` is the server's observed time (OD-6 =
B). Inert in P1.

**Response contract.** `IngestResponse` gains `TerminalLockedItems` as the final
positional parameter with default `0`:

```csharp
public sealed record IngestResponse(
    Guid SiteId,
    int AcceptedItems,
    int DuplicateItems,
    int ChangedProjections,
    bool Replayed,
    long? FirstChangeSeq,
    long? LastChangeSeq,
    int TerminalLockedItems = 0);
```

The default matters: responses already stored in `idempotency_records.response` were
serialized without the field, and a replay must still deserialize. Those requests predate
the feature, so replaying them as `terminalLocked = 0` is correct rather than merely
convenient. The audit payload at `:236` gains the same counter.

`ingested_at` remains untouched. It is a DB `DEFAULT now()` — transaction start time,
identical for all 200 items of a bulk request — so it cannot serve as a reducer tie-break,
and the API never writes it.

## D — Reopen endpoint

```
POST /api/v1/admin/sites/{siteId}/waybills/{waybillNo}/reopen
```

Operator token via the existing admin middleware; the handler re-checks
`IsAdminAuthenticated()` so it stays closed if the pipeline is ever reordered, matching
`ManifestEndpoints.cs:69`. `Idempotency-Key` is mandatory, 8–128 characters, and reuses
the existing `idempotency_records` table rather than a new one:

- same key + same body ⇒ exact replay of the stored response;
- same key + different body ⇒ 409 `IDEMPOTENCY_KEY_REUSED`;
- a duplicate retry must not bump the version, allocate a `change_seq`, or add a
  `dashboard_changes` row.

Status semantics: 404 when the waybill has no projection, 410 when it was purged and only
a tombstone remains, 409 on idempotency key reuse. The audit actor is taken from the
authenticated principal only — never from the request body.

## Configuration

Two settings are added, both used by C1:

| Property | Env | Default | Bounds |
|---|---|---|---|
| `IngestHorizon` | `DATAHUB_INGEST_HORIZON_DAYS` | 45 | 1–59 |
| `IngestFutureSkew` | `DATAHUB_INGEST_FUTURE_SKEW_SECONDS` | 900 | 0–3600 |

The upper bound of 45 exists to keep the invariant below the 60-day event retention;
see the next section for why a static bound is not sufficient on its own.

## Error handling and the horizon invariant

The invariant is `ingest_horizon < event_retention`. Event retention is **not** a setting:
it is a `retention_policies` row keyed on `table_name = 'waybill_scan_events'`, resolved
**per site with a global fallback**, and it may be NULL
(`RetentionRepository.cs:95-113`). A NULL policy means events are never deleted, so the
invariant holds trivially for any horizon.

That shape forces the check into two places:

1. **Startup.** Read every existing `waybill_scan_events` policy, global and per-site. A
   violation is any policy whose `delete_after` is non-NULL and `<= IngestHorizon`; rows
   with a NULL `delete_after` are not violations. One violation refuses the boot — this is
   the fail-fast the contract requires. If the database cannot be read, the host does
   **not** crash-loop; reporting an unreachable database is already
   `RuntimeConfigurationHealthCheck`'s job, and a boot refusal there would turn a transient
   outage into an outage that needs a human.
2. **Health check.** The same evaluation runs in `RuntimeConfigurationHealthCheck` and
   reports **Degraded**, not Unhealthy: the host is still serving every read and write
   correctly, and taking `/health/ready` to 503 over a retention policy would remove a
   working host from rotation. A startup-only check cannot see a bad per-site policy
   inserted an hour later, and that policy would silently make the horizon admit events the
   retention pass is already deleting.

## Testing

§27 of the contract governs the phase assignment, not the P1 prompt's list. P1 runs:

`AT-T1`, `AT-T2`, `AT-T3`, `AT-R1`–`AT-R6`, `AT-CS1`, `AT-LS3`, `AT-CK3`, plus horizon
tests. The horizon tests carry the prompt's names `AT-H1`, `AT-H2`, `AT-H4`: §24 assigns
the horizon guardrail to P1, but §27 never issued IDs for them.

Explicitly not in P1:

- `AT-LS1`, `AT-LS2` → **P2.** They need P2's `/waybills` read surface and ETag; running
  them in P1 would breach the §24 phase gate.
- `AT-CK1`, `AT-CK2` → **P0 debt.** They were never run and are not part of G1–G9.
- `AT-TERM-LATE` → **not required**, because OD-6 = B.

`IngestHorizonPolicy` and `TerminalPolicy` are extracted as pure types precisely so their
tests need no database. The repository is not a seam — `PostgresDataSource` is sealed and
builds a real `NpgsqlDataSource` — so tests that must touch the database carry
`RequiresDataHubDatabaseFact` and skip when `DATAHUB_TEST_CONNECTION_STRING` is unset, the
same pattern as the two enrollment tests. No CI workflow provisions PostgreSQL, so those
tests are a local and staging guard, not a build gate; the pure-logic tests are the part
that gates the build.

## File structure

**Create**

- `backend/datahub/migrations/007_event_metadata.sql`
- `backend/datahub/migrations/008_terminal_tombstone.sql`
- `backend/datahub/migrations/009_terminal_index_notx.sql`
- `src/AutoJMS.DataHub.Api/Infrastructure/IngestHorizonPolicy.cs`
- `src/AutoJMS.DataHub.Api/Infrastructure/TerminalPolicy.cs`
- `src/AutoJMS.DataHub.Api/Infrastructure/ReopenRepository.cs`
- `src/AutoJMS.DataHub.Api/Endpoints/ReopenEndpoints.cs`

**Modify**

- `src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs` — C1–C4
- `src/AutoJMS.DataHub.Api/Infrastructure/IngestContracts.cs` — `TerminalLockedItems`
- `src/AutoJMS.DataHub.Api/Configuration/DataHubRuntimeOptions.cs` — two horizon settings
- `src/AutoJMS.DataHub.Api/Health/RuntimeConfigurationHealthCheck.cs` — horizon invariant
- `src/AutoJMS.DataHub.Api/Program.cs` — route registration and the startup check

`src/AutoJMS.DataHub.Api/Program.cs` is **not** a protected file. The protected entry is
`src/AutoJMS/Program.cs`, the WinForms host — a different path.

## Sequencing and gates

Contract §20 is a prohibition, not advice: "⛔ CẤM migration trước khi backup được verify."
G4 (backup restore) is still FAIL. So the phase splits:

1. **Now.** Write A + B + C + D and their tests. Build Release, run `eng/harness/verify.ps1`,
   commit, push. Migration files exist on disk; none is applied anywhere.
2. **Gated.** Apply 007/008/009 to staging only after a backup → restore into a temporary
   instance passes. That run also closes G4.

Phase exit (§18): Build Release PASS, P1 tests PASS, throughput not worse than the
baseline.

## Rollback

P1 adds **no runtime feature flag.** The empty `jms_event_policies` table is already the
off switch that §22's flag stands for: with no terminal codes, the terminal and tombstone
paths cannot execute, so there is nothing to disable. Adding a second, redundant switch
would be dead configuration.

The rollback lever for step 1 is therefore reverting the commits — safe in full, because
no migration has been applied and no terminal or tombstone state can exist. That stays
true through step 2, since applying the migrations only creates columns and an empty
table. Once terminal or tombstone state does exist — in a later phase, after OD-1 is
signed — §22 forbids deleting or reversing the schema, and rollback stops being symmetric.

## Stop rules

Halt and report rather than guess if: the real schema differs from what A expects;
`retention_policies('waybill_projections')` returns more than zero rows; the contract
contradicts the real code; a protected file is needed without permission; or any owner
decision value would have to be guessed.

Two contract-versus-code contradictions were found before implementation and adjudicated
by the owner above (D-1, D-2). No others were found in P1's blast radius: migration
numbering, `fingerprint_version`, the `jms_event_policies` primary key, and every
`IngestRepository` helper named by §26 were each checked against the code.

## Documentation debt

The governing documents for this programme — `streaming-v4.6-contract.vi.md`,
`p1-prompt-proposal.vi.md`, `p0-execution-runbook.vi.md`, `walkthrough-v4.6.md` — are
untracked. Only `p0-report-2026-09-07.md` is in git, and it cites the others by relative
path, so those links resolve to nothing in a fresh clone and `git clean -fdx` erases the
targets. Tracking them needs a public-repo infra-leak scan first, since the repository is
public. Not adjudicated; carried as an open item.
