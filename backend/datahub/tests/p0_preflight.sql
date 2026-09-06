-- p0_preflight.sql — read-only schema inventory for the P0 gate
-- (docs/review/p0-execution-runbook.vi.md, step 1; §19.1 of streaming-v4.6-contract).
--
--   ./run-sql.sh --env-file /path/to/.env.staging ../tests/p0_preflight.sql
--
-- Answers one question: is the database in the state P1's migrations expect to find?
-- It applies nothing and repairs nothing. A mismatch is a STOP, not something to fix
-- in passing — §29.9 and rule 7 of the runbook both forbid reconciling by guesswork.
--
-- Unlike 001_core_catalog_assertions.sql, which raises and aborts, this file reports.
-- The gate criteria are evaluated mechanically at the end (section 1.9) so the
-- PASS/STOP call does not depend on reading eight result sets correctly by eye; the
-- individual sections above it exist to show the operator *why* a check landed where
-- it did, and to be pasted into the P0 report.
SET default_transaction_read_only = on;
SET statement_timeout = '5min';

-- 1.1 Bảng
--     Seven of these must already exist. waybill_tombstones must NOT: it is created
--     by P1's migration 008, and finding it here means someone has already started
--     P1 against this database.
SELECT 'table' AS kind, t.name AS object,
       CASE WHEN c.table_name IS NULL THEN 'missing' ELSE 'exists' END AS state
  FROM (VALUES ('waybill_scan_events'),('waybill_projections'),
               ('site_change_counters'),('dashboard_changes'),
               ('idempotency_records'),('jms_event_policies'),
               ('retention_policies'),('waybill_tombstones')) AS t(name)
  LEFT JOIN information_schema.tables c
         ON c.table_name = t.name AND c.table_schema = 'public';

-- 1.2 Cột dự kiến THÊM ở P1 — phải là 'missing'; nếu 'exists' thì so type/nullability/default
--     Note for whoever reads the result: waybill_projections already has its own
--     reducer_version integer (001_core.sql:109). The row below is the different,
--     not-yet-existing waybill_scan_events.reducer_version, which P1 specifies as
--     smallint. Two columns, same name, different tables, different types — do not
--     read one as evidence about the other.
SELECT 'column' AS kind,
       e.tbl || '.' || e.col AS object,
       CASE WHEN c.column_name IS NULL THEN 'missing' ELSE 'exists' END AS state,
       c.data_type, c.is_nullable, c.column_default
  FROM (VALUES
        ('waybill_scan_events','event_kind'),
        ('waybill_scan_events','reducer_version'),
        ('waybill_scan_events','normalizer_version'),
        ('waybill_scan_events','source_schema_version'),
        ('waybill_projections','is_terminal'),
        ('waybill_projections','terminal_at'),
        ('waybill_projections','terminal_state_code'),
        ('waybill_projections','last_change_seq')
       ) AS e(tbl,col)
  LEFT JOIN information_schema.columns c
         ON c.table_name = e.tbl AND c.column_name = e.col AND c.table_schema='public';

-- 1.3 Cột CẤM ĐỤNG — phải tồn tại đúng như kỳ vọng
SELECT 'guard' AS kind, table_name||'.'||column_name AS object,
       data_type, is_nullable, column_default
  FROM information_schema.columns
 WHERE table_schema='public'
   AND (table_name,column_name) IN (
        ('waybill_scan_events','event_occurred_at'),
        ('waybill_scan_events','ingested_at'),
        ('waybill_scan_events','fingerprint_version'),
        ('site_change_counters','change_seq'),
        ('site_change_counters','pruned_through_seq'));

-- 1.4 Index
SELECT 'index' AS kind, indexname AS object, tablename
  FROM pg_indexes
 WHERE schemaname='public'
   AND tablename IN ('waybill_scan_events','waybill_projections','dashboard_changes')
 ORDER BY tablename, indexname;

-- 1.5 Constraint (PK của jms_event_policies phải composite)
--     Joined on the full constraint identity (catalog, schema, name) AND the table
--     name. Constraint names are only unique per table in PostgreSQL, so matching on
--     name and schema alone lets two tables that happen to share a constraint name
--     cross-join and report column lists that belong to neither.
SELECT 'constraint' AS kind, tc.table_name||'.'||tc.constraint_name AS object,
       tc.constraint_type, string_agg(kcu.column_name, ',' ORDER BY kcu.ordinal_position) AS cols
  FROM information_schema.table_constraints tc
  JOIN information_schema.key_column_usage kcu
    ON kcu.constraint_catalog = tc.constraint_catalog
   AND kcu.constraint_schema  = tc.constraint_schema
   AND kcu.constraint_name    = tc.constraint_name
   AND kcu.table_schema       = tc.table_schema
   AND kcu.table_name         = tc.table_name
 WHERE tc.table_schema='public'
   AND tc.table_name IN ('jms_event_policies','waybill_projections','dashboard_changes','idempotency_records')
 GROUP BY 1,2,3 ORDER BY 2;

