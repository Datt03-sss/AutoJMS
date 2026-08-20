# AutoJMS DataHub VPS Baseline Design

> Status: owner-approved architecture baseline, 2026-08-20.
> This document is the implementation source of truth for the new VPS data path.
> Older Supabase/DataHub planning documents remain historical and must not be used
> to generate the phase-1 SQL or API contract.

## Goal

Provide a durable, tenant-isolated dashboard backend for multiple AutoJMS clients that
are not on the same LAN, while keeping the JMS credential local to each Windows machine.
The first production deployment runs on the existing VPS. A second, fully isolated VPS
is used for development and integration testing. Either deployment can be rebuilt from
its own encrypted offsite backups plus replayable JMS observations; the development VPS
is not a production standby.

## Non-goals for phase 1

- No database or schema per site.
- No direct PostgreSQL connection from desktop clients.
- No Supabase DataHub dependency.
- No Redis, Kafka, PgBouncer, partitioning, Kubernetes, or chat/note tables before those
  features exist.
- No Strict leader command queue. Interactive JMS calls remain local to the operator's
  machine.

## Runtime architecture

```text
Client (WinForms/WebView2)        Windows Service (per machine)
        | Named Pipe (local)               | JMS token in DPAPI
        | REST + SignalR                   | bulk/interactive JMS
        +---------------- HTTPS/WSS --------+
                         |
                  Caddy :443
                         |
             ASP.NET Core DataHub API
             + SignalR + maintenance job
                         |
                    PostgreSQL
                 (private Docker network)
```

Only Caddy publishes ports 80/443. API and PostgreSQL are internal Compose services.
PostgreSQL is never exposed on host port 5432.

Recommended implementation stack:

- ASP.NET Core 10 Web API and SignalR for the new server project.
- Npgsql with one shared `NpgsqlDataSource`; Dapper or explicit SQL for the ingest path.
- PostgreSQL 17 or the pinned supported major selected during deployment.
- Docker Compose, Caddy, and an encrypted object-storage backup target.

## Environment separation and promotion

The two VPS deployments use the same Compose topology and application image, but they
are separate security and data boundaries:

| Environment | Public endpoint | PostgreSQL | Credentials and identity |
|---|---|---|---|
| Dev/Test | dedicated dev hostname | dedicated dev volume/database | dev JWT issuer/audience, signing keys, enroll pepper, sites/devices, and backup bucket |
| Production | dedicated production hostname | dedicated production volume/database | production JWT issuer/audience, signing keys, enroll pepper, sites/devices, and backup bucket |

There is no network route, shared volume, shared database credential, shared device
credential, or shared backup bucket between the environments. Dev fixtures must be
synthetic or anonymized; a production dump is never restored into dev without an explicit
sanitization step.

Configuration has two distinct targets:

- The API's `ConnectionStrings__DataHub` points to the private Compose service
  `Host=postgres` in that environment. It never points to a public PostgreSQL address.
- The client and Windows Service resolve an environment-specific DataHub URL from the
  signed license `datahub_url` override, falling back to the well-known URL for its
  signed `channel`. A manually entered host or unsigned remote response is rejected.
  A production device token is never sent to dev.
- `ASPNETCORE_ENVIRONMENT` is `Staging` on Dev/Test and `Production` on Production.
  JWT issuer/audience, signing keys, and Caddy hostname are supplied by deployment
  secrets/configuration outside git. Missing or mismatched environment identity must
  fail readiness, not silently fall back to another target.

Promotion is image-based: build one immutable image digest, deploy and test it on Dev,
run a synthetic two-device canary there, back up Production, apply forward-only
migrations, then deploy the exact digest to Production and run the real-user canary.
A production replacement VPS reuses the
production hostname and secrets after restore; clients do not need a new endpoint. Dev
may be stopped or downsized when not testing, but it is not an automatic failover target.

### Signed license channel boundary

The license authority adds these signed fields; the client does not invent them:

```text
channel: production | staging                 required
datahub_url: https URL override                optional, signed
site_codes: [licensed site codes]              required for DataHub access
exp, seats, token_version                       existing license claims
```

