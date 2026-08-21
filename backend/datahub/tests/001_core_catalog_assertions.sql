DO $$
DECLARE
    required_table text;
    required_tables text[] := ARRAY[
        'schema_migrations',
        'sites', 'devices', 'site_fetch_leases', 'site_change_counters',
        'waybill_scan_events', 'waybill_projections', 'dashboard_changes',
        'jms_event_policies', 'idempotency_records', 'retention_policies', 'audit_logs'
    ];
    actual_type text;
    actual_nullable text;
    pk_columns text;
BEGIN
    FOREACH required_table IN ARRAY required_tables LOOP
        IF to_regclass('public.' || required_table) IS NULL THEN
            RAISE EXCEPTION 'required table is missing: %', required_table;
        END IF;
    END LOOP;

    SELECT data_type, is_nullable
      INTO actual_type, actual_nullable
      FROM information_schema.columns
     WHERE table_schema = 'public'
       AND table_name = 'site_fetch_leases'
       AND column_name = 'leader_device_id';
    IF actual_type IS DISTINCT FROM 'uuid' OR actual_nullable IS DISTINCT FROM 'YES' THEN
        RAISE EXCEPTION 'leader_device_id must be nullable uuid (%, %)', actual_type, actual_nullable;
    END IF;

    SELECT string_agg(a.attname, ',' ORDER BY x.n)
      INTO pk_columns
      FROM pg_index i
      JOIN pg_class c ON c.oid = i.indexrelid
      JOIN LATERAL unnest(i.indkey) WITH ORDINALITY AS x(attnum, n) ON true
      JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = x.attnum
     WHERE i.indrelid = 'public.dashboard_changes'::regclass
       AND i.indisprimary;
    IF pk_columns IS DISTINCT FROM 'site_id,change_seq' THEN
        RAISE EXCEPTION 'dashboard_changes primary key must be site_id,change_seq; got %', pk_columns;
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM schema_migrations
         WHERE version = '001_core'
    ) THEN
        RAISE EXCEPTION '001_core migration version marker is missing';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid = 'public.waybill_projections'::regclass
           AND contype = 'f'
    ) THEN
        RAISE EXCEPTION 'waybill_projections must not have event foreign keys';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM pg_class
         WHERE relname = 'ix_dashboard_changes_site_seq'
    ) THEN
        RAISE EXCEPTION 'duplicate dashboard change cursor index exists';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM information_schema.columns
         WHERE table_schema = 'public'
           AND column_name ILIKE 'uploadtime'
    ) THEN
        RAISE EXCEPTION 'uploadTime must not be a hot column';
    END IF;

    FOREACH required_table IN ARRAY ARRAY['state_status', 'state_payload', 'last_activity_status', 'last_activity_payload', 'inventory_status', 'inventory_payload'] LOOP
        IF NOT EXISTS (
            SELECT 1
              FROM information_schema.columns
             WHERE table_schema = 'public'
               AND table_name = 'waybill_projections'
               AND column_name = required_table
        ) THEN
            RAISE EXCEPTION 'projection slot column is missing: %', required_table;
        END IF;
    END LOOP;

    IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = '004_projection_slot_payloads') THEN
        RAISE EXCEPTION '004 projection slot migration marker is missing';
    END IF;

    SELECT data_type, is_nullable
      INTO actual_type, actual_nullable
      FROM information_schema.columns
     WHERE table_schema = 'public'
       AND table_name = 'site_change_counters'
       AND column_name = 'pruned_through_seq';
    IF actual_type IS DISTINCT FROM 'bigint' OR actual_nullable IS DISTINCT FROM 'NO' THEN
        RAISE EXCEPTION 'pruned_through_seq must be non-null bigint (%, %)', actual_type, actual_nullable;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE version = '005_change_retention_floor') THEN
        RAISE EXCEPTION '005 change retention floor migration marker is missing';
    END IF;
END
$$;