-- 1.6 INVALID index (bẫy CREATE INDEX CONCURRENTLY) — phải RỖNG
--     apply-migrations.ps1:117-122 is explicit that the tooling does not detect this
--     and that recovery is a manual DROP INDEX CONCURRENTLY. §28 item 12: an index
--     existing does not mean it is usable.
SELECT indexrelid::regclass AS invalid_index FROM pg_index WHERE NOT indisvalid;

-- 1.7 Migration đã áp
SELECT version, applied_at FROM schema_migrations ORDER BY version;

-- 1.8 ⚠️ AN TOÀN: policy purge projection phải CHƯA tồn tại
--     DeleteProjectionsAsync purges on updated_at — inactivity, not terminality
--     (§23, §28 item 15). It is dormant only because this row is absent. One INSERT
--     here deletes live projections with no tombstone behind them.
SELECT count(*) AS projection_purge_policies
  FROM retention_policies WHERE table_name = 'waybill_projections';

-- 1.9 Gate verdict — mechanical evaluation of the runbook's PASS table (G2).
--     Any row that is not PASS is a STOP for the whole release.
WITH checks AS (
    SELECT 'G2.1 required tables present' AS check_name,
           '7 of 7' AS expected,
           count(*)::text || ' of 7' AS actual
      FROM information_schema.tables
     WHERE table_schema='public'
       AND table_name IN ('waybill_scan_events','waybill_projections','site_change_counters',
                          'dashboard_changes','idempotency_records','jms_event_policies',
                          'retention_policies')

    UNION ALL
    SELECT 'G2.2 waybill_tombstones absent (P1 not started)',
           '0',
           count(*)::text
      FROM information_schema.tables
     WHERE table_schema='public' AND table_name='waybill_tombstones'

    UNION ALL
    SELECT 'G2.3 all 8 P1 columns still missing',
           '0 present',
           count(*)::text || ' present'
      FROM information_schema.columns
     WHERE table_schema='public'
       AND (table_name,column_name) IN (
            ('waybill_scan_events','event_kind'),
            ('waybill_scan_events','reducer_version'),
            ('waybill_scan_events','normalizer_version'),
            ('waybill_scan_events','source_schema_version'),
            ('waybill_projections','is_terminal'),
            ('waybill_projections','terminal_at'),
            ('waybill_projections','terminal_state_code'),
            ('waybill_projections','last_change_seq'))

    UNION ALL
    SELECT 'G2.4 ingested_at defaults to now()',
           'now()',
           coalesce(max(column_default),'<absent>')
      FROM information_schema.columns
     WHERE table_schema='public' AND table_name='waybill_scan_events'
       AND column_name='ingested_at'

    UNION ALL
    SELECT 'G2.5 fingerprint_version NOT NULL DEFAULT 1',
           'NO / 1',
           coalesce(max(is_nullable),'<absent>') || ' / ' || coalesce(max(column_default),'<absent>')
      FROM information_schema.columns
     WHERE table_schema='public' AND table_name='waybill_scan_events'
       AND column_name='fingerprint_version'

    UNION ALL
    SELECT 'G2.6 event_occurred_at present and NOT NULL',
           'NO',
           coalesce(max(is_nullable),'<absent>')
      FROM information_schema.columns
     WHERE table_schema='public' AND table_name='waybill_scan_events'
       AND column_name='event_occurred_at'

    UNION ALL
    SELECT 'G2.7 jms_event_policies PK is composite',
           'reducer_version,scan_type_code',
           coalesce(string_agg(a.attname, ',' ORDER BY x.n), '<absent>')
      FROM pg_index i
      JOIN LATERAL unnest(i.indkey) WITH ORDINALITY AS x(attnum, n) ON true
      JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = x.attnum
     WHERE i.indrelid = to_regclass('public.jms_event_policies') AND i.indisprimary

    UNION ALL
    SELECT 'G2.8 no INVALID index',
           '0',
           count(*)::text
      FROM pg_index WHERE NOT indisvalid

    UNION ALL
    -- The one that silently destroys data if it is wrong. See 1.8.
    SELECT 'G2.9 no projection purge policy seeded',
           '0',
           count(*)::text
      FROM retention_policies WHERE table_name='waybill_projections'
)
SELECT check_name,
       expected,
       actual,
       CASE WHEN actual = expected THEN 'PASS' ELSE '*** STOP ***' END AS verdict
  FROM checks
 ORDER BY check_name;
