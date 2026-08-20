# DataHub VPS Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and verify the new VPS-hosted DataHub backend on isolated Dev/Test and
Production deployments before changing the AutoJMS desktop integration.

**Architecture:** A .NET 10 ASP.NET Core API owns the PostgreSQL transaction boundary and SignalR hub. A single canonical ingest pipeline serves both bulk leader observations and interactive observations; only bulk requests require lease fencing. PostgreSQL is private to each Compose network, and clients use REST delta/snapshot plus SignalR doorbells. Dev/Test and Production use the same image contract with separate data, domains, credentials, and JWT identity.

**Tech Stack:** ASP.NET Core 10, SignalR, Npgsql, explicit SQL, PostgreSQL, Docker Compose, Caddy, OpenAPI 3, xUnit, Testcontainers.PostgreSql (or a local PostgreSQL test instance when container runtime is unavailable).

**Source of truth:** `docs/superpowers/specs/2026-08-20-datahub-vps-baseline-design.md`. Do not use the historical Supabase planning documents to generate this backend.

---

### Task 1: Add the forward-only PostgreSQL core migration

**Files:**
- Create: `backend/datahub/migrations/001_core.sql`
- Create: `backend/datahub/migrations/002_seed_policies.sql`
- Test: `backend/datahub/tests/001_core_smoke.sql`

- [ ] **Step 1: Write the migration smoke assertions**

Assert that the migration creates `sites`, `devices`, `site_fetch_leases`,
`site_change_counters`, `waybill_scan_events`, `waybill_projections`,
`dashboard_changes`, `jms_event_policies`, `idempotency_records`,
`retention_policies`, and `audit_logs`. Assert that `leader_device_id` is nullable,
the change cursor primary key is `(site_id, change_seq)`, no global identity is used as a
cursor, projection event references have no foreign key, and `uploadTime` has no hot
column or index.

- [ ] **Step 2: Implement schema and constraints**

Create `sites` first. Create each site together with its lease seed
(`leader_device_id = NULL`, term `0`, `lease_expires_at = '-infinity'`) and change counter
(`change_seq = 0`) in one transaction. Add the compact projection payload default,
three projection slots, policy version, idempotency body hash/response, configurable
retention columns, and partial unique indexes for global and per-site retention rows.

- [ ] **Step 3: Implement the seed policy migration**

Keep code-to-kind mappings in `002_seed_policies.sql`, including fixtures for codes 98
and 110. Unknown codes remain activity at runtime. Do not embed code mappings in DDL
constraints or scatter them through application code.

- [ ] **Step 4: Apply and verify the migration**

Run the migration twice against a clean PostgreSQL database and assert the second run is
safe according to the migration runner's forward-only policy. Run the smoke assertions
and inspect indexes with `\d+`/catalog queries.

### Task 2: Publish the phase-1 OpenAPI contract

**Files:**
- Create: `backend/datahub/openapi/datahub-v1.yaml`
- Create: `backend/datahub/openapi/README.md`
- Test: `backend/datahub/openapi/openapi-lint.ps1`

- [ ] **Step 1: Define security schemes and common errors**

Define the device bearer token, site claim matching, `409 LEADER_FENCED`,
`409 LEASE_HELD`, `409 IDEMPOTENCY_KEY_REUSED`, `400 INVALID_SCAN_TIME`, and a common
request correlation ID. Do not describe a JMS token as an API credential.

- [ ] **Step 2: Define endpoints**

Specify `devices/enroll`, lease acquire/renew/release, `/jms/ingest`,
`/jms/observations`, `/changes`, and the phase-1 snapshot endpoint. Bulk and interactive
routes must reference one `IngestPipeline` semantic contract; only bulk carries the
fencing requirement. Limit ingest requests to 1 MB and 200 items.

- [ ] **Step 3: Define snapshot and realtime messages**

Specify one phase-1 `REPEATABLE READ` snapshot response with `snapshot_seq`, keyset
pagination inside that transaction, and a SignalR doorbell containing only site,
sequence, entity type, and entity key. Document delta catch-up and the 30-60 second
safety pull.

- [ ] **Step 4: Lint the contract**

Run the repository's available OpenAPI linter or a pinned Docker linter. Fail on invalid
references, undocumented error responses, or a route that accepts site authorization
from the request body.

### Task 3: Create the API skeleton and PostgreSQL boundary