`channel` is a DataHub deployment channel and is distinct from the existing binary
update channel (`stable`/`beta`). It must not reuse or overload `UpdateChannel`.

The client resolves `datahub_url ?? wellKnown[channel]`. The override is accepted only
after license signature, expiry, and channel validation, and must be HTTPS. The license
never contains an IP address, JMS token, PostgreSQL password, DPAPI blob, or device
secret. DNS replacement therefore does not require reissuing a license.

The API has a deployment setting `DATAHUB_CHANNEL=production|staging`. Enrollment
validates the signed license assertion, requires `license.channel == DATAHUB_CHANNEL`,
and requires the requested `site_code` to be in `license.site_codes`. It then issues a
device token containing the bound channel and site. Lease, ingest, and SignalR requests
validate that signed/derived claim against `DATAHUB_CHANNEL`; a raw `X-DataHub-Channel`
header or request body is never authoritative. Mismatch returns `403 CHANNEL_MISMATCH`;
an unlicensed site returns `403 SITE_NOT_LICENSED`.

Phase 1 does not require a `devices.channel` column. The bound channel is in the device
token and enrollment audit payload; a future audit migration may add a non-null column.
The two environments use different JWT signing keys and enrollment peppers, so a device
enrolled in staging is invalid in production even if configuration files are copied.

The existing .NET 8 desktop remains a client of this API; it does not need to target the
same framework version.

## Authority and data classes

```text
jms_observation  Windows Service <- JMS; replayable while JMS history is available
user_input       operator/dashboard; durable and non-disposable
derived          reducer output; rebuildable from observations
audit_config     API-managed configuration/audit; durable and non-disposable
```

The VPS is not described as a universal source of truth. Windows Service is the source
of JMS observations. PostgreSQL is the source of truth for user input, configuration,
and audit data. Projections are derived read models.

## Tenant and identity rules

- `sites.id` is an immutable UUID (or the existing immutable ID type if the deployment
  already has one). `site_code` is unique and may change.
- Every tenant-owned foreign key uses `site_id`, never `site_code` alone.
- The API derives `site_id` from the authenticated device token. A path site ID must
  match the token claim; a body field cannot grant access.
- A device token is separate from the JMS token. It contains `device_id`, `site_id`,
  `role`, and `token_version` and is revocable independently.
- SignalR connections join `site:{site_id}` based on server-side claims.

## Phase-1 schema

Required tables:

```text
sites
devices
site_fetch_leases
site_change_counters
waybill_scan_events
waybill_projections
dashboard_changes
jms_event_policies
idempotency_records
retention_policies
audit_logs
```

`user_notes`, `chat_messages`, and other user feature tables are added only when the
feature is implemented.

Creating a site is one transaction that inserts the `sites` row, its empty
`site_fetch_leases` row, and its `site_change_counters` row with `change_seq = 0`.

### Observation

`waybill_scan_events` is append-only except for retention. Its business timestamp is
`event_occurred_at`, parsed from JMS `scanTime`.

```sql
CREATE TABLE waybill_scan_events (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id             uuid NOT NULL REFERENCES sites(id),
    waybill_no          text NOT NULL,
    event_fingerprint   text NOT NULL,
    fingerprint_version smallint NOT NULL DEFAULT 1,
    event_occurred_at   timestamptz NOT NULL,
    ingested_at         timestamptz NOT NULL DEFAULT now(),
    scan_type_code      integer,
    scan_type_name      text,
    status              text,
    network_code        text,
    operator_code       text,
    package_number      text,
    task_code           text,
    payload             jsonb NOT NULL,
    UNIQUE (site_id, event_fingerprint)
);
CREATE INDEX ix_waybill_scan_events_site_waybill_time
    ON waybill_scan_events (site_id, waybill_no, event_occurred_at);
```

`uploadTime` may remain inside `payload` for diagnostics, but it is not a hot column,
index key, fingerprint input, reducer input, or retention clock.

### Projection

The projection has three independent slots. An unknown JMS code defaults to `activity`
and cannot overwrite state or inventory.

