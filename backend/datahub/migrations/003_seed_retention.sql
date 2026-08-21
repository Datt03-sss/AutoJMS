-- Retention values are defaults, not immutable architecture. Operators may add a
-- site-specific row later; the hosted worker only executes allow-listed clocks.
INSERT INTO retention_policies (site_id, table_name, clock_column, hot_after, archive_after, delete_after)
VALUES
    (NULL, 'waybill_scan_events', 'event_occurred_at', interval '30 days', interval '30 days', interval '60 days'),
    (NULL, 'dashboard_changes', 'change_at', NULL, NULL, interval '14 days'),
    (NULL, 'audit_logs', 'at', NULL, NULL, interval '90 days')
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (version)
VALUES ('003_seed_retention')
ON CONFLICT (version) DO NOTHING;
