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
