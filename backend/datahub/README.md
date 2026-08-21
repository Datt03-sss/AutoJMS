# DataHub VPS Deployment

This directory contains the phase-1 API contract, PostgreSQL migrations, and the
two-stack Docker deployment. Staging and production are independent deployments:
they must not share a database, device token key, enrollment pepper, license key,
or backup bucket.

## First staging boot

1. Copy `.env.staging.example` to an untracked `.env.staging` on the staging VPS.
2. Create the Docker network/volume and start PostgreSQL:
   `docker compose --env-file .env.staging up -d postgres`.
3. Apply migrations from an ops shell. Because PostgreSQL has no host port, use
   the running container's `psql` binary:
   `pwsh ./scripts/apply-migrations.ps1 -ComposeFile ./docker-compose.yml -ComposeEnvFile ./.env.staging`.
   Direct `psql` mode remains available when `DatabaseUrl` points at a reachable
   managed or forwarded database.
4. Run the catalog assertions and provision each site with `provision-site.ps1`:
   `pwsh ./scripts/provision-site.ps1 -SiteId <uuid> -SiteCode 272C03 -ComposeFile ./docker-compose.yml -ComposeEnvFile ./.env.staging`.
   For the compose database, stream the assertion file through the service:
   `Get-Content -Raw ./tests/001_core_catalog_assertions.sql | docker compose --env-file ./.env.staging exec -T postgres sh -ec 'exec psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 --file -'`.
5. Start the API and Caddy, then wait for `/health/ready` to return 200:
   `pwsh ./scripts/start-stack.ps1 -ComposeEnvFile ./.env.staging`.

The API does not auto-create sites during enrollment. A staging test assertion
issuer is enabled only when `ASPNETCORE_ENVIRONMENT=Staging` and the explicit flag
is true. It is not a production license verifier.
For a staging canary, issue a short-lived assertion from the operator shell with
`pwsh ./scripts/issue-staging-assertion.ps1`; keep the signing key only in the
untracked VPS environment and never paste the assertion into logs or source.

Production readiness intentionally remains red until the existing license
authority is wired behind `ILicenseAssertionValidator` with asymmetric issuer/
JWKS validation. The production HMAC-shaped test seam is fail-closed and cannot
enroll devices; do not bypass this gate by placing a shared key in the env file.

## Client and Windows Service boundary

The desktop application and its Windows Service never receive PostgreSQL
credentials. After WebView2 login, the desktop relays the local JMS token to the
service through an ACL-protected local channel; the service keeps that token in
its machine-protected store and never sends it to this API. The service uses the
derived device token for API calls:

- one service/device per site may hold the 120-second bulk lease (renew every 30
  seconds); every bulk request carries the current fencing term;
- when the JMS token is invalid, the service pauses bulk fetch and releases the
  lease, allowing another enrolled machine to take over;
- a user operation on the machine where it occurred may submit an interactive
  observation without the bulk term; it still uses the same fingerprint/reducer
  pipeline;
- the client consumes SignalR as a doorbell, then applies `/changes?after=`; a
  `RESYNC_REQUIRED` response triggers the one-transaction snapshot.

The service may continue while the desktop is closed, but it must stop fetching
when its local JMS credential is absent/invalid. Desktop integration, Named Pipe
ACLs, DPAPI storage, and the real production license/JWKS adapter are deliberately
separate work after this backend canary.

## Production promotion

Build and scan one immutable API image, then deploy the same digest with
`.env.production`. Do not copy the staging env file or database volume. Point the
stable production DNS name at the active VPS; a replacement VPS reuses the DNS
name after restoring the encrypted external backup. License payloads do not carry
an IP address. `scripts/start-stack.ps1` rejects mutable tags, verifies the pulled
digest, and starts Compose with `--no-build`.

Only Caddy publishes ports 80/443. PostgreSQL is on the internal Docker network
and has no host `ports` mapping. Do not expose 5432 to the Internet.

## Backup and restore

Run `scripts/backup-postgres.ps1` outside peak JMS ingest hours. It emits a
compressed dump to an operator-selected directory; encrypt and upload that file to
an external bucket. Credentials and passphrases are supplied through environment
variables and never written to this repository. Before declaring a VPS replacement
ready, restore the dump into an isolated database, apply any missing migrations,
run the catalog assertions, and execute the synthetic ingest/snapshot checks.

For this compose layout, use the scripts' compose mode; they execute the PostgreSQL
tools inside the private service and transfer only the dump file:

```powershell
pwsh ./scripts/backup-postgres.ps1 -OutputDirectory /srv/datahub-backups -ComposeFile ./docker-compose.yml -ComposeEnvFile ./.env.production
pwsh ./scripts/restore-postgres.ps1 -DumpFile /srv/datahub-backups/datahub-<timestamp>.dump -ComposeFile ./docker-compose.yml -ComposeEnvFile ./.env.production
```

Restore uses one transaction and refuses to clean an existing database unless
`-AllowExistingData` is explicitly supplied. Prefer an empty isolated database
for drills; never point a first restore at the live production database.

Direct `DatabaseUrl` mode remains available through a managed endpoint or SSH tunnel;
a normal host shell cannot resolve `postgres` because port 5432 is intentionally
unpublished.

The seeded `archive_after` value is policy metadata for a future archive adapter;
phase 1 does not pretend that an `is_archived` flag saves disk or that an external
archive already exists. Until that adapter is measured and enabled, the event dump
remains the recovery source and `delete_after` must not be shortened.

The target `<30 minutes` recovery time is a drill target, not an SLA. Until the
JMS replay window is measured, retain an encrypted observation dump as well as the
non-disposable user/config/audit/device/site data.