```sql
CREATE TABLE waybill_projections (
    site_id                    uuid NOT NULL REFERENCES sites(id),
    waybill_no                 text NOT NULL,
    state_code                 integer,
    state_name                 text,
    state_event_at             timestamptz,
    state_fingerprint          text,
    state_event_id             bigint,
    state_kind                 text,
    last_activity_code         integer,
    last_activity_name         text,
    last_activity_kind         text,
    last_activity_at           timestamptz,
    last_activity_fingerprint  text,
    last_activity_event_id     bigint,
    inventory_code             integer,
    inventory_name             text,
    inventory_event_at         timestamptz,
    inventory_fingerprint      text,
    inventory_event_id         bigint,
    payload                    jsonb NOT NULL DEFAULT '{}'::jsonb,
    reducer_version            integer NOT NULL DEFAULT 1,
    version                    bigint NOT NULL DEFAULT 1,
    updated_at                 timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (site_id, waybill_no)
);
```

The projection is self-sufficient for dashboard list/detail rendering. The event ID
columns are optional hydration references and deliberately have no foreign key to
`waybill_scan_events`: event retention must not break a non-terminal projection. A
retention job may set those references to `NULL`, or retain referenced events while the
projection is non-terminal. The compact `payload` is the apply-safe projection snapshot,
not a copy of the full JMS envelope.

Each slot uses the deterministic winner key:

```text
(event_occurred_at, event_fingerprint)
```

`ingested_at` and `uploadTime` never participate. If JMS later supplies a stable source
event ID, it is added to the canonical fingerprint in a new fingerprint version.

Slot semantics are fixed:

| Slot | Updated when | Purpose |
|---|---|---|
| `current_state_*` | `state_transition` | business state |
| `latest_activity_*` | every event kind | newest activity, including inventory |
| `inventory_*` | `inventory` | newest inventory checkpoint |

`communication` updates only `latest_activity_*`. The event policy is data in
`jms_event_policies`; it is not scattered through reducer code.

### Policy and change cursor

```sql
CREATE TABLE jms_event_policies (
    reducer_version integer NOT NULL,
    scan_type_code  integer NOT NULL,
    event_kind      text NOT NULL CHECK (event_kind IN
                    ('state_transition', 'activity', 'inventory', 'communication')),
    PRIMARY KEY (reducer_version, scan_type_code)
);

CREATE TABLE site_change_counters (
    site_id     uuid PRIMARY KEY REFERENCES sites(id),
    change_seq  bigint NOT NULL DEFAULT 0
);

CREATE TABLE dashboard_changes (
    site_id      uuid NOT NULL REFERENCES sites(id),
    change_seq   bigint NOT NULL,
    entity_type  text NOT NULL,
    entity_key   text NOT NULL,
    operation    text NOT NULL CHECK (operation IN ('upsert', 'delete', 'resync')),
    change_at    timestamptz NOT NULL DEFAULT now(),
    body         jsonb NOT NULL DEFAULT '{}'::jsonb,
    PRIMARY KEY (site_id, change_seq)
);
```

For an `upsert`, `body` contains the complete hot-column snapshot needed to apply the
projection to SQLite in one pass. It never contains the full JMS JSON. A delete carries
an applicable tombstone body. The primary key already supplies the `(site_id,
change_seq)` access path; no duplicate index is created.

Every change is appended through one repository/database function that locks the
site counter, allocates the next number, inserts the change, and commits in the same
transaction as the projection update. No other code may insert directly into
`dashboard_changes`. The cursor is per-site and no API rule assumes numeric contiguity.

## Time parsing contract

The parser never uses the VPS timezone, `TimeZoneInfo.Local`, or `DateTime.Now`.

```text
SCAN_TIME_ZONE = Asia/Ho_Chi_Minh

yyyy-MM-dd HH:mm:ss with no offset:
  interpret as Asia/Ho_Chi_Minh, then convert to UTC

ISO value with Z or an explicit offset:
  honor the supplied offset; do not add +07:00

empty or invalid value:
  reject that item and report a parse error; never substitute current time
```

