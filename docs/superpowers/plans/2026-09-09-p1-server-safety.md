# P1 Server Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the DataHub server the terminal-state, anti-resurrection and ingest-horizon machinery the later streaming phases depend on, with every new path inert in production.

**Architecture:** Three additive migrations declare the schema; two pure policy types (`IngestHorizonPolicy`, `TerminalPolicy`) hold every decision so the tests need no database; four small insertions into `IngestRepository.IngestAsync` wire those decisions in without touching the transaction shape; one new admin endpoint reverses a terminal mark. The terminal set is empty by construction, so the terminal and tombstone paths compile, ship, and never execute.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, Npgsql (raw SQL, no ORM), PostgreSQL 16, xUnit.

**Spec:** [docs/superpowers/specs/2026-09-09-p1-server-safety-design.md](../specs/2026-09-09-p1-server-safety-design.md) — read it before Task 1. It carries the owner signatures (OD-1, OD-2, OD-6) and the three adjudicated deviations (D-1, D-2, D-3) this plan implements.

## Global Constraints

- **Branch:** `main`. Start with `git switch main && git pull --ff-only origin main`. Never force push, never rewrite history.
- **Never `git add .`** and never `git add` a directory. Stage explicit file paths only. Never delete files.
- **Build gate before any push:** `dotnet build .\AutoJMS.slnx -c Release` must pass, then `powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1`.
- **The repository is PUBLIC.** No VPS IP, account name, SSH key path, container ID, or firewall threshold in any tracked file. The hostname `dev.jmsauto.online` is permitted. Never commit `.env`, service-account keys, `*.pfx`, `*.pem`, or any token file. Mask tokens in logs as `first4...last4`.
- **Minimal Edit Rule.** Smallest change that delivers the task. Do not refactor `IngestAsync`, do not reformat untouched lines, match the surrounding comment density and naming.
- **§26 do-not-rewrite list is binding.** Leave exactly as they are: the 1-transaction all-or-nothing bulk of ≤ 200 items, full idempotency, 3-checkpoint lease fencing, `ON CONFLICT DO NOTHING RETURNING id` dedupe, `change_seq` allocation under `FOR UPDATE`, `RepeatableRead` snapshots, prefix-only pruning, and the sleeping projection purge.
- **OD-1 — fail closed.** No scan type code may be classified terminal anywhere in the schema, a migration, `JmsEventKind`, or production DI. Not 9001, not 9002, not 9004, and not "just for a test". A test may construct `TerminalPolicy` with codes of its own because nothing outside that test's object graph sees them.
- **OD-2 values, exact:** ingest horizon **45 days** (bounds 1–59), future skew **900 seconds** (bounds 0–3600). Event/dedupe retention stays 60 days. The binding invariant is `ingest_horizon < event_retention`.
- **OD-6 = B.** `terminal_at` is the server's observed time, never `source_event_at`. `AT-TERM-LATE` is not required.
- **§20 — migrations are written, never applied.** No task in this plan runs `apply-migrations.sh`, `psql`, or any DDL against any database. Applying 007–009 is gated on a verified backup → restore and is out of this plan's scope.
- **D-2 — the tombstone retention setting is named, not added.** `WaybillTombstoneRetention` / `DATAHUB_WAYBILL_TOMBSTONE_RETENTION_DAYS` (730 days, bounded 730–3650) belongs to the P6 purge, which is what would read it. Do **not** add it to `DataHubRuntimeOptions` in P1 — it is deliberately distinct from the existing `TombstoneRetention` (90 days, `dashboard_changes` delete markers), and shipping an unread setting invites an operator to tune something with no effect.
- **D-3 — deployment ordering.** After this plan lands, no host may run the resulting image until 007–009 are applied to its database, because `PostgresDataSource.RequiredMigrations` is what `/health/ready` demands. This plan commits and pushes; it deploys nothing.
- **Owner permission for protected files:** `backend/datahub/migrations/**` is a protected area ("Database schema migrations"). The owner approved creating exactly `007_event_metadata.sql`, `008_terminal_tombstone.sql`, `009_terminal_index_notx.sql` as block A of the spec. Creating any other migration, or editing 001–006, requires a new owner request. `src/AutoJMS.DataHub.Api/Program.cs` is **not** protected — the protected entry is `src/AutoJMS/Program.cs`, the WinForms host.
- **Single-writer lock.** Take `.agent-lock.md` before Task 1 only if it reads `Current Writer: None`; release it after Task 7. If another session holds it, stop and report.
- **Npgsql:** one command in progress per connector. Never call `RollbackAsync` while a reader is open — dispose the reader first. This is the defect that produced the P0 enrollment bug.
- **Test conventions:** pure-logic tests are plain `[Fact]`. Anything needing PostgreSQL carries `[RequiresDataHubDatabaseFact]` and skips when `DATAHUB_TEST_CONNECTION_STRING` is unset. No CI workflow provisions PostgreSQL, so the pure tests are the build gate.

---

## File Structure

**Create — migrations** (`backend/datahub/migrations/`)

| File | Responsibility |
|---|---|
| `007_event_metadata.sql` | Four nullable metadata columns on `waybill_scan_events` for later phases. Written by nothing in P1. |
| `008_terminal_tombstone.sql` | Terminal columns on `waybill_projections` plus the `waybill_tombstones` table. |
| `009_terminal_index_notx.sql` | The partial retention index, `CONCURRENTLY`, outside a transaction. |

**Create — source** (`src/AutoJMS.DataHub.Api/`)

| File | Responsibility |
|---|---|
| `Infrastructure/IngestHorizonPolicy.cs` | Pure: is one scan time inside the horizon? Owns the problem code and the operator-facing detail string. |
| `Infrastructure/IngestHorizonInvariant.cs` | Pure: which `waybill_scan_events` retention policies violate `ingest < retention`? Plus the `RetentionDeletePolicy` row shape. |
| `Infrastructure/RetentionPolicyReader.cs` | The one database read those two need. Nothing else. |
| `Infrastructure/TerminalPolicy.cs` | Pure: is this scan type code terminal? Constructed empty in production. |
| `Infrastructure/ReopenRepository.cs` | The reopen transaction: idempotency, counter, projection update, change row, audit. |
| `Endpoints/ReopenEndpoints.cs` | Route, admin re-check, header validation, status mapping. |
| `Health/IngestHorizonHealthCheck.cs` | Degraded when the invariant is violated. Separate from the configuration check on purpose (see spec, "File structure"). |
| `Health/IngestHorizonStartupCheck.cs` | The same evaluation once at boot, refusing to start. |

**Create — tests** (`tests/AutoJMS.DataHub.Api.Tests/`)

`Infrastructure/IngestHorizonPolicyTests.cs`, `Infrastructure/IngestHorizonInvariantTests.cs`, `Infrastructure/TerminalPolicyTests.cs`, `Health/IngestHorizonHealthCheckTests.cs`, `Configuration/DataHubRuntimeOptionsHorizonTests.cs`, `Hosting/ReopenEndpointTests.cs`.

**Modify**

| File | Change |
|---|---|
| `Infrastructure/PostgresDataSource.cs` | `RequiredMigrations` += 3, `RequiredTables` += `waybill_tombstones` (D-3). |
| `Configuration/DataHubRuntimeOptions.cs` | `IngestHorizon`, `IngestFutureSkew`, their bounds, their env parsing. |
| `Infrastructure/IngestContracts.cs` | `IngestResponse.TerminalLockedItems`. |
| `Infrastructure/IngestRepository.cs` | C1–C4, `TimeProvider` + `TerminalPolicy` injection, `ReadProjectionAsync` → `internal`. |
| `Program.cs` | DI for the new types, the startup check, the health check, `MapReopenEndpoints()`. |

---

## Task 1: Migrations and the readiness contract

**Files:**
- Create: `backend/datahub/migrations/007_event_metadata.sql`
- Create: `backend/datahub/migrations/008_terminal_tombstone.sql`
- Create: `backend/datahub/migrations/009_terminal_index_notx.sql`
- Modify: `src/AutoJMS.DataHub.Api/Infrastructure/PostgresDataSource.cs:100-117`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Health/SchemaContractTests.cs` (existing — must stay green, do not edit)

**Interfaces:**
- Consumes: nothing.
- Produces: the columns `waybill_projections.is_terminal`, `.terminal_at`, `.terminal_state_code`, `.last_change_seq` and the table `waybill_tombstones (site_id, waybill_no, terminal_at, terminal_state_code, last_change_seq, purged_at)`, which Tasks 6 and 7 write SQL against.

**Why the test file is not edited:** `SchemaContractTests` already asserts that the migration directory equals `PostgresDataSource.RequiredMigrations` and that the tables the migrations create equal `RequiredTables`. It is the failing test for this task — it exists, and adding the three files makes it red.

- [ ] **Step 1: Run the existing schema contract tests to confirm they start green**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~SchemaContractTests"
```

Expected: PASS, 5 tests. This is the baseline — if it is already red, stop and report.

- [ ] **Step 2: Create `backend/datahub/migrations/007_event_metadata.sql`**

```sql
-- 007_event_metadata.sql
-- Metadata that later streaming phases read off an event without re-deriving it: which
-- reducer kind the event was classified as, and the three version numbers that say how it
-- was produced. Nothing in P1 writes them -- InsertEventAsync is untouched -- so every
-- column is nullable with no default, which is also what keeps this ALTER from rewriting a
-- table that already holds every scan the fleet has ever sent.
--
-- event_kind repeats the vocabulary of jms_event_policies.event_kind rather than
-- referencing it: the policy table is keyed by (reducer_version, scan_type_code) and says
-- what a code means *now*, while this column records what the event was taken to mean when
-- it was ingested. A foreign key would tie a historical fact to a mutable classification.
--
-- Transactional on purpose (no `-- no-transaction` marker). Only 009 needs CONCURRENTLY.

ALTER TABLE waybill_scan_events
    ADD COLUMN IF NOT EXISTS event_kind             text,
    ADD COLUMN IF NOT EXISTS reducer_version        integer,
    ADD COLUMN IF NOT EXISTS normalizer_version     integer,
    ADD COLUMN IF NOT EXISTS source_schema_version  integer;

-- Dropped first so a re-run cannot fail on "constraint already exists". The NULL arm is
-- what lets every existing row -- and every row P1 writes -- satisfy it.
ALTER TABLE waybill_scan_events
    DROP CONSTRAINT IF EXISTS ck_waybill_scan_events_event_kind;
ALTER TABLE waybill_scan_events
    ADD CONSTRAINT ck_waybill_scan_events_event_kind
        CHECK (event_kind IS NULL
               OR event_kind IN ('state_transition', 'activity', 'inventory', 'communication'));

INSERT INTO schema_migrations (version) VALUES ('007_event_metadata')
ON CONFLICT (version) DO NOTHING;
```

- [ ] **Step 3: Create `backend/datahub/migrations/008_terminal_tombstone.sql`**

