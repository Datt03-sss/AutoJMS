-- Tighten public anon/authenticated privileges for AutoJMS tables.
-- Client writes must go through SECURITY DEFINER RPCs, not direct table writes.

revoke all privileges on table public.waybills from anon, authenticated;
revoke all privileges on table public.inventory_sync_leases from anon, authenticated;
revoke all privileges on table public.app_manifest from anon, authenticated;
revoke all privileges on table public.app_modules from anon, authenticated;
revoke all privileges on table public.app_configs from anon, authenticated;

grant select on table public.waybills to anon, authenticated;
grant select on table public.app_manifest to anon, authenticated;
grant select on table public.app_modules to anon, authenticated;
grant select on table public.app_configs to anon, authenticated;

grant execute on function public.try_acquire_inventory_lease(text, integer) to anon, authenticated;
grant execute on function public.refresh_inventory_lease(text, integer) to anon, authenticated;
grant execute on function public.release_inventory_lease(text) to anon, authenticated;
grant execute on function public.complete_inventory_sync(text) to anon, authenticated;
grant execute on function public.upsert_new_waybills(text[]) to anon, authenticated;
grant execute on function public.merge_waybill_tracking_rows(jsonb) to anon, authenticated;