**Files:**
- Create: `src/AutoJMS.DataHub.Api/AutoJMS.DataHub.Api.csproj`
- Create: `src/AutoJMS.DataHub.Api/Program.cs`
- Create: `src/AutoJMS.DataHub.Api/appsettings.json`
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/PostgresDataSource.cs`
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/ApiProblemDetails.cs`
- Create: `src/AutoJMS.DataHub.Api/Health/PostgresHealthCheck.cs`
- Create: `src/AutoJMS.DataHub.Api/Hubs/SiteHub.cs`
- Modify: `AutoJMS.slnx`
- Test: `tests/AutoJMS.DataHub.Api.Tests/AutoJMS.DataHub.Api.Tests.csproj`

- [ ] **Step 1: Add the server project and test project**

Target `net10.0`. Reference only the packages required for ASP.NET Core, Npgsql,
SignalR, and test hosting. Do not reference Supabase client packages or the WinForms
project.

- [ ] **Step 2: Configure the data source and health checks**

Register one singleton `NpgsqlDataSource` with bounded pooling. `/health/live` must not
require PostgreSQL; `/health/ready` must fail when PostgreSQL cannot be reached.

- [ ] **Step 3: Add authentication and tenant authorization seams**

Implement a testable device-token validator interface. Every site route compares the
route site ID with the authenticated device site ID. Keep token issuance/enrollment
behind an interface so the existing license server can be adapted later without
putting license changes in this phase.

- [ ] **Step 4: Add SignalR site grouping**

Join `site:{site_id}` from authenticated claims only. Redact access tokens from request
logs and never send JMS credentials through the hub.

### Task 4: Implement lease, idempotency, parsing, and the canonical ingest pipeline

**Files:**
- Create: `src/AutoJMS.DataHub.Api/Domain/ScanTimeParser.cs`
- Create: `src/AutoJMS.DataHub.Api/Domain/EventFingerprintV1.cs`
- Create: `src/AutoJMS.DataHub.Api/Domain/JmsEventPolicy.cs`
- Create: `src/AutoJMS.DataHub.Api/Domain/ProjectionReducer.cs`
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/LeaseRepository.cs`
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/IngestRepository.cs`
- Create: `src/AutoJMS.DataHub.Api/Endpoints/LeaseEndpoints.cs`
- Create: `src/AutoJMS.DataHub.Api/Endpoints/IngestEndpoints.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Domain/ScanTimeParserTests.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Domain/ProjectionReducerTests.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Infrastructure/IngestConcurrencyTests.cs`

- [ ] **Step 1: Write parser and fingerprint tests first**

Cover naive `yyyy-MM-dd HH:mm:ss` in `Asia/Ho_Chi_Minh`, explicit `Z`, explicit offsets,
invalid/empty values, rejection without current-time fallback, exclusion of `uploadTime`,
stable canonical field order, and fingerprint versioning.

- [ ] **Step 2: Implement the parser and fingerprint**

Return UTC `DateTimeOffset` for valid values. Record a format-deviation diagnostic for
offset-bearing input. Reject invalid items. Canonicalize fixed v1 fields and normalize
missing fields as empty strings.

- [ ] **Step 3: Write reducer tests**

Cover code 98 as inventory/activity without changing state, code 110 as state, unknown
codes as activity, delayed observations, equal `scanTime` deterministic tie-breaking,
three independent slots, and rebuild with the same observations producing the same
projection.

- [ ] **Step 4: Implement the reducer**

Use `(event_occurred_at, event_fingerprint)` independently for state, activity, and
inventory winners. Store only the compact projection snapshot and nullable event IDs;
never compare `uploadTime` or `ingested_at`.

- [ ] **Step 5: Write lease/concurrency tests**

Cover first acquire, concurrent acquire, renew without term increment, release with term
increment and null owner, old-term fencing, expired steal, duplicate idempotency key,
same key with different body hash, and bulk/interactive calls sharing one pipeline.

- [ ] **Step 6: Implement one transaction pipeline**

Validate device/site, apply bulk fencing only for `/jms/ingest`, check idempotency,
insert events with `ON CONFLICT DO NOTHING`, reduce affected projections, lock the
per-site counter, append a complete hot-column `dashboard_changes.body`, and commit.
Publish the SignalR doorbell only after commit.

### Task 5: Implement changes, snapshot, and maintenance behavior