```sql
-- 008_terminal_tombstone.sql
-- Terminal state on the projection, and the ledger that outlives it.
--
-- is_terminal is NOT NULL DEFAULT false rather than nullable: "we have not decided" is not
-- a state this column can be in, and a three-valued flag would make every reader write the
-- IS NOT TRUE form to stay correct. PostgreSQL 11+ stores a non-volatile default in the
-- catalogue, so this does not rewrite the table either.
--
-- waybill_tombstones is the anti-resurrection ledger: once the P6 purge removes a terminal
-- projection, the tombstone is the only remaining proof that the waybill existed and must
-- not come back from a late re-ingest. Nothing in P1 inserts into it -- rows are written by
-- the purge -- but ingest reads it from P1 onward, which is why it is created now.
--
-- It deliberately does NOT reference waybill_projections: the projection is exactly the row
-- that is gone by the time a tombstone matters. site_id references sites(id) with no
-- cascade, matching every other table in 001_core.
--
-- Transactional on purpose (no `-- no-transaction` marker).

ALTER TABLE waybill_projections
    ADD COLUMN IF NOT EXISTS is_terminal          boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS terminal_at          timestamptz,
    ADD COLUMN IF NOT EXISTS terminal_state_code  integer,
    ADD COLUMN IF NOT EXISTS last_change_seq      bigint;

-- A terminal row without a timestamp cannot be aged out by the P6 purge, so it would sit
-- terminal forever and never become eligible. Cheap to enforce here, impossible to repair
-- later without guessing the time.
ALTER TABLE waybill_projections
    DROP CONSTRAINT IF EXISTS ck_waybill_projections_terminal_at_present;
ALTER TABLE waybill_projections
    ADD CONSTRAINT ck_waybill_projections_terminal_at_present
        CHECK (is_terminal = false OR terminal_at IS NOT NULL);

CREATE TABLE IF NOT EXISTS waybill_tombstones (
    site_id             uuid        NOT NULL REFERENCES sites(id),
    waybill_no          text        NOT NULL,
    terminal_at         timestamptz,
    terminal_state_code integer,
    last_change_seq     bigint,
    purged_at           timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (site_id, waybill_no)
);

INSERT INTO schema_migrations (version) VALUES ('008_terminal_tombstone')
ON CONFLICT (version) DO NOTHING;
```

- [ ] **Step 4: Create `backend/datahub/migrations/009_terminal_index_notx.sql`**

```sql
-- 009_terminal_index_notx.sql
-- The index the P6 retention purge will scan: terminal rows, oldest first, per site.
--
-- CONCURRENTLY because waybill_projections is the hot table on every ingest, and a plain
-- CREATE INDEX takes a lock that blocks every writer at every site for the duration. That
-- forbids a transaction, which is what the _notx suffix tells apply-migrations.sh: a file
-- named *_notx.sql is applied WITHOUT --single-transaction.
--
-- Partial on is_terminal = true so the index holds only the rows the purge looks at. With
-- no terminal scan type classified (OD-1), that is zero rows today and the index costs
-- nothing until OD-1 is signed.
--
-- CONCURRENTLY's failure mode is the reason for contract 19.2: a cancelled or failed build
-- leaves an INVALID index behind, and IF NOT EXISTS then finds it and skips silently, so
-- the purge plans a sequential scan against a table nobody is watching. After applying
-- this file, check pg_index.indisvalid for the index below; if false, DROP it, retry once,
-- and stop and report if it fails again.

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_waybill_projections_terminal_retention
    ON waybill_projections (site_id, terminal_at)
    WHERE is_terminal = true;

INSERT INTO schema_migrations (version) VALUES ('009_terminal_index_notx')
ON CONFLICT (version) DO NOTHING;
```

- [ ] **Step 5: Run the schema contract tests to verify they now fail**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~SchemaContractTests"
```

Expected: FAIL. `Required_migrations_are_exactly_the_migration_files_in_order` reports the on-disk array has 9 entries against `RequiredMigrations`' 6, and `Required_tables_are_exactly_the_tables_the_migrations_create` reports `waybill_tombstones` missing from `RequiredTables`.

- [ ] **Step 6: Update the readiness contract in `PostgresDataSource.cs`**

Replace the `RequiredMigrations` array body (`:100-108`):

```csharp
    public static readonly string[] RequiredMigrations =
    [
        "001_core",
        "002_seed_policies",
        "003_seed_retention",
        "004_projection_slot_payloads",
        "005_change_retention_floor",
        "006_revocation_and_retention_indexes",
        "007_event_metadata",
        "008_terminal_tombstone",
        "009_terminal_index_notx"
    ];
```

Replace the `RequiredTables` declaration (`:110-117`):

```csharp
    /// <summary>
    /// Every table those migrations create. Same contract, same test.
    ///
    /// Adding a name here is a deployment decision, not just a list edit: readiness is what
    /// docker-compose gates the API container on, so a host that has not applied the
    /// migration creating this table answers /health/ready with 503 and never enters
    /// rotation. 007-009 must be applied before the image carrying them is deployed.
    /// </summary>
    public static readonly string[] RequiredTables =
    [
        "schema_migrations", "sites", "devices", "site_fetch_leases",
        "site_change_counters", "waybill_scan_events", "waybill_projections",
        "dashboard_changes", "jms_event_policies", "idempotency_records",
        "retention_policies", "audit_logs", "revoked_device_credentials",
        "waybill_tombstones"
    ];
```

- [ ] **Step 7: Run the schema contract tests to verify they pass**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~SchemaContractTests"
```

Expected: PASS, 5 tests. `The_probe_query_names_every_required_migration_and_table` passing is the important one — it proves the interpolated readiness SQL is still balanced and still counts correctly at the new lengths.

- [ ] **Step 8: Commit**

```bash
git add backend/datahub/migrations/007_event_metadata.sql backend/datahub/migrations/008_terminal_tombstone.sql backend/datahub/migrations/009_terminal_index_notx.sql src/AutoJMS.DataHub.Api/Infrastructure/PostgresDataSource.cs
git commit -m "feat(datahub): add the P1 terminal and tombstone schema, unapplied"
```

---

## Task 2: The horizon policy and its two settings

**Files:**
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/IngestHorizonPolicy.cs`
- Modify: `src/AutoJMS.DataHub.Api/Configuration/DataHubRuntimeOptions.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Infrastructure/IngestHorizonPolicyTests.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Configuration/DataHubRuntimeOptionsHorizonTests.cs`

**Interfaces:**
- Consumes: `DataHubRuntimeOptions` (existing).
- Produces:
  - `DataHubRuntimeOptions.IngestHorizon` → `TimeSpan`, default 45 days
  - `DataHubRuntimeOptions.IngestFutureSkew` → `TimeSpan`, default 900 seconds
  - `IngestHorizonPolicy(TimeSpan Horizon, TimeSpan FutureSkew)` — sealed record
  - `IngestHorizonPolicy.From(DataHubRuntimeOptions) → IngestHorizonPolicy`
  - `IngestHorizonPolicy.FindViolation(DateTimeOffset scanTimeUtc, DateTimeOffset nowUtc) → string?` — null when acceptable, otherwise the operator-facing detail
  - `IngestHorizonPolicy.ProblemCode` → `"INGEST_HORIZON_VIOLATION"`

- [ ] **Step 1: Write the failing tests**

Create `tests/AutoJMS.DataHub.Api.Tests/Infrastructure/IngestHorizonPolicyTests.cs`:

```csharp
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// The horizon decision is extracted to a pure type for one reason: it is the only part of
/// the ingest guardrail that can be wrong in a way tests can catch without a database.
/// Every case below fixes <c>now</c> explicitly — a test that read the real clock would be
/// asserting against the thing under test.
/// </summary>
public sealed class IngestHorizonPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly IngestHorizonPolicy Policy = new(TimeSpan.FromDays(45), TimeSpan.FromSeconds(900));

    [Fact]
    public void A_scan_time_inside_the_window_is_accepted()
    {
        Assert.Null(Policy.FindViolation(Now.AddDays(-44), Now));
    }

    [Fact]
    public void The_present_moment_is_accepted()
    {
        Assert.Null(Policy.FindViolation(Now, Now));
    }

    [Theory]
    [InlineData(45)]  // exactly at the horizon
    [InlineData(0)]   // and the trivial case, to pin that the boundary is inclusive
    public void The_horizon_boundary_itself_is_accepted(int daysOld)
    {
        // Inclusive on purpose. A device that batches a full 45 days and posts at the
        // boundary is the normal backlog case, not a clock fault, and an exclusive bound
        // would reject it on a round number for no reason an operator could act on.
        Assert.Null(Policy.FindViolation(Now.AddDays(-daysOld), Now));
    }

    [Fact]
    public void A_scan_time_older_than_the_horizon_is_rejected()
    {
        var violation = Policy.FindViolation(Now.AddDays(-45).AddSeconds(-1), Now);

        Assert.NotNull(violation);
        Assert.Contains("older than", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scan_time_inside_the_future_skew_is_accepted()
    {
        // A station clock running fast is ordinary; 15 minutes of it is signed as
        // acceptable by OD-2. Rejecting it would drop real scans.
        Assert.Null(Policy.FindViolation(Now.AddSeconds(900), Now));
    }

    [Fact]
    public void A_scan_time_beyond_the_future_skew_is_rejected()
    {
        var violation = Policy.FindViolation(Now.AddSeconds(901), Now);

        Assert.NotNull(violation);
        Assert.Contains("ahead of", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void Comparison_is_in_utc_regardless_of_the_supplied_offset()
    {
        // The parser hands back UTC, but the type must not depend on that: the same instant
        // written with a +07:00 offset has to decide identically, or a Vietnam-local value
        // would be judged seven hours out.
        var sameInstantInVietnam = Now.AddDays(-1).ToOffset(TimeSpan.FromHours(7));

        Assert.Null(Policy.FindViolation(sameInstantInVietnam, Now));
    }

    [Fact]
    public void From_options_carries_the_configured_values()
    {
        var options = new DataHubRuntimeOptions
        {
            IngestHorizon = TimeSpan.FromDays(10),
            IngestFutureSkew = TimeSpan.FromSeconds(30)
        };

        var policy = IngestHorizonPolicy.From(options);

        Assert.Equal(TimeSpan.FromDays(10), policy.Horizon);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.FutureSkew);
    }
}
```

Create `tests/AutoJMS.DataHub.Api.Tests/Configuration/DataHubRuntimeOptionsHorizonTests.cs`:

```csharp
using AutoJMS.DataHub.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AutoJMS.DataHub.Api.Tests.Configuration;

/// <summary>
/// The bounds are the point. 45 days is not a preference — it is the value that keeps
/// <c>ingest_horizon &lt; event_retention</c> true against the seeded 60-day event policy,
/// so a configuration that clamps somewhere else silently breaks the invariant the startup
/// check exists to defend.
/// </summary>
public sealed class DataHubRuntimeOptionsHorizonTests
{
    [Fact]
    public void The_horizon_defaults_to_the_signed_forty_five_days()
    {
        Assert.Equal(TimeSpan.FromDays(45), Build().IngestHorizon);
    }

    [Fact]
    public void The_future_skew_defaults_to_the_signed_fifteen_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(900), Build().IngestFutureSkew);
    }

    [Fact]
    public void A_configured_horizon_is_honoured()
    {
        Assert.Equal(TimeSpan.FromDays(30), Build(("DATAHUB_INGEST_HORIZON_DAYS", "30")).IngestHorizon);
    }

    [Fact]
    public void A_horizon_at_or_above_the_sixty_day_event_retention_is_clamped_to_fifty_nine()
    {
        // The upper bound is the invariant expressed as a number: at 60 the horizon admits
        // events the retention pass is already deleting, so ingest would accept scans whose
        // dedupe history no longer exists and re-accept them forever.
        Assert.Equal(TimeSpan.FromDays(59), Build(("DATAHUB_INGEST_HORIZON_DAYS", "60")).IngestHorizon);
        Assert.Equal(TimeSpan.FromDays(59), Build(("DATAHUB_INGEST_HORIZON_DAYS", "3650")).IngestHorizon);
    }

    [Fact]
    public void A_zero_or_negative_horizon_is_clamped_to_one_day()
    {
        Assert.Equal(TimeSpan.FromDays(1), Build(("DATAHUB_INGEST_HORIZON_DAYS", "0")).IngestHorizon);
        Assert.Equal(TimeSpan.FromDays(1), Build(("DATAHUB_INGEST_HORIZON_DAYS", "-5")).IngestHorizon);
    }

    [Fact]
    public void An_unparseable_horizon_falls_back_to_the_default_rather_than_to_a_bound()
    {
        // Distinct from clamping: "abc" is not a number to clamp, and answering 1 day would
        // turn a typo into a silent near-total rejection of the fleet's backlog.
        Assert.Equal(TimeSpan.FromDays(45), Build(("DATAHUB_INGEST_HORIZON_DAYS", "abc")).IngestHorizon);
    }

    [Fact]
    public void The_future_skew_is_clamped_between_zero_and_one_hour()
    {
        Assert.Equal(TimeSpan.Zero, Build(("DATAHUB_INGEST_FUTURE_SKEW_SECONDS", "-1")).IngestFutureSkew);
        Assert.Equal(TimeSpan.FromSeconds(3600), Build(("DATAHUB_INGEST_FUTURE_SKEW_SECONDS", "99999")).IngestFutureSkew);
    }

    [Fact]
    public void A_zero_future_skew_is_a_legal_choice()
    {
        // Zero means "no clock tolerance", which an operator running NTP everywhere may
        // legitimately want. It must not be clamped up to the default.
        Assert.Equal(TimeSpan.Zero, Build(("DATAHUB_INGEST_FUTURE_SKEW_SECONDS", "0")).IngestFutureSkew);
    }

    private static DataHubRuntimeOptions Build(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DataHub"] = "Host=postgres;Database=datahub;Username=datahub;Password=test"
        };
        foreach (var (key, value) in settings)
            values[key] = value;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return DataHubRuntimeOptions.FromConfiguration(configuration, new StubEnvironment());
    }

    // A copy rather than a reference: the equivalent stub in TombstoneRetentionTests is a
    // private nested class, and widening it would mean editing a passing test file for the
    // convenience of a new one.
    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "AutoJMS.DataHub.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~IngestHorizonPolicyTests|FullyQualifiedName~DataHubRuntimeOptionsHorizonTests"
