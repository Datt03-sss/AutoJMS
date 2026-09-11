-- 010_retention_scan_indexes_notx.sql
-- Makes the three retention scans index-only. Audit §5bis.5.
--
-- The audit asked for `ix_audit_logs_at` on audit_logs (at) and `ix_dashboard_changes_site_at`
-- on dashboard_changes (site_id, change_at). Both already exist in a strictly wider form —
-- 006 created ix_audit_logs_retention (at, id) and ix_dashboard_changes_site_change_at
-- (site_id, change_at, change_seq), each a leading-column superset of what was asked for. So
-- the report's proposal is redundant. Its *reasoning* is not: the retention queries really do
-- lack an equality predicate on site_id, and really do scan every cycle.
--
-- What the proposal missed is that adding another index leading with site_id could not have
-- fixed that — an unconstrained leading column is unseekable either way. The scans are
-- inherently full; what is avoidable is the heap. Each of these three tables carries a jsonb
-- column (dashboard_changes.body, audit_logs.payload, waybill_scan_events.payload), so every
-- heap fetch drags a wide tuple in for the sake of one narrow column the retention predicate
-- needs. Adding that column to the index as an INCLUDE payload turns the scan index-only.
--
-- All three are replacements on the same or a better key, so the index count per table is
-- unchanged and no write path gets slower by an index.
--
-- CONCURRENTLY on all six statements: these are the ingest and audit hot tables, and a plain
-- CREATE/DROP INDEX takes a lock that blocks every writer at every site for the duration.
-- That forbids a transaction, which is what the _notx suffix tells apply-migrations.sh: a
-- file named *_notx.sql is applied WITHOUT --single-transaction.
--
-- Create-then-drop, never drop-then-create: a build that fails halfway leaves the old index
-- standing, so the worst outcome of a bad run is the plan we have today, not an unindexed
-- table. The cost is a window where both indexes exist.
--
-- CONCURRENTLY's failure mode is the reason for contract 19.2: a cancelled or failed build
-- leaves an INVALID index behind, and IF NOT EXISTS then finds it and skips silently, so the
-- scan falls back to a sequential plan nobody is watching. After applying this file, check
-- pg_index.indisvalid for the three indexes below; if false, DROP the invalid one, retry once,
-- and stop and report if it fails again:
--
--   SELECT c.relname, i.indisvalid
--     FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
--    WHERE c.relname IN ('ix_dashboard_changes_retention',
--                        'ix_audit_logs_retention_covering',
--                        'ix_waybill_scan_events_retention');

-- 1. dashboard_changes — the largest scan in the system, and the only one with no LIMIT.
-- RetentionRepository.DeleteChangesAsync aggregates the whole table with GROUP BY c.site_id
-- to find each site's prunable prefix, so it must touch every row by construction. Its FILTER
-- reads c.operation to tell a tombstone (long clock) from an ordinary change (short clock),
-- and operation is the one column missing from the 006 index — forcing a heap fetch per row
-- purely to read a 6-to-7-character string. Same key, so every plan that used the old index
-- uses this one identically.
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_dashboard_changes_retention
    ON dashboard_changes (site_id, change_at, change_seq)
    INCLUDE (operation);

DROP INDEX CONCURRENTLY IF EXISTS ix_dashboard_changes_site_change_at;

-- 2. audit_logs — DeleteAuditLogsAsync orders by (at, id) and stops at batch_size, which the
-- 006 index already serves. The heap fetch that remains is for a.site_id alone, needed only to
-- join the per-site retention policy. INCLUDE it and the ordering scan never leaves the index.
-- ix_audit_logs_site_at (site_id, at) from 001 stays: it serves site-scoped audit reads, which
-- this index cannot.
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_audit_logs_retention_covering
    ON audit_logs (at, id)
    INCLUDE (site_id);

DROP INDEX CONCURRENTLY IF EXISTS ix_audit_logs_retention;

-- 3. waybill_scan_events — the one case where the key itself was wrong, not just uncovering.
-- 006 created ix_waybill_scan_events_site_occurred (site_id, event_occurred_at) for retention,
-- but DeleteEventsAsync orders by e.id, so the planner takes the primary key and that index has
-- never been read by anything: it is the only query in the codebase that reads this table
-- outside ingest. Leading with event_occurred_at instead, and switching that ORDER BY to match,
-- puts the scan in age order — which is the order a retention job wants anyway — so a backlog
-- is drained from the head of the index, and a drained table is proven empty by an index-only
-- scan of a narrow index rather than a walk of the full heap. id is in the key rather than the
-- INCLUDE payload because it is the ORDER BY tiebreak; site_id is INCLUDE because it is only
-- ever read, never ordered or seeked on.
--
-- Replacement, not addition. This table takes every ingest insert, and a fifth index would tax
-- a write path this same commit just spent its effort making faster.
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_waybill_scan_events_retention
    ON waybill_scan_events (event_occurred_at, id)
    INCLUDE (site_id);

DROP INDEX CONCURRENTLY IF EXISTS ix_waybill_scan_events_site_occurred;

INSERT INTO schema_migrations (version) VALUES ('010_retention_scan_indexes_notx')
ON CONFLICT (version) DO NOTHING;