**Files:**
- Create: `src/AutoJMS.DataHub.Api/Endpoints/SyncEndpoints.cs`
- Create: `src/AutoJMS.DataHub.Api/Services/SignalRDoorbellPublisher.cs`
- Create: `src/AutoJMS.DataHub.Api/Services/RetentionWorker.cs`
- Create: `src/AutoJMS.DataHub.Api/Infrastructure/ChangeRepository.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Sync/ChangeCursorTests.cs`
- Test: `tests/AutoJMS.DataHub.Api.Tests/Sync/SnapshotWatermarkTests.cs`

- [ ] **Step 1: Write cursor tests**

Cover per-site cursors, changes from multiple sites, counter serialization, rollback,
out-of-order concurrent transactions, complete `body` application, tombstones, and
retention behavior without a duplicate cursor index.

- [ ] **Step 2: Implement delta reads**

Return `change_seq > after` for the authenticated site, ordered by `change_seq`, with
bounded page size. Never require `after + 1`.

- [ ] **Step 3: Implement the phase-1 snapshot**

Stream all pages from one `REPEATABLE READ` transaction and return one `snapshot_seq`.
Do not introduce snapshot tokens in this phase.

- [ ] **Step 4: Implement the doorbell and safety pull contract**

Send only a high-watermark/key notification after commit. Reconnect paths always run a
delta catch-up; maintenance does not trigger full snapshots unless the cursor is outside
the retained range.

- [ ] **Step 5: Implement policy-driven retention**

Use allowlisted table/clock policies, bounded delete batches, tombstones for offline
clients, and no free-form SQL from policy rows. Never delete lease/counter/site rows.

### Task 6: Add Compose, Caddy, environment isolation, backup, and backend verification

**Files:**
- Create: `backend/datahub/docker-compose.yml`
- Create: `backend/datahub/Caddyfile`
- Create: `backend/datahub/.env.example`
- Create: `backend/datahub/README.md`
- Create: `backend/datahub/deploy/environment-runbook.md`
- Create: `backend/datahub/scripts/backup.ps1`
- Create: `backend/datahub/scripts/restore-drill.ps1`
- Create: `backend/datahub/tests/cutover-checklist.md`

- [ ] **Step 1: Define private Compose networking and environment targets**

Publish only Caddy ports 80/443. API connects to `postgres` by Compose DNS. PostgreSQL
has no host port mapping. Add health checks and bounded memory/log settings. Document
two independent env files: Dev/Test uses a dev hostname, database volume, JWT
issuer/audience, signing keys, site/device registry, and `DataHubApiBaseUrl`; Production
uses a separate set. The API must fail readiness on missing/mismatched environment
identity. Never put either secret set in git.

- [ ] **Step 2: Define image promotion and encrypted backup/restore**

Build one immutable image digest and deploy it to Dev first. Back up user/config/audit/
device/site data and observations while replay coverage is not measured. Keep credentials
out of dumps. Restore to a clean Dev database, replay a fixture, rebuild projections, and
compare expected dashboard rows. Promote the exact tested digest to Production only after
the Production backup succeeds; a replacement VPS reuses the Production hostname and
secrets rather than changing client endpoints.

- [ ] **Step 3: Run backend verification on Dev, then Production**

Run SQL smoke tests, unit tests, API integration tests, OpenAPI lint, Compose health
checks, duplicate-batch tests, leader crash/fencing tests, SignalR reconnect tests, and
the drop/restore/replay drill on Dev. Confirm that Dev cannot reach Production's
PostgreSQL or accept Production device tokens. Repeat the required health, migration,
backup, and canary checks on Production. Do not modify the desktop integration during
this task.

### Task 7: Backend completion gate before desktop integration

**Files:**
- Modify: `docs/superpowers/specs/2026-08-20-datahub-vps-baseline-design.md` only if a verified implementation discrepancy requires clarification.
- Create: `backend/datahub/tests/canary-report-template.md`

- [ ] **Step 1: Run one-site canary against two devices**

Run the canary against Dev first, using only Dev endpoint/device credentials. Verify bulk
leader failover, local interactive observations, scanTime parsing, code
98/110 slot behavior, duplicate bulk/interactive observations, reconnect catch-up,
snapshot watermark, old-term fencing, and VPS restart.

- [ ] **Step 2: Complete restore drill and record RPO/RTO**

Record the measured restore duration and the observed JMS replay window. Do not claim
the backend is production-ready until both are recorded.

- [ ] **Step 3: Gate desktop integration**

Only after Tasks 1-6 pass on Dev and the Production promotion/canary is recorded should
a separate plan replace the existing
Supabase adapter with the new HTTP/SignalR client and wire the WebView2/Named Pipe
worker lifecycle. Protected licensing, update, and designer files remain untouched.