```

Expected: FAIL — the build itself fails with `CS0246: The type or namespace name 'IngestHorizonPolicy' could not be found` and `CS0117: 'DataHubRuntimeOptions' does not contain a definition for 'IngestHorizon'`.

- [ ] **Step 3: Add the two settings to `DataHubRuntimeOptions.cs`**

Insert immediately after the `MaximumTombstoneRetentionDays` constant (`:89`), before the `DefaultTrustedProxyNetworks` doc comment:

```csharp
    /// <summary>
    /// How far back a scan time may be and still be ingested.
    ///
    /// The bound that matters is not this number but its relation to event retention:
    /// <c>ingest_horizon &lt; event_retention</c>. Dedupe is
    /// <c>ON CONFLICT (site_id, event_fingerprint) DO NOTHING</c> against
    /// <c>waybill_scan_events</c>, so once retention has deleted an event there is nothing
    /// left to conflict with and the same scan is accepted again — and again on every
    /// retry — silently rebuilding projection state from history the server has already
    /// decided to forget. A horizon shorter than retention means every event ingest will
    /// accept still has its own dedupe row.
    ///
    /// 45 days against the seeded 60-day event policy, signed as OD-2. The maximum is 59
    /// because 60 is the seeded retention and equality is already a violation; it is a
    /// static floor under a dynamic value, which is why
    /// <see cref="Infrastructure.IngestHorizonInvariant"/> re-checks it against the actual
    /// per-site policies at startup and in the health check.
    /// </summary>
    public TimeSpan IngestHorizon { get; set; } = TimeSpan.FromDays(DefaultIngestHorizonDays);

    public const int DefaultIngestHorizonDays = 45;
    public const int MinimumIngestHorizonDays = 1;
    public const int MaximumIngestHorizonDays = 59;

    /// <summary>
    /// How far ahead of server time a scan time may be and still be ingested.
    ///
    /// Station clocks drift, and a scan stamped slightly in the future is a clock fault,
    /// not a forgery — rejecting it would drop real work. Fifteen minutes, signed as OD-2.
    /// This is deliberately not part of the retention invariant: a future-dated event is
    /// never at risk of having been deleted already. Zero is a legal setting for a
    /// deployment that trusts NTP.
    /// </summary>
    public TimeSpan IngestFutureSkew { get; set; } = TimeSpan.FromSeconds(DefaultIngestFutureSkewSeconds);

    public const int DefaultIngestFutureSkewSeconds = 900;
    public const int MinimumIngestFutureSkewSeconds = 0;
    public const int MaximumIngestFutureSkewSeconds = 3600;
```

Then, in `FromConfiguration`, insert immediately after the `TombstoneRetention = ...` initializer (`:170-174`) and before `TrustedProxyNetworks = ...`:

```csharp
            IngestHorizon = TimeSpan.FromDays(ParseBoundedInt(
                configuration["DATAHUB_INGEST_HORIZON_DAYS"],
                DefaultIngestHorizonDays,
                MinimumIngestHorizonDays,
                MaximumIngestHorizonDays)),
            IngestFutureSkew = TimeSpan.FromSeconds(ParseBoundedInt(
                configuration["DATAHUB_INGEST_FUTURE_SKEW_SECONDS"],
                DefaultIngestFutureSkewSeconds,
                MinimumIngestFutureSkewSeconds,
                MaximumIngestFutureSkewSeconds)),
```

- [ ] **Step 4: Create `src/AutoJMS.DataHub.Api/Infrastructure/IngestHorizonPolicy.cs`**

```csharp
using System.Globalization;
using AutoJMS.DataHub.Api.Configuration;

namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// Decides whether one scan time is close enough to now to be ingested.
///
/// A pure record with the clock passed in, because this is the whole guardrail: the
/// repository does nothing but call it and turn a non-null answer into a 422. Keeping the
/// decision here is what lets the boundary cases be tested without a database or a clock
/// hack.
/// </summary>
public sealed record IngestHorizonPolicy(TimeSpan Horizon, TimeSpan FutureSkew)
{
    /// <summary>
    /// Distinct from VALIDATION_FAILED on purpose. Both are 422, but an operator fixes them
    /// differently: this one means a device's clock or backlog is outside the window, and
    /// the remedy is on the device or in the horizon setting, not in the payload. No client
    /// change is needed — unknown problem codes are already handled generically.
    /// </summary>
    public const string ProblemCode = "INGEST_HORIZON_VIOLATION";

    public static IngestHorizonPolicy From(DataHubRuntimeOptions options)
        => new(options.IngestHorizon, options.IngestFutureSkew);

    /// <summary>
    /// Null when the scan time is acceptable; otherwise the detail an operator reads in the
    /// problem response. Both bounds are inclusive: a batch that lands exactly on the
    /// horizon is an ordinary backlog, not a fault.
    /// </summary>
    public string? FindViolation(DateTimeOffset scanTimeUtc, DateTimeOffset nowUtc)
    {
        var scan = scanTimeUtc.ToUniversalTime();
        var now = nowUtc.ToUniversalTime();

        if (scan < now - Horizon)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"scanTime {scan:O} is older than the {Horizon.TotalDays:0.##}-day ingest horizon.");

        if (scan > now + FutureSkew)
            return string.Create(
                CultureInfo.InvariantCulture,
                $"scanTime {scan:O} is more than {FutureSkew.TotalSeconds:0} seconds ahead of server time.");