The first live canary must capture 3-5 JMS payloads and record the observed format. A
format deviation is observable and does not silently change the parser contract.

## Producer and leader contract

There are two producer paths and one canonical ingest pipeline:

| Path | Caller | Endpoint | Fencing |
|---|---|---|---|
| Bulk dashboard fetch | Windows Service holding site lease | `/jms/ingest` | Required |
| Interactive operation | Windows Service on operator machine | `/jms/observations` | Not required |

The UI never bulk-polls JMS. Interactive UI commands use a local ACL-protected Named
Pipe to the Windows Service; the JMS token stays in the service's DPAPI store. Both
endpoints call the same `IngestPipeline`; only the bulk endpoint performs lease fencing.

Lease defaults:

```text
duration: 120 seconds
renew:     30 seconds
```

`site_fetch_leases` is seeded when a site is created and is never deleted:

```text
leader_device_id: NULL
leader_term:      0
lease_expires_at: -infinity
last_seen_at:     NULL
```

Acquire, renew, steal, and release are serialized with a row lock. Stealing an expired
lease increments `leader_term`; renew does not. Release also increments `leader_term`,
sets `leader_device_id = NULL`, and sets `lease_expires_at = -infinity`; an in-flight
request with the old term is fenced. Bulk ingest requires matching device, term, and
unexpired lease. A stale request receives `409 LEADER_FENCED`. API/network failure
pauses bulk ingestion; it never grants a local fail-open lease.

The remaining phase-1 tables have these required columns:

```sql
CREATE TABLE sites (
    id         uuid PRIMARY KEY,
    site_code  text NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE devices (
    id              uuid PRIMARY KEY,
    site_id         uuid NOT NULL REFERENCES sites(id),
    name            text NOT NULL,
    credential_hash text NOT NULL,
    token_version   integer NOT NULL DEFAULT 1,
    status          text NOT NULL DEFAULT 'active',
    last_seen_at    timestamptz,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    UNIQUE (site_id, name)
);

CREATE TABLE site_fetch_leases (
    site_id           uuid PRIMARY KEY REFERENCES sites(id),
    leader_device_id  uuid NULL REFERENCES devices(id),
    leader_term       bigint NOT NULL DEFAULT 0,
    lease_expires_at  timestamptz NOT NULL DEFAULT '-infinity',
    last_seen_at      timestamptz NULL
);

CREATE TABLE idempotency_records (
    site_id      uuid NOT NULL REFERENCES sites(id),
    key          text NOT NULL,
    body_sha256  text NOT NULL,
    response     jsonb NOT NULL,
    status_code  integer NOT NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    expires_at   timestamptz NOT NULL,
    PRIMARY KEY (site_id, key)
);

CREATE TABLE retention_policies (
    id             bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id        uuid NULL REFERENCES sites(id),
    table_name     text NOT NULL,
    clock_column   text NOT NULL,
    hot_after      interval,
    archive_after  interval,
    delete_after   interval,
    UNIQUE (site_id, table_name)
);

-- PostgreSQL permits multiple NULLs in a normal UNIQUE constraint. Keep one
-- global policy per table and one policy per site/table explicitly.
CREATE UNIQUE INDEX ux_retention_policies_global_table
    ON retention_policies (table_name)
    WHERE site_id IS NULL;
CREATE UNIQUE INDEX ux_retention_policies_site_table
    ON retention_policies (site_id, table_name)
    WHERE site_id IS NOT NULL;

CREATE TABLE audit_logs (
    id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id    uuid NULL REFERENCES sites(id),
    actor      text NOT NULL,
    action     text NOT NULL,
    at         timestamptz NOT NULL DEFAULT now(),
    payload    jsonb NOT NULL DEFAULT '{}'::jsonb
);
```

The global retention row uses `site_id IS NULL`; the partial unique indexes above make
the global and per-site scopes unambiguous. The SQL migration must create `sites` before
the tables that reference it, then seed the lease and change-counter rows in the same
site-creation transaction:

