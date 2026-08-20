# DataHub API contract

`datahub-v1.yaml` is the phase-1 HTTP and SignalR contract for the new VPS
DataHub. It is derived from the owner-approved VPS baseline. Historical Supabase
documents are not inputs to this contract.

## Environments

There are two independent deployments:

| Environment | URL | API setting |
| --- | --- | --- |
| Staging/dev | `https://datahub-dev.example.com` | `DATAHUB_CHANNEL=staging` |
| Production | `https://datahub.example.com` | `DATAHUB_CHANNEL=production` |

Each deployment has its own PostgreSQL volume, JWT issuer/audience and signing
keys, enrollment pepper, site/device rows, and encrypted backup bucket. A staging
device token is not valid in production. The same image may be promoted after the
staging checks pass, but data and credentials are never shared. Replacing a VPS
means restoring its backup and moving the stable DNS name; the license does not
contain an IP address.

## Authentication and channel binding

1. `POST /api/v1/devices/enroll` accepts one signed license assertion in the
   `Authorization` bearer value. The assertion must contain `channel` (`staging`
   or `production`), `site_codes`, and `exp`. It may contain a signed HTTPS
   `datahub_url` endpoint override. The API checks the signature and expiry,
   requires the channel to equal its `DATAHUB_CHANNEL`, and requires the requested
   existing `siteCode` to be in `site_codes`.
2. Enrollment returns a derived device bearer token. All later lease, JMS, delta,
   snapshot, and SignalR calls use that token. A license assertion is never an API
   credential after enrollment; a JMS auth token is never an API credential.
3. Device claims bind `device_id`, `site_id`, `channel`, role, and token version.
   A path `siteId` must equal the claim. The server derives the SignalR group from
   the claim.

`channel` and site values in arbitrary request bodies or headers are not authority.
The API ignores them for authorization and returns `CHANNEL_MISMATCH` when the
signed/derived channel does not match the deployment, or `SITE_NOT_LICENSED` when
the signed license does not include the requested site. Clients must not expose a
free-form host field to operators; `datahub_url` is accepted only as a signed
license claim and must be HTTPS.

## Ingest contract

`/jms/ingest` and `/jms/observations` call one semantic `IngestPipeline`:

```text
authenticate -> validate request hash/idempotency -> parse scanTime
-> fingerprint -> insert observation -> reduce state/activity/inventory slots
-> allocate site change sequence and append body -> commit -> SignalR doorbell
```

The bulk route is used by the Windows Service holding the site lease and requires
`X-Leader-Term`. The interactive route is used by the service on the operator's
machine and has no lease fence. Both routes require `Authorization` and
`Idempotency-Key`. A key is bound to the SHA-256 body hash for 24 hours; reusing a
key with another body returns `409 IDEMPOTENCY_KEY_REUSED`.

Each request is limited to 1 MiB and 200 items. The service chunks a larger JMS
response and creates one idempotency key per chunk. The top-level observation is a
normalized object. The raw JMS object is retained under `payload`; `uploadTime` is
allowed only there and is never a hot column, index, fingerprint input, reducer
ordering value, or retention clock.

`scanTime` is the business time. A naive `yyyy-MM-dd HH:mm:ss` value is interpreted
as `Asia/Ho_Chi_Minh` and stored as UTC. ISO values carrying `Z` or an explicit
offset use that offset. Empty or invalid values fail the item with
`400 INVALID_SCAN_TIME`; the API never substitutes server-local time.

The reducer maintains independent winners keyed by
`(event_occurred_at, event_fingerprint)`:

- `current_state_*`: only `state_transition` policy events;
- `latest_activity_*`: every event kind, including communication and inventory;
- `inventory_*`: only `inventory` policy events.

Unknown codes default to activity. `ingested_at` and `uploadTime` never decide a
winner. The `dashboard_changes.body` for an upsert is the complete compact hot
projection snapshot, so a client can apply a delta without fetching each key.

## Delta, snapshot, and SignalR

`GET /api/v1/sites/{siteId}/changes?after=N&limit=500` returns rows with
`changeSeq > N` for that site. The cursor is per-site and allocated by a locked
counter in the same transaction as the projection update. Clients must not require
`N + 1`; the HTTP response is authoritative.

`GET /api/v1/sites/{siteId}/projections/snapshot` is phase-1 one-response streaming:
the API keeps one PostgreSQL `REPEATABLE READ` transaction, captures one
`snapshot_seq`, reads all pages in keyset order, and returns that watermark with
the projection rows. After applying the response, the client sets its cursor to
`snapshot_seq` and reads `/changes?after=snapshot_seq`. Snapshot tokens and
multi-request snapshots are intentionally deferred.

The SignalR hub is `/hubs/site` (with the normal `/hubs/site/negotiate` endpoint).
After commit it sends only this doorbell message:

```json
{
  "siteId": "00000000-0000-0000-0000-000000000000",
  "changeSeq": 42,
  "entityType": "waybill_projection",
  "entityKey": "862229607222"
}
```

The message contains no projection data. On connect, reconnect, missed messages,
or the 30-60 second safety pull, fetch HTTP changes from the local cursor. The
server chooses the `site:{siteId}` group from claims; a client cannot subscribe to
another site.

## Errors

All API errors use `application/problem+json` with `code` and `traceId`. The
contract defines these important outcomes:

| Status | Code | Meaning |
| ---: | --- | --- |
| 400 | `INVALID_SCAN_TIME` | JMS `scanTime` cannot be parsed |
| 401 | `UNAUTHORIZED` | Missing or invalid bearer credential |
| 403 | `CHANNEL_MISMATCH` | Signed/device channel differs from `DATAHUB_CHANNEL` |
| 403 | `SITE_NOT_LICENSED` | Requested site is outside signed `site_codes` |
| 404 | `NOT_FOUND` | Site/device/resource does not exist |
| 409 | `LEADER_FENCED` | Bulk term/device/lease is stale |
| 409 | `LEASE_HELD` | Another device holds an unexpired lease |
| 409 | `IDEMPOTENCY_KEY_REUSED` | Same key has a different body hash |
| 409 | `RESYNC_REQUIRED` | Cursor is older than retained changes |
| 413 | `PAYLOAD_TOO_LARGE` | Body exceeds 1 MiB or 200 items |
| 422 | `VALIDATION_FAILED` | Schema or domain validation failed |
| 429 | `RATE_LIMITED` | Caller exceeded a bounded rate |
| 503 | `SERVICE_UNAVAILABLE` | API/PostgreSQL dependency is unavailable |

`/health/live` is process liveness and does not require PostgreSQL. `/health/ready`
returns 503 when PostgreSQL, required secrets, or channel configuration is not
ready; it must never fall back to another environment.

## Linting

Run the deterministic contract check from the repository root:

```powershell
pwsh .\backend\datahub\openapi\openapi-lint.ps1
```

The script uses a locally available `redocly` CLI, or `npx --no-install` when the
Redocly package is already available. It never downloads a package implicitly. If
no linter is installed, it performs static contract checks for required routes,
security schemes, headers, limits, errors, cursor/snapshot semantics, and the
absence of an `uploadTime` hot field. A non-zero exit code blocks the API build.