        return null;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~IngestHorizonPolicyTests|FullyQualifiedName~DataHubRuntimeOptionsHorizonTests"
```

Expected: PASS, 17 tests.

- [ ] **Step 6: Commit**

```bash
git add src/AutoJMS.DataHub.Api/Infrastructure/IngestHorizonPolicy.cs src/AutoJMS.DataHub.Api/Configuration/DataHubRuntimeOptions.cs tests/AutoJMS.DataHub.Api.Tests/Infrastructure/IngestHorizonPolicyTests.cs tests/AutoJMS.DataHub.Api.Tests/Configuration/DataHubRuntimeOptionsHorizonTests.cs
git commit -m "feat(datahub): add the ingest horizon policy and its two settings"
```

---

## Task 3: Wire the horizon guardrail into ingest (C1)

**Files:**
- Modify: `src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs:16-19` (constructor) and `:39-41` (the guardrail)
- Modify: `src/AutoJMS.DataHub.Api/Program.cs:36` region (DI)

**Interfaces:**
- Consumes: `IngestHorizonPolicy` and `IngestHorizonPolicy.ProblemCode` from Task 2; `TimeProvider`, already registered at `Program.cs:26`.
- Produces: `IngestRepository`'s primary constructor becomes `(PostgresDataSource dataSource, ProjectionReducer reducer, JmsEventPolicyRepository policyRepository, IngestHorizonPolicy horizonPolicy, TimeProvider timeProvider)`. Task 6 adds one more parameter to the same list.

**Design notes for the implementer:**
- The check runs **before** `OpenConnectionAsync` at `:42`, so a violation costs no connection and no transaction.
- It re-parses each item's scan time. The loop at `:152` parses again. That duplication is deliberate: folding the two would mean restructuring the item loop, which the Minimal Edit Rule and §26 both push back on, and `ScanTimeParser.Parse` is a `TryParseExact` plus one regex — 200 of them are microseconds against a database round trip.
- Items whose scan time does **not** parse are skipped here and left to the existing in-loop path at `:152-157`, so parse-error status, code and message stay byte-identical.
- A violation fails the whole batch. No partial acceptance — the ≤ 200 items are one all-or-nothing transaction per §26, and accepting some would break that.

- [ ] **Step 1: Add the dependencies to the primary constructor**

Replace `IngestRepository.cs:16-20`:

```csharp
public sealed class IngestRepository(
    PostgresDataSource dataSource,
    ProjectionReducer reducer,
    JmsEventPolicyRepository policyRepository,
    IngestHorizonPolicy horizonPolicy,
    TimeProvider timeProvider)
{
```

- [ ] **Step 2: Insert the guardrail**

In `IngestAsync`, after the idempotency-key length check (`:38-39`) and before the `bodyHash` line (`:41`), insert:

```csharp
        // Before the connection is opened, so a batch outside the window costs no
        // transaction and takes no locks. Items whose scanTime does not parse are left
        // alone here and handled by the existing path inside the loop below, which keeps
        // the parse-error code and message exactly as they were.
        var now = timeProvider.GetUtcNow();
        foreach (var item in request.Items)
        {
            var scanTime = ScanTimeParser.Parse(item.ScanTime);
            if (!scanTime.Success) continue;

            // Whole-batch failure by design: the ≤200 items commit or roll back together,
            // so accepting a subset would break the one guarantee bulk ingest makes.
            if (horizonPolicy.FindViolation(scanTime.UtcValue!.Value, now) is { } violation)
                return IngestOperationResult.Failure(
                    StatusCodes.Status422UnprocessableEntity,
                    IngestHorizonPolicy.ProblemCode,
                    violation);
        }

```

- [ ] **Step 3: Register the policy in `Program.cs`**

After `builder.Services.AddSingleton(JmsEventPolicyCatalog.Default);` (`:36`), insert:

```csharp
builder.Services.AddSingleton(IngestHorizonPolicy.From(runtimeOptions));
```

- [ ] **Step 4: Build to verify the wiring compiles**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: Build succeeded, 0 warnings, 0 errors. A `CS7036` on `IngestRepository` means the DI registration in Step 3 is missing or misordered.

- [ ] **Step 5: Run the full suite to verify nothing regressed**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj
```

Expected: PASS. Database-backed tests report as skipped unless `DATAHUB_TEST_CONNECTION_STRING` is set.

- [ ] **Step 6: Commit**

```bash
git add src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs src/AutoJMS.DataHub.Api/Program.cs
git commit -m "feat(datahub): reject ingest outside the configured time horizon"
```

---

## Task 4: The horizon invariant — startup fail-fast and health check

**Files:**
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/IngestHorizonInvariant.cs`
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/RetentionPolicyReader.cs`
- Create: `src/AutoJMS.DataHub.Api/Health/IngestHorizonHealthCheck.cs`
- Create: `src/AutoJMS.DataHub.Api/Health/IngestHorizonStartupCheck.cs`
- Modify: `src/AutoJMS.DataHub.Api/Program.cs` (DI, health check registration, startup call)
- Test: `tests/AutoJMS.DataHub.Api.Tests/Infrastructure/IngestHorizonInvariantTests.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Health/IngestHorizonHealthCheckTests.cs`

**Interfaces:**
- Consumes: `DataHubRuntimeOptions.IngestHorizon` (Task 2), `PostgresDataSource` (existing).
- Produces:
  - `RetentionDeletePolicy(Guid? SiteId, TimeSpan? DeleteAfter)` — sealed record
  - `IngestHorizonInvariant.Violations(IEnumerable<RetentionDeletePolicy>, TimeSpan horizon) → IReadOnlyList<string>`
  - `IRetentionPolicyReader.ReadEventDeletePoliciesAsync(CancellationToken) → Task<IReadOnlyList<RetentionDeletePolicy>>`
  - `RetentionPolicyReader : IRetentionPolicyReader`
  - `IngestHorizonHealthCheck : IHealthCheck`
  - `IngestHorizonStartupCheck.RunAsync(IServiceProvider, ILogger, CancellationToken) → Task`

**Design notes for the implementer:**
- Event retention is not a setting. It is a `retention_policies` row keyed on `table_name = 'waybill_scan_events'`, resolved per site with a global fallback, and `delete_after` may be NULL. NULL means events are never deleted, so the invariant holds trivially — **NULL is not a violation.**
- A violation is `delete_after` non-NULL **and** `<= IngestHorizon`. Equality is a violation: at equal values the retention pass and the horizon disagree about the same day.
- Startup refuses to boot on a violation. It does **not** refuse to boot when the database cannot be read — reporting an unreachable database is `PostgresHealthCheck`'s job, and crash-looping there turns a transient outage into one that needs a human.
- The health check reports **Degraded**, not Unhealthy. The host serves every read and write correctly; taking `/health/ready` to 503 over a retention policy would evict a working host, and Compose gates caddy on that endpoint.
- The reader is behind `IRetentionPolicyReader` so the health check has a seam. `PostgresDataSource` is sealed and builds a real `NpgsqlDataSource`, so there is no seam below that.

- [ ] **Step 1: Write the failing tests**

Create `tests/AutoJMS.DataHub.Api.Tests/Infrastructure/IngestHorizonInvariantTests.cs`:

```csharp
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// <c>ingest_horizon &lt; event_retention</c> is the one relation the horizon exists to
/// preserve, and it cannot be checked from configuration alone: retention lives in
/// <c>retention_policies</c> rows, per site, with a global fallback, and it is nullable.
/// These cases pin what counts as a violation before either caller is written.
/// </summary>
public sealed class IngestHorizonInvariantTests
{
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(45);

    [Fact]
    public void No_policies_is_no_violation()
    {
        // An empty table means nothing deletes events, so no horizon can outlive retention.
        Assert.Empty(IngestHorizonInvariant.Violations([], Horizon));
    }

    [Fact]
    public void A_null_delete_after_is_not_a_violation()
    {
        // NULL is "never delete". That satisfies the invariant for any horizon, and
        // reporting it would make an intentionally archival deployment permanently Degraded.
        var policies = new[] { new RetentionDeletePolicy(null, null) };

        Assert.Empty(IngestHorizonInvariant.Violations(policies, Horizon));
    }

    [Fact]
    public void A_retention_longer_than_the_horizon_is_not_a_violation()
    {
        var policies = new[] { new RetentionDeletePolicy(null, TimeSpan.FromDays(60)) };

        Assert.Empty(IngestHorizonInvariant.Violations(policies, Horizon));
    }

    [Fact]
    public void A_retention_equal_to_the_horizon_is_a_violation()
    {
        // Equality is not safe. At the same value the horizon admits an event on the day
        // the retention pass deletes its dedupe row, and which one runs first is a race.
        var policies = new[] { new RetentionDeletePolicy(null, TimeSpan.FromDays(45)) };

        Assert.Single(IngestHorizonInvariant.Violations(policies, Horizon));
    }

    [Fact]
    public void A_retention_shorter_than_the_horizon_is_a_violation()
    {
        var policies = new[] { new RetentionDeletePolicy(null, TimeSpan.FromDays(30)) };
        var violations = IngestHorizonInvariant.Violations(policies, Horizon);

        Assert.Single(violations);
        Assert.Contains("global", violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_per_site_violation_names_the_site()
    {
        // A startup refusal has to say which policy to fix. "A policy is wrong" against a
        // fleet of sites is not an actionable message.
        var siteId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var policies = new[] { new RetentionDeletePolicy(siteId, TimeSpan.FromDays(10)) };
        var violations = IngestHorizonInvariant.Violations(policies, Horizon);

        Assert.Single(violations);
        Assert.Contains(siteId.ToString("D"), violations[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Every_violating_policy_is_reported_not_only_the_first()
    {
        // The operator fixes them in one pass or comes back for each reboot.
        var policies = new[]
        {
            new RetentionDeletePolicy(null, TimeSpan.FromDays(30)),
            new RetentionDeletePolicy(Guid.NewGuid(), TimeSpan.FromDays(7)),
            new RetentionDeletePolicy(Guid.NewGuid(), TimeSpan.FromDays(90))
        };

        Assert.Equal(2, IngestHorizonInvariant.Violations(policies, Horizon).Count);
    }
}
```

Create `tests/AutoJMS.DataHub.Api.Tests/Health/IngestHorizonHealthCheckTests.cs`:

```csharp
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Health;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AutoJMS.DataHub.Api.Tests.Health;

/// <summary>
/// The status this check reports is a traffic decision. <c>/health/ready</c> maps Degraded
/// to 200 and Unhealthy to 503, and docker-compose gates the caddy service on it — so
/// choosing Unhealthy here would take a host that serves every read and write correctly out
/// of rotation over a retention policy an operator can fix while it runs.
/// </summary>
public sealed class IngestHorizonHealthCheckTests
{
    private sealed class StubReader(IReadOnlyList<RetentionDeletePolicy>? policies, Exception? failure = null)
        : IRetentionPolicyReader
    {
        public Task<IReadOnlyList<RetentionDeletePolicy>> ReadEventDeletePoliciesAsync(CancellationToken cancellationToken)
            => failure is null
                ? Task.FromResult(policies!)
                : Task.FromException<IReadOnlyList<RetentionDeletePolicy>>(failure);
    }

    private static readonly DataHubRuntimeOptions Options = new() { IngestHorizon = TimeSpan.FromDays(45) };

    [Fact]
    public async Task A_retention_longer_than_the_horizon_is_healthy()
    {
        var check = new IngestHorizonHealthCheck(Options, new StubReader([new RetentionDeletePolicy(null, TimeSpan.FromDays(60))]));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task A_violating_policy_is_degraded_and_names_the_policy()
    {
        var check = new IngestHorizonHealthCheck(Options, new StubReader([new RetentionDeletePolicy(null, TimeSpan.FromDays(30))]));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("global", result.Description!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_database_is_not_this_check_s_report_to_make()
    {
        // PostgresHealthCheck already answers "can we reach the database", and it is tagged
        // ready too. Reporting it twice would double-count one outage; reporting it as a
        // horizon violation would send the operator to the wrong setting entirely.
        var check = new IngestHorizonHealthCheck(Options, new StubReader(null, new NpgsqlException("connection refused")));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task An_unconfigured_data_source_is_not_a_violation_either()
    {
        // PostgresDataSource throws InvalidOperationException when no connection string was
        // supplied, which is the shape of a test host and of a misconfigured one. The
        // configuration check owns that report.
        var check = new IngestHorizonHealthCheck(Options, new StubReader(null, new InvalidOperationException("not configured")));

        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~IngestHorizonInvariantTests|FullyQualifiedName~IngestHorizonHealthCheckTests"
```

Expected: FAIL with `CS0246` for `RetentionDeletePolicy`, `IngestHorizonInvariant`, `IRetentionPolicyReader`, and `IngestHorizonHealthCheck`.

- [ ] **Step 3: Create `src/AutoJMS.DataHub.Api/Infrastructure/IngestHorizonInvariant.cs`**

```csharp
using System.Globalization;

namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// One <c>retention_policies</c> row's delete clock, reduced to what the invariant needs.
/// A null <see cref="SiteId"/> is the global fallback row; a null
/// <see cref="DeleteAfter"/> means the table is never purged.
/// </summary>
public sealed record RetentionDeletePolicy(Guid? SiteId, TimeSpan? DeleteAfter);

/// <summary>
/// Checks <c>ingest_horizon &lt; event_retention</c> against the retention policies that
/// actually exist.
///
/// The static 59-day maximum on <see cref="Configuration.DataHubRuntimeOptions.IngestHorizon"/>
/// only defends the seeded 60-day global policy. Retention is per site with a global
/// fallback and is editable at runtime, so a per-site row inserted an hour after boot can
/// break the relation without any configuration changing — which is why this is evaluated
/// at startup AND on every readiness poll rather than once.
/// </summary>
public static class IngestHorizonInvariant
{
    public static IReadOnlyList<string> Violations(
        IEnumerable<RetentionDeletePolicy> policies,
        TimeSpan horizon)
        => policies
            .Where(policy => policy.DeleteAfter is { } deleteAfter && deleteAfter <= horizon)
            .Select(policy => Describe(policy, horizon))
            .ToArray();

    private static string Describe(RetentionDeletePolicy policy, TimeSpan horizon)
    {
        var scope = policy.SiteId is { } siteId
            ? string.Create(CultureInfo.InvariantCulture, $"site {siteId:D}")
            : "the global policy";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{scope} deletes waybill_scan_events after {policy.DeleteAfter!.Value.TotalDays:0.##} days, "
            + $"which is not longer than the {horizon.TotalDays:0.##}-day ingest horizon");
    }
}
```

- [ ] **Step 4: Create `src/AutoJMS.DataHub.Api/Infrastructure/RetentionPolicyReader.cs`**

```csharp
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public interface IRetentionPolicyReader
{
    Task<IReadOnlyList<RetentionDeletePolicy>> ReadEventDeletePoliciesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The one query <see cref="IngestHorizonInvariant"/>'s two callers need. Behind an
/// interface because <see cref="PostgresDataSource"/> is sealed and builds a real
/// <c>NpgsqlDataSource</c>, so this is the lowest point at which the health check can be
/// tested without a database.
/// </summary>
public sealed class RetentionPolicyReader(PostgresDataSource dataSource) : IRetentionPolicyReader
{
    public async Task<IReadOnlyList<RetentionDeletePolicy>> ReadEventDeletePoliciesAsync(
        CancellationToken cancellationToken)
    {
        // EXTRACT(EPOCH FROM ...) rather than reading the interval directly: Npgsql cannot
        // map an interval carrying months or years onto TimeSpan, and an operator is free
        // to write `interval '2 months'`. EXTRACT resolves that to seconds server-side
        // using PostgreSQL's own 30-day month, so no value in the column can throw here.
        const string sql = """
            SELECT site_id, EXTRACT(EPOCH FROM delete_after)
              FROM retention_policies
             WHERE table_name = 'waybill_scan_events';
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var policies = new List<RetentionDeletePolicy>();
        while (await reader.ReadAsync(cancellationToken))
        {
            policies.Add(new RetentionDeletePolicy(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.IsDBNull(1) ? null : TimeSpan.FromSeconds((double)reader.GetDecimal(1))));
        }

        return policies;
    }
}
```

- [ ] **Step 5: Create `src/AutoJMS.DataHub.Api/Health/IngestHorizonHealthCheck.cs`**

```csharp
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AutoJMS.DataHub.Api.Health;

/// <summary>
/// Degraded, never Unhealthy. A horizon that outlives a retention policy is a real fault an
/// operator must fix, but the host is still serving every read and every write correctly —
/// and <c>/health/ready</c> maps Unhealthy to 503, which docker-compose reads as "do not
/// route to this container". Removing a working host from rotation would be a self-inflicted
/// outage on top of a configuration mistake.
///
/// Separate from <see cref="RuntimeConfigurationHealthCheck"/> because that one is
/// deliberately pure configuration with no I/O; giving it a database read would change what
/// its failures mean.
/// </summary>
public sealed class IngestHorizonHealthCheck(
    DataHubRuntimeOptions options,
    IRetentionPolicyReader reader) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RetentionDeletePolicy> policies;
        try
        {
            policies = await reader.ReadEventDeletePoliciesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            // "Can we reach the database" is PostgresHealthCheck's question, and it carries
            // the same ready tag. Answering it again here would double-count one outage and
            // point the operator at the wrong setting.
            return HealthCheckResult.Healthy(
                "The ingest horizon invariant was not evaluated; the database check owns that report.");
        }

        var violations = IngestHorizonInvariant.Violations(policies, options.IngestHorizon);
        return violations.Count == 0
            ? HealthCheckResult.Healthy("The ingest horizon is shorter than every event retention policy.")
            : HealthCheckResult.Degraded("Ingest horizon invariant violated: " + string.Join("; ", violations));
    }
}
```

- [ ] **Step 6: Create `src/AutoJMS.DataHub.Api/Health/IngestHorizonStartupCheck.cs`**

```csharp
using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Npgsql;

namespace AutoJMS.DataHub.Api.Health;

/// <summary>
/// Refuses the boot when a retention policy would delete events inside the ingest horizon.
///
/// Fail-fast is the right shape here because the damage is silent and cumulative: past the
/// dedupe window, <c>ON CONFLICT DO NOTHING</c> has nothing left to conflict with, so the
/// same scans are re-accepted on every retry and rebuild projection state from history the
/// server has already decided to forget. A host that starts and quietly does that is worse
/// than a host that does not start.
///
/// An unreadable database is explicitly NOT a boot refusal. That would turn a transient
/// outage into one that needs a human, and reporting an unreachable database already
/// belongs to the readiness probe.
/// </summary>
public static class IngestHorizonStartupCheck
{
    public static async Task RunAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<DataHubRuntimeOptions>();
        var reader = services.GetRequiredService<IRetentionPolicyReader>();

        IReadOnlyList<RetentionDeletePolicy> policies;
        try
        {
            policies = await reader.ReadEventDeletePoliciesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "The ingest horizon invariant could not be checked at startup because the database was unreadable. The readiness probe will report the database, and the health check re-evaluates the invariant on every poll.");
            return;
        }

        var violations = IngestHorizonInvariant.Violations(policies, options.IngestHorizon);
        if (violations.Count > 0)
            throw new InvalidOperationException(
                "Refusing to start: the ingest horizon is not shorter than event retention. "
                + string.Join("; ", violations)
                + ". Raise the retention policy's delete_after, or lower DATAHUB_INGEST_HORIZON_DAYS.");
    }
}
```

- [ ] **Step 7: Register everything in `Program.cs`**

After `builder.Services.AddSingleton(IngestHorizonPolicy.From(runtimeOptions));` (added in Task 3), insert:

```csharp
builder.Services.AddSingleton<IRetentionPolicyReader, RetentionPolicyReader>();
```

Add the health check to the existing `AddHealthChecks()` chain, so it reads:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<RuntimeConfigurationHealthCheck>("runtime-configuration", tags: ["ready"])
    .AddCheck<IngestHorizonHealthCheck>("ingest-horizon", tags: ["ready"])
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
```

Immediately before `app.Run();`, insert:

```csharp
// Last thing before serving. A retention policy that deletes events inside the ingest
// horizon makes ingest silently re-accept scans whose dedupe rows are gone, so this refuses
// the boot rather than letting the host corrupt projections quietly. An unreadable database
// is not a refusal — see IngestHorizonStartupCheck.
await IngestHorizonStartupCheck.RunAsync(app.Services, app.Logger, CancellationToken.None);

app.Run();
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~IngestHorizonInvariantTests|FullyQualifiedName~IngestHorizonHealthCheckTests"
```

Expected: PASS, 11 tests.

- [ ] **Step 9: Run the full suite — the host-booting tests are the real check here**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj
```

Expected: PASS. `HealthEndpointTests`, `AuthenticationBoundaryTests` and `RequestContractTests` boot a `WebApplicationFactory<Program>` host, so they exercise the new startup call. If any of them now fails to start, the startup check is throwing where it should be logging — re-check the catch clause in Step 6.

- [ ] **Step 10: Commit**

```bash
git add src/AutoJMS.DataHub.Api/Infrastructure/IngestHorizonInvariant.cs src/AutoJMS.DataHub.Api/Infrastructure/RetentionPolicyReader.cs src/AutoJMS.DataHub.Api/Health/IngestHorizonHealthCheck.cs src/AutoJMS.DataHub.Api/Health/IngestHorizonStartupCheck.cs src/AutoJMS.DataHub.Api/Program.cs tests/AutoJMS.DataHub.Api.Tests/Infrastructure/IngestHorizonInvariantTests.cs tests/AutoJMS.DataHub.Api.Tests/Health/IngestHorizonHealthCheckTests.cs
git commit -m "feat(datahub): enforce the horizon-below-retention invariant at boot and on readiness"
```

---

## Task 5: The terminal policy

**Files:**
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/TerminalPolicy.cs`
- Modify: `src/AutoJMS.DataHub.Api/Program.cs` (one DI line)
- Test: `tests/AutoJMS.DataHub.Api.Tests/Infrastructure/TerminalPolicyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `TerminalPolicy(IEnumerable<int> terminalScanTypeCodes)` — sealed class
  - `TerminalPolicy.Empty` → the production instance
  - `TerminalPolicy.IsTerminal(int? scanTypeCode) → bool`
  - `TerminalPolicy.Count → int`

**Design notes for the implementer — read before writing code:**

`jms_event_policies` is **not** empty: `002_seed_policies.sql` seeds `(1, 98, 'inventory')` and `(1, 110, 'state_transition')`, and `JmsEventPolicyCatalog.Default` hard-codes the same two. Do **not** write a gate of the form `if (policies.Count == 0)` — it would be checking something false.

The reason P1 is fail-closed is stronger. `jms_event_policies.event_kind` is CHECK-constrained to exactly `state_transition`, `activity`, `inventory`, `communication` (`001_core.sql:122-130`), and `JmsEventKind` mirrors those four. Neither has a terminal member, so terminal classification has **no carrier anywhere in the schema or the domain**, and §19.3 forbids adding one in P1. That is why this type takes an explicit code set instead of reading the policy table: the table cannot answer the question.

Production constructs it empty. Tests construct it with codes of their own, which is the only way the branch is exercised at all — and is not seeding, because nothing outside the test's object graph ever sees those codes.

- [ ] **Step 1: Write the failing test**

Create `tests/AutoJMS.DataHub.Api.Tests/Infrastructure/TerminalPolicyTests.cs`:

```csharp
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// The codes below are the test's own and exist nowhere else — not in a migration, not in
/// <c>JmsEventKind</c>, not in production DI. OD-1 defers the real terminal set past P4 and
/// forbids seeding one anywhere, including for a test; taking the set as a constructor
/// argument is what makes the branch testable without breaking that.
/// </summary>
public sealed class TerminalPolicyTests
{
    [Fact]
    public void The_production_policy_is_empty()
    {
        // This is the fail-closed guarantee, asserted rather than assumed. If a later change
        // seeds a terminal code into the shared instance, this is the test that says so.
        Assert.Equal(0, TerminalPolicy.Empty.Count);
    }

    [Fact]
    public void An_empty_policy_treats_every_code_as_non_terminal()
    {
        Assert.False(TerminalPolicy.Empty.IsTerminal(98));
        Assert.False(TerminalPolicy.Empty.IsTerminal(110));
        Assert.False(TerminalPolicy.Empty.IsTerminal(int.MaxValue));
    }

    [Fact]
    public void A_configured_code_is_terminal()
    {
        var policy = new TerminalPolicy([7001]);

        Assert.True(policy.IsTerminal(7001));
    }

    [Fact]
    public void A_code_outside_the_set_is_not_terminal()
    {
        var policy = new TerminalPolicy([7001]);

        Assert.False(policy.IsTerminal(7002));
    }

    [Fact]
    public void A_null_code_is_never_terminal()
    {
        // JmsObservation.Code is nullable and a slot can carry no code at all. Null must
        // read as "not terminal", never as a match against a set that happens to be empty.
        Assert.False(new TerminalPolicy([7001]).IsTerminal(null));
        Assert.False(TerminalPolicy.Empty.IsTerminal(null));
    }

    [Fact]
    public void Duplicate_codes_collapse()
    {
        var policy = new TerminalPolicy([7001, 7001, 7002]);

        Assert.Equal(2, policy.Count);
        Assert.True(policy.IsTerminal(7002));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~TerminalPolicyTests"
```

Expected: FAIL with `CS0246: The type or namespace name 'TerminalPolicy' could not be found`.

- [ ] **Step 3: Create `src/AutoJMS.DataHub.Api/Infrastructure/TerminalPolicy.cs`**

```csharp
namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// Which scan type codes end a waybill's life.
///
/// The set is a constructor argument rather than something read from
/// <c>jms_event_policies</c>, because that table cannot answer the question: its
/// <c>event_kind</c> is CHECK-constrained to state_transition, activity, inventory and
/// communication, and <see cref="Domain.JmsEventKind"/> mirrors those four. Terminal
/// classification has no carrier in the schema or the domain, and contract §19.3 forbids
/// adding one in P1.
///
/// So the set is empty by construction, not by configuration — which is what makes P1
/// fail-closed without a feature flag. <see cref="Empty"/> is what production gets; OD-1
/// defers the real set past P4, and until it is signed no code may be added here, to a
/// migration, or to JmsEventKind — including "just for a test". A test constructing its own
/// instance is not seeding: nothing outside that test's object graph sees it.
/// </summary>
public sealed class TerminalPolicy(IEnumerable<int> terminalScanTypeCodes)
{
    /// <summary>The production instance. Empty until OD-1 is signed.</summary>
    public static readonly TerminalPolicy Empty = new([]);

    private readonly HashSet<int> _codes = [.. terminalScanTypeCodes];

    public int Count => _codes.Count;

    public bool IsTerminal(int? scanTypeCode) => scanTypeCode is { } code && _codes.Contains(code);
}
```

- [ ] **Step 4: Register the empty policy in `Program.cs`**

After `builder.Services.AddSingleton<IRetentionPolicyReader, RetentionPolicyReader>();` (added in Task 4), insert:

```csharp
// Empty on purpose, and the only place that decides it. OD-1 defers the terminal scan type
// set past P4, so nothing is terminal, no projection is ever marked, no tombstone is
// written and no purge becomes eligible. This is the off switch P1 ships with — adding a
// feature flag beside it would be a second, redundant one.
builder.Services.AddSingleton(TerminalPolicy.Empty);
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~TerminalPolicyTests"
```

Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/AutoJMS.DataHub.Api/Infrastructure/TerminalPolicy.cs src/AutoJMS.DataHub.Api/Program.cs tests/AutoJMS.DataHub.Api.Tests/Infrastructure/TerminalPolicyTests.cs
git commit -m "feat(datahub): add the fail-closed terminal policy, empty in production"
```

---

## Task 6: Anti-resurrection, last_change_seq and the terminal lifecycle (C2–C4)

**Files:**
- Modify: `src/AutoJMS.DataHub.Api/Infrastructure/IngestContracts.cs:11-18`
- Modify: `src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs` — constructor, item loop, change loop, response, audit, `UpsertProjectionAsync`, `ReadProjectionAsync` visibility, new `ReadTerminalGuardAsync`

**Interfaces:**
- Consumes: `TerminalPolicy` (Task 5), `TimeProvider` (Task 3), the `waybill_projections` terminal columns and `waybill_tombstones` (Task 1).
- Produces:
  - `IngestResponse` gains `int TerminalLockedItems = 0` as the final positional parameter
  - `IngestRepository.ReadProjectionAsync` becomes `internal static` — Task 7 calls it
  - `IngestRepository`'s constructor is `(PostgresDataSource, ProjectionReducer, JmsEventPolicyRepository, IngestHorizonPolicy, TimeProvider, TerminalPolicy)`

**Design notes for the implementer:**
- **Order is the requirement.** The tombstone/terminal probe runs *after* `InsertEventAsync` succeeds. The event is inserted first on purpose: dedupe and history must stay complete regardless of the block decision, or a blocked scan would be re-accepted forever.
- **`accepted` still increments for a blocked item.** `AcceptedItems` means "events accepted into the event log", and the event was. `TerminalLockedItems` is an orthogonal counter saying an accepted event did not move the projection. Changing what `AcceptedItems` counts would be a silent contract break for every existing client.
- **A block is not a rollback.** `continue` — no projection mutation, no version bump, no `change_seq`, no `dashboard_changes` row, and the rest of the batch commits.
- **The probe is cached per waybill.** A 200-item batch touching five waybills costs five probes, not two hundred.
- **The default on `TerminalLockedItems` matters.** Responses already in `idempotency_records.response` were serialized without the field and must still deserialize on replay. Those requests predate the feature, so replaying them as 0 is correct, not merely convenient.
- **`last_change_seq` is set only for rows this transaction writes.** Never backfill existing rows.
- **Terminal is one-way in the upsert.** `is_terminal` ORs and the two terminal columns COALESCE, so an ordinary later upsert cannot clear a terminal mark. Only Task 7's reopen clears it, explicitly.

- [ ] **Step 1: Add `TerminalLockedItems` to the response contract**

Replace `IngestContracts.cs:11-18`:

```csharp
/// <summary>
/// <paramref name="TerminalLockedItems"/> counts events that were accepted into the event
/// log but did not move a projection, because the waybill is terminal or tombstoned. It is
/// last and defaulted because responses already stored in
/// <c>idempotency_records.response</c> were serialized without it, and a replay of one of
/// those requests must still deserialize — as 0, which is what those requests actually did.
/// </summary>
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

- [ ] **Step 2: Add `TerminalPolicy` to the repository constructor**

Replace the primary constructor written in Task 3:

```csharp
public sealed class IngestRepository(
    PostgresDataSource dataSource,
    ProjectionReducer reducer,
    JmsEventPolicyRepository policyRepository,
    IngestHorizonPolicy horizonPolicy,
    TimeProvider timeProvider,
    TerminalPolicy terminalPolicy)
{
```

- [ ] **Step 3: Add the three accumulators**

Replace `IngestRepository.cs:145-148`:

```csharp
        var accepted = 0;
        var duplicates = 0;
        var terminalLocked = 0;
        var changedByWaybill = new Dictionary<string, (WaybillProjection Projection, ProjectionBody Body)>(StringComparer.Ordinal);
        var seenWaybills = new Dictionary<string, WaybillProjection>(StringComparer.Ordinal);
        // One probe per distinct waybill, not per item: a 200-item batch usually touches a
        // handful of waybills. true means blocked — terminal, or tombstoned by an earlier
        // purge. Kept in step with the terminal marks this batch makes, below.
        var terminalGuard = new Dictionary<string, bool>(StringComparer.Ordinal);
        var terminalByWaybill = new Dictionary<string, (DateTimeOffset At, int StateCode)>(StringComparer.Ordinal);
```

- [ ] **Step 4: Insert the anti-resurrection guard (C2)**

Replace `IngestRepository.cs:172-175` — the `accepted++` line and the `current` assignment that follows it:

```csharp
            accepted++;

            // After the event insert, not before: dedupe and history stay complete whatever
            // this decides, so a blocked scan is recorded once and never re-accepted. The
            // count is separate from `accepted` because the event WAS accepted — it just
            // did not move the projection.
            if (!terminalGuard.TryGetValue(observation.WaybillNo, out var blocked))
            {
                blocked = await ReadTerminalGuardAsync(connection, transaction, siteId, observation.WaybillNo, cancellationToken);
                terminalGuard[observation.WaybillNo] = blocked;
            }

            if (blocked)
            {
                // No projection mutation, no version bump, no change_seq, no
                // dashboard_changes row — and deliberately no rollback: the rest of the
                // batch is unaffected and commits.
                terminalLocked++;
                continue;
            }

            var current = seenWaybills.TryGetValue(observation.WaybillNo, out var cached)
                ? cached
                : await ReadProjectionAsync(connection, transaction, siteId, observation.WaybillNo, cancellationToken);
```

- [ ] **Step 5: Insert the terminal lifecycle (C4)**

Replace `IngestRepository.cs:188-191` — the reduce, cache and change-detection lines:

```csharp
            var next = reducer.Reduce(current, eventValue, policies);
            seenWaybills[observation.WaybillNo] = next;

            // Inert in P1: TerminalPolicy is empty, so this never fires. OD-6 = B, so the
            // stamp is the server's observed time rather than the event's — a device that
            // uploads a month-old terminal scan must not make the purge clock retroactive.
            if (terminalPolicy.IsTerminal(next.CurrentState?.Code))
            {
                terminalByWaybill[observation.WaybillNo] = (timeProvider.GetUtcNow(), next.CurrentState!.Code!.Value);
                // A later item in this same batch for this waybill is now blocked too.
                terminalGuard[observation.WaybillNo] = true;
            }

            if (next.Version != (current?.Version ?? 0))
                changedByWaybill[observation.WaybillNo] = (next, ProjectionBody.From(next, DateTimeOffset.UtcNow));
```

- [ ] **Step 6: Pass the sequence and the terminal mark into the upsert (C3 + C4)**

Replace `IngestRepository.cs:216-219` — the body of the `foreach (var entry in changed)` loop, through the `InsertChangeAsync` call:

```csharp
                sequence = checked(sequence + 1);
                var body = entry.Body with { Version = entry.Projection.Version };
                var terminalMark = terminalByWaybill.TryGetValue(entry.Projection.WaybillNo, out var mark)
                    ? mark
                    : ((DateTimeOffset At, int StateCode)?)null;
                await UpsertProjectionAsync(connection, transaction, entry.Projection, body.UpdatedAt, sequence, terminalMark, cancellationToken);
                await InsertChangeAsync(connection, transaction, siteId, sequence, entry.Projection.WaybillNo, body, cancellationToken);
```

- [ ] **Step 7: Carry the counter into the response and the audit**

Replace `IngestRepository.cs:228` and the audit payload at `:236`:

```csharp
        var response = new IngestResponse(siteId, accepted, duplicates, changed.Count, false, firstSeq, lastSeq, terminalLocked);
```

```csharp
            new { deviceId, accepted, duplicates, changedProjections = changed.Count, terminalLocked, firstSeq, lastSeq },
```

- [ ] **Step 8: Add the guard query**

Insert immediately after `InsertEventAsync` (`:384`, before `ReadProjectionAsync`):

```csharp
    /// <summary>
    /// True when this waybill may no longer move: it is terminal, or a tombstone survives
    /// from a purge that already removed it. Two EXISTS rather than a join because both are
    /// primary-key probes and either alone is decisive.
    ///
    /// No FOR UPDATE. The projection row is locked a moment later by
    /// <see cref="ReadProjectionAsync"/>, and a tombstone is written only by the retention
    /// purge, which never runs inside this transaction.
    /// </summary>
    private static async Task<bool> ReadTerminalGuardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (SELECT 1
                             FROM waybill_projections
                            WHERE site_id = @site_id AND waybill_no = @waybill_no AND is_terminal)
                OR EXISTS (SELECT 1
                             FROM waybill_tombstones
                            WHERE site_id = @site_id AND waybill_no = @waybill_no);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo.Trim());
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }
```

- [ ] **Step 9: Make `ReadProjectionAsync` internal**

Replace the signature line at `:386`. Task 7 needs this exact reader — twenty-six ordinals — and duplicating it there would be two copies of one mapping that must agree.

```csharp
    /// <summary>
    /// Internal rather than private because <see cref="ReopenRepository"/> needs the same
    /// twenty-six-ordinal mapping and two copies of it would drift. Visibility only: the
    /// body is untouched, so §26's rule against rewriting the ingest read still holds.
    /// </summary>
    internal static async Task<WaybillProjection?> ReadProjectionAsync(
```

- [ ] **Step 10: Extend the upsert (C3 + C4)**

Replace the `UpsertProjectionAsync` signature and SQL (`:454-475`) — add the two parameters, four columns, four values, and the four `DO UPDATE SET` clauses:

```csharp
    /// <summary>
    /// <paramref name="changeSeq"/> is the sequence this transaction allocated for the row,
    /// written only for rows it actually writes — existing rows are never backfilled.
    /// <paramref name="terminal"/> is null except when the reducer produced a terminal code,
    /// which cannot happen while <see cref="TerminalPolicy"/> is empty.
    /// </summary>
    private static async Task UpsertProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WaybillProjection projection,
        DateTimeOffset updatedAt,
        long changeSeq,
        (DateTimeOffset At, int StateCode)? terminal,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO waybill_projections (
                site_id, waybill_no,
                state_code, state_name, state_status, state_event_at, state_fingerprint, state_event_id, state_kind, state_payload,
                last_activity_code, last_activity_name, last_activity_status, last_activity_kind, last_activity_at,
                last_activity_fingerprint, last_activity_event_id, last_activity_payload,
                inventory_code, inventory_name, inventory_status, inventory_event_at, inventory_fingerprint, inventory_event_id, inventory_payload,
                payload, reducer_version, version, updated_at,
                last_change_seq, is_terminal, terminal_at, terminal_state_code)
            VALUES (@site_id, @waybill_no,
                    @state_code, @state_name, @state_status, @state_event_at, @state_fingerprint, @state_event_id, @state_kind, @state_payload,
                    @activity_code, @activity_name, @activity_status, @activity_kind, @activity_event_at,
                    @activity_fingerprint, @activity_event_id, @activity_payload,
                    @inventory_code, @inventory_name, @inventory_status, @inventory_event_at, @inventory_fingerprint, @inventory_event_id, @inventory_payload,
                    @payload, @reducer_version, @version, @updated_at,
                    @last_change_seq, @is_terminal, @terminal_at, @terminal_state_code)
            ON CONFLICT (site_id, waybill_no) DO UPDATE SET
```

and, inside the `DO UPDATE SET` list, replace the final `updated_at = EXCLUDED.updated_at;` line with:

```sql
                updated_at = EXCLUDED.updated_at,
                last_change_seq = EXCLUDED.last_change_seq,
                -- Terminal is one-way here. An ordinary later upsert must not clear a mark
                -- an earlier one made; only the reopen endpoint does that, explicitly.
                is_terminal = waybill_projections.is_terminal OR EXCLUDED.is_terminal,
                terminal_at = COALESCE(waybill_projections.terminal_at, EXCLUDED.terminal_at),
                terminal_state_code = COALESCE(waybill_projections.terminal_state_code, EXCLUDED.terminal_state_code);
```

Then, after `command.Parameters.AddWithValue("updated_at", updatedAt);` (`:515`), insert:

```csharp
        command.Parameters.AddWithValue("last_change_seq", changeSeq);
        command.Parameters.AddWithValue("is_terminal", terminal is not null);
        AddNullable(command, "terminal_at", terminal?.At);
        AddNullable(command, "terminal_state_code", terminal?.StateCode);
```

- [ ] **Step 11: Build and run the full suite**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: Build succeeded, 0 warnings, 0 errors.

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj
```

Expected: PASS. `RequestContractTests` is the one to watch — it asserts on the ingest response shape, and a new defaulted field must not change any existing assertion.

- [ ] **Step 12: Commit**

```bash
git add src/AutoJMS.DataHub.Api/Infrastructure/IngestContracts.cs src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs
git commit -m "feat(datahub): block ingest into terminal and tombstoned waybills"
```

---

## Task 7: The reopen endpoint

**Files:**
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/ReopenRepository.cs`
- Create: `src/AutoJMS.DataHub.Api/Endpoints/ReopenEndpoints.cs`
- Modify: `src/AutoJMS.DataHub.Api/Program.cs` (DI + `MapReopenEndpoints()`)
- Test: `tests/AutoJMS.DataHub.Api.Tests/Hosting/ReopenEndpointTests.cs`

**Interfaces:**
- Consumes: `IngestRepository.ReadProjectionAsync` (Task 6, now `internal`), the terminal columns (Task 1), `AdminAuthenticationContext.PathPrefix` and `HttpContext.IsAdminAuthenticated()` (existing), `ProjectionBody.From` (existing).
- Produces:
  - `ReopenResponse(Guid SiteId, string WaybillNo, long Version, long? ChangeSeq, bool WasTerminal, bool Replayed)` — sealed record
  - `ReopenResult(bool Succeeded, int StatusCode, string? ProblemCode, string? Detail, ReopenResponse? Response)` — sealed record, with `Success(ReopenResponse)` and `Failure(int, string, string)`
  - `ReopenRepository(PostgresDataSource dataSource, TimeProvider timeProvider)` with
    `ReopenAsync(Guid siteId, string waybillNo, string idempotencyKey, string actor, CancellationToken cancellationToken) → Task<ReopenResult>`
  - `IEndpointRouteBuilder.MapReopenEndpoints() → IEndpointRouteBuilder`

**Route (D-1):**

```
POST /api/v1/admin/sites/{siteId:guid}/waybills/{waybillNo}/reopen
```

The contract's `/api/v1/sites/...` is unreachable: `AdminAuthenticationMiddleware` returns early for any path outside `AdminAuthenticationContext.PathPrefix = "/api/v1/admin"` (`AdminAuthenticationMiddleware.cs:35`, prefix at `:13`) and so never sets the marker `IsAdminAuthenticated()` reads — the endpoint would answer 401 to every caller, including a correctly authenticated operator. Moving the route under the prefix changes a URL string only; the operator-token requirement the contract actually specifies is satisfied exactly, and the endpoint inherits the per-IP admin limiter and the constant-time token comparison unchanged. The security middleware is not modified.

**Design notes for the implementer:**
- **`AdminAuthenticationMiddleware` checks configuration before credentials.** With `DATAHUB_ADMIN_TOKEN` unset it answers **503** at `AdminAuthenticationMiddleware.cs:55`, before the bearer comparison at `:69`. Any test host asserting on 401 must set the token — see the test's constructor.
- **Idempotency keys are namespaced.** Store the key as `"reopen:" + key`. `idempotency_records`' primary key is `(site_id, key)`, shared with ingest, and `IngestRepository.ReadIdempotencyAsync` deserializes whatever it finds as an `IngestResponse` — a colliding key would hand it a `ReopenResponse`. The prefix makes the collision impossible.
- **Counter SQL is written out here rather than shared with `IngestRepository`.** §26 forbids rewriting the ingest `change_seq` allocation, and extracting it into a shared helper is a rewrite of exactly that block. Two six-line queries is the cheaper price.
- **Status mapping:** 404 when neither a projection nor a tombstone exists; 410 when only a tombstone remains (purged — reopening is impossible, the row is gone); 409 on idempotency key reuse.
- **The audit actor comes from the authenticated principal only, never from the request body.**
- Reopening a non-terminal waybill is a no-op success — it allocates no sequence and writes no change row. An operator retrying because they were unsure must not churn the change feed.

- [ ] **Step 1: Write the failing test**

Create `tests/AutoJMS.DataHub.Api.Tests/Hosting/ReopenEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AutoJMS.DataHub.Api.Tests.Hosting;

/// <summary>
/// The route's placement is the thing under test. The contract put reopen at
/// <c>/api/v1/sites/...</c>, where AdminAuthenticationMiddleware returns early and never
/// sets the marker the handler reads — so the endpoint would have answered 401 to a
/// correctly authenticated operator, forever. These assertions fail if the route ever moves
/// back outside the admin prefix, because an unauthenticated call there produces 404 (no
/// route) rather than 401 (route exists, closed).
///
/// Everything here stops before the repository, so none of it needs a database.
/// </summary>
public sealed class ReopenEndpointTests : IDisposable
{
    private const string AdminToken = "test-admin-token-that-is-long-enough-32";

    private readonly WebApplicationFactory<Program> _factory;

    public ReopenEndpointTests()
    {
        // DATAHUB_ADMIN_TOKEN must be set. AdminAuthenticationMiddleware checks it BEFORE
        // the bearer token (AdminAuthenticationMiddleware.cs:55) and answers 503 "not
        // configured" when it is absent — so without this setting every assertion below
        // would be measuring a missing configuration rather than the guard.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting(WebHostDefaults.EnvironmentKey, "Testing")
            .UseSetting("DATAHUB_ADMIN_TOKEN", AdminToken));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_route_exists_under_the_admin_prefix_and_is_closed_without_a_token()
    {
        using var client = _factory.CreateClient();
        using var request = Request(Route(), "key-12345678");

        using var response = await client.SendAsync(request);

        // 401, not 404: the route is mapped and the guard closed it. A 404 here means the
        // endpoint is not registered; a 200 means it is not guarded.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_device_token_is_not_sufficient_for_an_admin_route()
    {
        // The whole reason the route moved under /api/v1/admin rather than having the
        // handler check a capability: an operator action must not be reachable with a
        // station's credentials.
        using var client = _factory.CreateClient();
        using var request = Request(Route(), "key-12345678");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "a-device-token-that-is-not-the-admin-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_authenticated_operator_reaches_the_handler_and_a_missing_key_is_rejected()
    {
        // The counterpart to the two 401s: with the right token the request gets past the
        // middleware and into the handler, which rejects the missing header itself. Without
        // this case, all three tests above would still pass if the route were never mapped
        // at all — 401 can come from the middleware alone.
        using var client = _factory.CreateClient();
        using var request = Request(Route(), idempotencyKey: null);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_short_idempotency_key_is_rejected()
    {
        using var client = _factory.CreateClient();
        using var request = Request(Route(), "short");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);

        using var response = await client.SendAsync(request);

        // Rejected before the repository is reached, which is what lets this run with no
        // database: the handler validates the header, then hands off.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string Route()
        => $"/api/v1/admin/sites/{Guid.NewGuid():D}/waybills/JMS-1/reopen";

    private static HttpRequestMessage Request(string path, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~ReopenEndpointTests"
```

Expected: FAIL — all four report `NotFound`, because the route is not mapped yet. The first two expect `Unauthorized`, the last two `BadRequest`.

- [ ] **Step 3: Create `src/AutoJMS.DataHub.Api/Infrastructure/ReopenRepository.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record ReopenResponse(
    Guid SiteId,
    string WaybillNo,
    long Version,
    long? ChangeSeq,
    bool WasTerminal,
    bool Replayed);

public sealed record ReopenResult(
    bool Succeeded,
    int StatusCode,
    string? ProblemCode,
    string? Detail,
    ReopenResponse? Response)
{
    public static ReopenResult Success(ReopenResponse response)
        => new(true, StatusCodes.Status200OK, null, null, response);

    public static ReopenResult Failure(int statusCode, string code, string detail)
        => new(false, statusCode, code, detail, null);
}

/// <summary>
/// Clears a terminal mark so a waybill can receive observations again.
///
/// The counter read/update and the change insert are written out here rather than shared
/// with <see cref="IngestRepository"/> on purpose: §26 forbids rewriting the ingest
/// <c>change_seq</c> allocation, and lifting it into a shared helper is a rewrite of exactly
/// that block. The projection read IS shared, because that one is a twenty-six-ordinal
/// mapping and two copies of it would drift.
/// </summary>
public sealed class ReopenRepository(PostgresDataSource dataSource, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Keys live in <c>idempotency_records</c>, whose primary key <c>(site_id, key)</c> is
    /// shared with ingest — and ingest deserializes whatever it finds there as an
    /// <c>IngestResponse</c>. The prefix makes a collision impossible rather than unlikely.
    /// </summary>
    private const string KeyPrefix = "reopen:";

    public async Task<ReopenResult> ReopenAsync(
        Guid siteId,
        string waybillNo,
        string idempotencyKey,
        string actor,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            return ReopenResult.Failure(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "siteId is required.");

        var normalizedWaybill = (waybillNo ?? "").Trim();
        if (normalizedWaybill.Length is 0 or > 64)
            return ReopenResult.Failure(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "waybillNo must contain between 1 and 64 characters.");

        var normalizedKey = KeyPrefix + idempotencyKey.Trim();

        // The request carries no body, so the hash binds the key to the only inputs there
        // are. Without it, the same key aimed at a second waybill would replay the first
        // one's response and silently do nothing.
        var bodyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{siteId:D}\n{normalizedWaybill}"))).ToLowerInvariant();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var deadlines = new NpgsqlCommand(
            "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '30s';",
            connection,
            transaction))
        {
            await deadlines.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cleanup = new NpgsqlCommand(
            "DELETE FROM idempotency_records WHERE site_id = @site_id AND key = @key AND expires_at <= now();",
            connection,
            transaction))
        {
            cleanup.Parameters.AddWithValue("site_id", siteId);
            cleanup.Parameters.AddWithValue("key", normalizedKey);
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        var existing = await ReadIdempotencyAsync(connection, transaction, siteId, normalizedKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.Value.BodyHash, bodyHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return ReopenResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_REUSED", "The idempotency key is bound to a different waybill.");
            }

            await transaction.CommitAsync(cancellationToken);
            return existing.Value.Response is null
                ? ReopenResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS", "The idempotency key is still being processed.")
                : ReopenResult.Success(existing.Value.Response with { Replayed = true });
        }

        if (!await ReserveIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return ReopenResult.Failure(StatusCodes.Status409Conflict, "IDEMPOTENCY_IN_PROGRESS", "The idempotency key is still being processed.");
        }

        // Counter first, then the projection — the same lock order ingest takes, so the two
        // can never deadlock against each other.
        var startingSequence = await ReadCounterAsync(connection, transaction, siteId, cancellationToken);
        if (startingSequence is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ReopenResult.Failure(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The site change counter has not been provisioned.");
        }

        var projection = await IngestRepository.ReadProjectionAsync(connection, transaction, siteId, normalizedWaybill, cancellationToken);
        if (projection is null)
        {
            // Gone and gone-for-good are different answers. A tombstone means the purge
            // already removed the row, so there is nothing left to reopen and a retry will
            // never help — 410, not 404.
            var tombstoned = await HasTombstoneAsync(connection, transaction, siteId, normalizedWaybill, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
            return tombstoned
                ? ReopenResult.Failure(StatusCodes.Status410Gone, "WAYBILL_PURGED", "The waybill was purged and only a tombstone remains; it cannot be reopened.")
                : ReopenResult.Failure(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "No projection exists for this waybill.");
        }

        var wasTerminal = await ReadIsTerminalAsync(connection, transaction, siteId, normalizedWaybill, cancellationToken);
        if (!wasTerminal)
        {
            // A no-op success. An operator who was not sure whether the waybill was terminal
            // must not churn the change feed by asking.
            var unchanged = new ReopenResponse(siteId, normalizedWaybill, projection.Version, null, false, false);
            await InsertIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, unchanged, cancellationToken);
            await AuditRepository.AppendAsync(
                connection, transaction, siteId, actor, "waybill.reopen_noop",
                new { waybillNo = normalizedWaybill, version = projection.Version },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ReopenResult.Success(unchanged);
        }

        if (startingSequence.Value == long.MaxValue)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ReopenResult.Failure(StatusCodes.Status500InternalServerError, "COUNTER_OVERFLOW", "The site change sequence counter has reached its maximum value and must be reset.");
        }

        var sequence = checked(startingSequence.Value + 1);
        var now = timeProvider.GetUtcNow();
        var newVersion = await ClearTerminalAsync(connection, transaction, siteId, normalizedWaybill, sequence, now, cancellationToken);

        // The feed carries the row's current body so a station applies the reopened state
        // without a second fetch. Version comes from the UPDATE's RETURNING rather than from
        // the read, because the UPDATE is what incremented it.
        var body = ProjectionBody.From(projection with { Version = newVersion }, now);
        await InsertChangeAsync(connection, transaction, siteId, sequence, normalizedWaybill, body, cancellationToken);
        await UpdateCounterAsync(connection, transaction, siteId, sequence, cancellationToken);

        var response = new ReopenResponse(siteId, normalizedWaybill, newVersion, sequence, true, false);
        await InsertIdempotencyAsync(connection, transaction, siteId, normalizedKey, bodyHash, response, cancellationToken);
        await AuditRepository.AppendAsync(
            connection, transaction, siteId, actor, "waybill.reopen",
            new { waybillNo = normalizedWaybill, version = newVersion, changeSeq = sequence },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReopenResult.Success(response);
    }

    private static async Task<(string BodyHash, ReopenResponse? Response)?> ReadIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string key,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT body_sha256, response, status_code
              FROM idempotency_records
             WHERE site_id = @site_id AND key = @key AND expires_at > now()
             FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var hash = reader.GetString(0);
        var json = reader.GetFieldValue<string>(1);
        var statusCode = reader.GetInt32(2);
        var response = statusCode == 0
            ? null
            : JsonSerializer.Deserialize<ReopenResponse>(json, JsonOptions)
              ?? throw new InvalidOperationException("Stored reopen idempotency response is invalid.");
        return (hash, response);
    }

    private static async Task<bool> ReserveIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string key,
        string bodyHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO idempotency_records (site_id, key, body_sha256, response, status_code, expires_at)
            VALUES (@site_id, @key, @body_hash, '{}'::jsonb, 0, now() + interval '24 hours')
            ON CONFLICT (site_id, key) DO NOTHING
            RETURNING 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("body_hash", bodyHash);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null and not DBNull;
    }

    private static async Task InsertIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string key,
        string bodyHash,
        ReopenResponse response,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE idempotency_records
               SET response = @response,
                   status_code = 200,
                   expires_at = now() + interval '24 hours'
             WHERE site_id = @site_id AND key = @key AND body_sha256 = @body_hash;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("body_hash", bodyHash);
        command.Parameters.Add("response", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(response, JsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long?> ReadCounterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT change_seq FROM site_change_counters WHERE site_id = @site_id FOR UPDATE;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task UpdateCounterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        long sequence,
        CancellationToken cancellationToken)
    {
        const string sql = "UPDATE site_change_counters SET change_seq = @sequence WHERE site_id = @site_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sequence", sequence);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ReadIsTerminalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        CancellationToken cancellationToken)
    {
        // A separate probe rather than an extra ordinal on ReadProjectionAsync: that reader
        // is shared with ingest, and widening it would change a hot path for one caller.
        const string sql = "SELECT is_terminal FROM waybill_projections WHERE site_id = @site_id AND waybill_no = @waybill_no;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is bool terminal && terminal;
    }

    private static async Task<bool> HasTombstoneAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM waybill_tombstones WHERE site_id = @site_id AND waybill_no = @waybill_no);";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<long> ClearTerminalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        string waybillNo,
        long sequence,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        // The only place terminal state is cleared. The version bump is what tells a station
        // its cached copy is stale; without it the change row would carry a version the
        // client already has and be discarded as a replay.
        const string sql = """
            UPDATE waybill_projections
               SET is_terminal = false,
                   terminal_at = NULL,
                   terminal_state_code = NULL,
                   last_change_seq = @sequence,
                   version = version + 1,
                   updated_at = @updated_at
             WHERE site_id = @site_id AND waybill_no = @waybill_no
            RETURNING version;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("updated_at", updatedAt);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task InsertChangeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        long sequence,
        string waybillNo,
        ProjectionBody body,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dashboard_changes (site_id, change_seq, entity_type, entity_key, operation, body)
            VALUES (@site_id, @sequence, 'waybill_projection', @waybill_no, 'upsert', @body);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("waybill_no", waybillNo);
        command.Parameters.Add("body", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(body, JsonOptions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Create `src/AutoJMS.DataHub.Api/Endpoints/ReopenEndpoints.cs`**

```csharp
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Endpoints;

public static class ReopenEndpoints
{
    public static IEndpointRouteBuilder MapReopenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Under /api/v1/admin because that prefix is the only thing
        // AdminAuthenticationMiddleware inspects: outside it the middleware returns early
        // and never sets the marker IsAdminAuthenticated() reads, so the contract's
        // /api/v1/sites/... placement would have answered 401 to every caller. Same rate
        // limiter as the other operator routes.
        endpoints.MapPost(
                "/api/v1/admin/sites/{siteId:guid}/waybills/{waybillNo}/reopen",
                (HttpContext context, Guid siteId, string waybillNo, ReopenRepository repository)
                    => HandleAsync(context, siteId, waybillNo, repository))
            .RequireRateLimiting("manifestAdmin");
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        Guid siteId,
        string waybillNo,
        ReopenRepository repository)
    {
        // Re-checked in the handler rather than trusted from the pipeline, matching
        // ManifestEndpoints: if the middleware order is ever changed, this stays closed
        // instead of silently opening an operator route to anyone.
        if (!context.IsAdminAuthenticated())
            return Problem(StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "An administrative bearer token is required.");

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 128)
            return Problem(StatusCodes.Status400BadRequest, ApiProblemCodes.BadRequest, "Idempotency-Key must contain between 8 and 128 characters.");

        // The actor is the authenticated principal, never anything the caller supplied. An
        // audit trail an operator can write their own name into records nothing.
        var result = await repository.ReopenAsync(siteId, waybillNo, idempotencyKey, "admin-token", context.RequestAborted);
        return result.Succeeded
            ? Results.Ok(result.Response)
            : Problem(result.StatusCode, result.ProblemCode ?? ApiProblemCodes.BadRequest, result.Detail ?? "Reopen failed.");
    }

