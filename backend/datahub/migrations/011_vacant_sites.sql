-- Empty-site cleanup, part 1 of 2: the definition of "empty".
--
-- Enrollment provisions its own site now that the licence assertion is signed
-- (EnrollmentRepository.ProvisionSiteAsync), so a licence key carrying a mistyped
-- middle code no longer fails with 404 -- it silently creates a site nobody will
-- ever use, together with the device that asked for it. This view is the single
-- answer to "which sites hold nothing", read both by the retention worker and by
-- scripts/cleanup-empty-sites.sh, so the automatic sweep and the operator's dry
-- run can never disagree about what is empty.
--
-- It is deliberately a DATA test only and says nothing about age: a site enrolled
-- ten minutes ago is listed here and is not junk. Every caller adds its own age
-- condition on created_at and last_device_seen_at before deleting anything, which
-- is why both columns are projected.
--
-- change_seq = 0 is the load-bearing clause and the only one retention cannot
-- erase. Ingest and tombstone publication each bump the counter and it never goes
-- back down, so a site that was once busy stays out of this view forever -- even
-- after its events, changes and projections have aged away under the policies in
-- 003_seed_retention. The NOT EXISTS clauses cannot do that job on their own:
-- every table they name is one retention empties on a clock.
--
-- The join to site_change_counters is an inner join on purpose. A site with no
-- counter row was not built by create_datahub_site, so this view does not know its
-- shape and declines to call it empty.
--
-- A site carrying its own retention_policies row is excluded for two reasons that
-- happen to agree: someone configured it deliberately, and retention_policies.site_id
-- references sites(id) with NO ACTION, so deleting such a site would fail anyway.
CREATE OR REPLACE VIEW vacant_sites AS
SELECT s.id                                                              AS site_id,
       s.site_code,
       s.created_at,
       (SELECT count(*)        FROM devices d WHERE d.site_id = s.id)    AS device_count,
       (SELECT max(d.last_seen_at) FROM devices d WHERE d.site_id = s.id) AS last_device_seen_at,
       (SELECT count(*)        FROM audit_logs a WHERE a.site_id = s.id) AS audit_log_count
  FROM sites s
  JOIN site_change_counters c ON c.site_id = s.id
 WHERE c.change_seq = 0
   AND c.pruned_through_seq = 0
   AND NOT EXISTS (SELECT 1 FROM waybill_scan_events  e  WHERE e.site_id  = s.id)
   AND NOT EXISTS (SELECT 1 FROM waybill_projections  p  WHERE p.site_id  = s.id)
   AND NOT EXISTS (SELECT 1 FROM dashboard_changes    ch WHERE ch.site_id = s.id)
   AND NOT EXISTS (SELECT 1 FROM idempotency_records  i  WHERE i.site_id  = s.id)
   AND NOT EXISTS (SELECT 1 FROM retention_policies   r  WHERE r.site_id  = s.id);

-- No index is created here. Every lookup above is already served by an existing
-- key: the primary keys of waybill_projections (site_id, waybill_no),
-- dashboard_changes (site_id, change_seq) and idempotency_records (site_id, key),
-- ix_waybill_scan_events_site_waybill_time, ix_devices_site_status,
-- ix_audit_logs_site_at, and ux_retention_policies_site_table.
--
-- No retention policy is seeded either, which is what makes the automatic sweep
-- off by default -- the same shape 003_seed_retention uses for waybill_projections,
-- and for the same reason: deleting a site takes its devices with it, so a station
-- that comes back from a long holiday would find its enrollment gone. Turning it
-- on is one row, and it only ever removes sites this view already lists:
--
--   INSERT INTO retention_policies (site_id, table_name, clock_column, delete_after)
--   VALUES (NULL, 'sites', 'created_at', interval '30 days');
--
-- Only the global row (site_id IS NULL) is honoured; see the note above about
-- per-site rows. Run scripts/cleanup-empty-sites.sh first to see what that
-- interval would take.

INSERT INTO schema_migrations (version)
VALUES ('011_vacant_sites')
ON CONFLICT (version) DO NOTHING;
