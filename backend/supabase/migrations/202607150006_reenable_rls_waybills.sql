-- Fix: RLS was found disabled on public.waybills (Supabase advisor 0007 + 0013:
-- policy_exists_rls_disabled / rls_disabled_in_public), meaning the site-scoped
-- SELECT policy waybills_site_read was NOT being enforced and anon could read all
-- sites' rows. Re-enable RLS. Writes still go through SECURITY DEFINER RPCs and
-- direct insert/update/delete remain revoked. Applied to jrqxnviixmagiriqysov. Idempotent.
alter table public.waybills enable row level security;