    private static IResult Problem(int status, string code, string detail)
        => ApiProblemWriter.Result(status, code, detail);
}
```

- [ ] **Step 5: Register in `Program.cs`**

After `builder.Services.AddSingleton(TerminalPolicy.Empty);` (added in Task 5), insert:

```csharp
builder.Services.AddSingleton<ReopenRepository>();
```

After `app.MapManifestEndpoints();`, insert:

```csharp
app.MapReopenEndpoints();
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj --filter "FullyQualifiedName~ReopenEndpointTests"
```

Expected: PASS, 4 tests.

- [ ] **Step 7: Build Release and run the harness**

```bash
dotnet build .\AutoJMS.slnx -c Release
```

Expected: Build succeeded, 0 warnings, 0 errors.

```bash
dotnet test tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj
```

Expected: PASS, database-backed tests skipped.

```bash
powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
```

Expected: all gates PASS. If the secret gate flags anything, stop — do not stage the flagged file.

- [ ] **Step 8: Commit and push**

```bash
git add src/AutoJMS.DataHub.Api/Infrastructure/ReopenRepository.cs src/AutoJMS.DataHub.Api/Endpoints/ReopenEndpoints.cs src/AutoJMS.DataHub.Api/Program.cs tests/AutoJMS.DataHub.Api.Tests/Hosting/ReopenEndpointTests.cs
git commit -m "feat(datahub): add the operator reopen endpoint under the admin prefix"
git push origin main
git log --oneline -1
git status
```

---

## After the plan

**Phase exit (§18):** Build Release PASS, P1 tests PASS, throughput not worse than the baseline.

**Test assignment (§27 governs, not the P1 prompt's list).** P1 runs `AT-T1`, `AT-T2`, `AT-T3`, `AT-R1`–`AT-R6`, `AT-CS1`, `AT-LS3`, `AT-CK3`, plus the horizon tests carrying the prompt's names `AT-H1`, `AT-H2`, `AT-H4`. Explicitly not in P1: `AT-LS1`/`AT-LS2` (P2 — they need P2's `/waybills` read surface and ETag), `AT-CK1`/`AT-CK2` (P0 debt, never run, not part of G1–G9), `AT-TERM-LATE` (not required, because OD-6 = B).

**Still gated, and not part of this plan:** applying 007–009. The order is verify a backup → restore into a temporary instance (this also closes G4) → apply → deploy. Per D-3 that order is not interchangeable.

**Rollback.** P1 adds no runtime feature flag — the absent terminal classification is already the off switch, and a second would be dead configuration. Until the migrations are applied, reverting the commits is a complete rollback. After they are applied it still is, because they only add columns and an empty table. Once terminal or tombstone state exists — a later phase, after OD-1 — §22 forbids reversing the schema and rollback stops being symmetric.

**Stop rules.** Halt and report rather than guess if: the real schema differs from what Task 1 expects; `retention_policies` has a `waybill_projections` row; the contract contradicts the real code; a protected file is needed without permission; or any owner decision value would have to be guessed.