```sql
BEGIN;
INSERT INTO sites (id, site_code) VALUES ($site_id, $site_code);
INSERT INTO site_fetch_leases (site_id) VALUES ($site_id);
INSERT INTO site_change_counters (site_id, change_seq) VALUES ($site_id, 0);
COMMIT;
```

## Ingest transaction

For every bounded chunk (maximum 1 MB or 200 items):

```text
authenticate device and site
validate the lease fence only for `/jms/ingest`
validate idempotency key and request hash
parse scanTime
insert each event with ON CONFLICT DO NOTHING
reduce every affected waybill and every projection slot
lock site_change_counters and append changes for changed projections
commit
send a small SignalR doorbell after commit
```

Fingerprint v1 canonicalizes fixed fields, including `scanTime`, code/status, scan type,
network/operator/package/task fields, and `remark1..remark9`; it excludes `uploadTime`.
Same idempotency key with a different request hash returns `409 IDEMPOTENCY_KEY_REUSED`.

## Snapshot, delta, and realtime

```text
SignalR doorbell -> GET /changes?after=cursor -> apply SQLite transactionally
```

SignalR payload is only `{siteId, changeSeq, entityType, entityKey}`. On reconnect or a
missed notification, the client performs delta catch-up. A safety pull runs every
30-60 seconds.

Phase 1 chooses one snapshot strategy: the API streams all snapshot pages in one
`REPEATABLE READ` transaction and returns one `snapshot_seq` for the entire response.
The client applies the streamed pages, sets its cursor to that watermark, and then
requests changes after it. A server snapshot token with a short TTL is a later
optimization; implementors must not choose a different paging strategy ad hoc.

## Retention and recovery

Retention is data in `retention_policies`, not hard-coded application constants. Initial
defaults are:

```text
waybill_scan_events:  delete after 60 days, only after JMS replay is measured
waybill_projections:  retain while non-terminal; terminal cleanup is policy-driven
dashboard_changes:    retain at least 14 days plus maximum offline window
audit_logs:           default 90 days, configurable
sites/devices/config: no automatic deletion
```

Deleting a projection or change visible to offline clients requires a tombstone change
retained for at least the maximum supported offline period.

Backups are encrypted and stored outside the VPS. They must include user/config/audit/
device/site data. Observation dumps remain mandatory until the JMS replay window has
been measured. A daily dump implies an explicit maximum RPO of 24 hours; it is not HA.
Restore drill: before canary and monthly thereafter.

## Phase-1 API surface

```text
POST /api/v1/devices/enroll
POST /api/v1/sites/{siteId}/lease/acquire
POST /api/v1/sites/{siteId}/lease/renew
POST /api/v1/sites/{siteId}/lease/release
POST /api/v1/sites/{siteId}/jms/ingest
POST /api/v1/sites/{siteId}/jms/observations
GET  /api/v1/sites/{siteId}/changes?after=&limit=
GET  /api/v1/sites/{siteId}/projections/snapshot
GET  /health/live
GET  /health/ready
HUB  /hubs/site
```

All endpoints derive site authorization from the device token. The API does not expose
PostgreSQL credentials or raw JMS tokens.

## Cutover and verification

1. Deploy the same API/PostgreSQL/Caddy Compose stack independently on Dev and Production.
2. Configure test licenses with `channel=staging`; configure the canary and all real users
   with `channel=production`. Do not dual-write Dev to Production.
3. Add parser and policy tests using live-format payload fixtures.
4. Install the Windows Service on one real production canary site and test Named Pipe/DPAPI.
5. Run bulk and interactive observations through the new pipeline while the old read
  path remains active.
6. Verify duplicate batches, delayed events, unknown codes, concurrent leader election,
  old-term fencing, reconnect/catch-up, snapshot watermark, and VPS restart.
7. Run the documented Dev drop/restore/replay drill, then the Production restore drill.
8. Switch one site's dashboard reads to the Production API, then expand site by site.

The first implementation must not modify protected licensing, update, or WinForms
designer files. Adding these signed license fields to the existing license engine and
license server is a later, explicitly authorized integration task; it must preserve the
existing update-channel semantics. Client integration is an adapter change after the
backend contract and canary tests pass.
