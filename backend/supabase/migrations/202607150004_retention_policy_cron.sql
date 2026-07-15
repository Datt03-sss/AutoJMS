-- Retention policy for FullStackOperation waybills (30-day DB residency).
-- Rules (per owner spec 2026-07-15):
--   * "Ký nhận CPN"  -> purge immediately (any age).
--   * "Kết thúc"     -> keep indefinitely for arbitration ("Trọng tài"),
--                       until explicitly marked handled (is_handled=true).
--   * everything else -> 30-day residency, then purged.
-- Automated daily via pg_cron. Applied to project jrqxnviixmagiriqysov. Idempotent.

create extension if not exists pg_cron;

-- 1. Handling flag for terminal "Kết thúc" waybills kept for arbitration.
alter table public.waybills
    add column if not exists is_handled boolean not null default false,
    add column if not exists handled_at timestamptz;

create index if not exists idx_waybills_kept_unhandled
    on public.waybills(site_code)
    where is_handled = false;

-- 2. Status predicates (normalized, checks both status columns).
create or replace function public.wb_is_signed_receipt(p_cur text, p_legacy text)
returns boolean language sql immutable set search_path = public as $$
    select lower(trim(coalesce(p_cur,''))) = lower('Ký nhận CPN')
        or lower(trim(coalesce(p_legacy,''))) = lower('Ký nhận CPN');
$$;

create or replace function public.wb_is_ended(p_cur text, p_legacy text)
returns boolean language sql immutable set search_path = public as $$
    select lower(trim(coalesce(p_cur,''))) = lower('Kết thúc')
        or lower(trim(coalesce(p_legacy,''))) = lower('Kết thúc');
$$;

-- 3. Immediate purge of signed-receipt waybills for a site (app calls right
--    after each sync so "Ký nhận CPN" is removed without waiting for cron).
create or replace function public.purge_signed_receipts(p_site_code text)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
    v_site  text := coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    if public.jwt_site_code() is not null and public.jwt_site_code() <> v_site then
        return 0;
    end if;
    delete from public.waybills w
    where w.site_code = v_site
      and public.wb_is_signed_receipt(w.current_status, w.trang_thai_hien_tai);
    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

-- 4. Mark / unmark "Kết thúc" waybills as handled (arbitration resolved) so the
--    retention job can purge them. Non-destructive.
create or replace function public.mark_waybill_handled(
    p_site_code text,
    p_waybill_nos text[],
    p_handled boolean default true
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_count integer := 0;
    v_site  text := coalesce(nullif(trim(p_site_code), ''), 'default');
begin
    if public.jwt_site_code() is not null and public.jwt_site_code() <> v_site then
        return 0;
    end if;
    update public.waybills w
    set is_handled = coalesce(p_handled, true),
        handled_at = case when coalesce(p_handled,true) then now() else null end,
        updated_at = now()
    from (select distinct nullif(trim(x),'') as wb
          from unnest(coalesce(p_waybill_nos, array[]::text[])) as x) src
    where w.site_code = v_site
      and w.waybill_no = src.wb
      and src.wb is not null;
    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

-- 5. Global retention cleanup (run by cron). NOT granted to anon/authenticated.
create or replace function public.run_retention_cleanup(p_retention_days integer default 30)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_days      integer := greatest(coalesce(p_retention_days, 30), 1);
    v_cutoff    timestamptz := now() - make_interval(days => v_days);
    n_receipt   integer := 0;
    n_handled   integer := 0;
    n_expired   integer := 0;
begin
    -- (a) signed-receipt: purge immediately regardless of age
    delete from public.waybills w
    where public.wb_is_signed_receipt(w.current_status, w.trang_thai_hien_tai);
    get diagnostics n_receipt = row_count;

    -- (b) "Kết thúc" that arbitration has resolved (is_handled)
    delete from public.waybills w
    where public.wb_is_ended(w.current_status, w.trang_thai_hien_tai)
      and w.is_handled;
    get diagnostics n_handled = row_count;

    -- (c) 30-day residency for everything EXCEPT unhandled "Kết thúc"
    delete from public.waybills w
    where w.updated_at < v_cutoff
      and not (public.wb_is_ended(w.current_status, w.trang_thai_hien_tai) and not w.is_handled);
    get diagnostics n_expired = row_count;

    return jsonb_build_object(
        'ran_at', now(),
        'retention_days', v_days,
        'purged_signed_receipt', n_receipt,
        'purged_handled_ended', n_handled,
        'purged_expired', n_expired
    );
end;
$$;

-- 6. Grants: site-scoped helpers are client-callable; global cleanup is not.
grant execute on function public.purge_signed_receipts(text) to anon, authenticated;
grant execute on function public.mark_waybill_handled(text, text[], boolean) to anon, authenticated;
revoke all on function public.run_retention_cleanup(integer) from anon, authenticated;

-- 7. Schedule daily retention at 18:00 UTC (01:00 Asia/Ho_Chi_Minh).
do $$
begin
    perform cron.unschedule('autojms-retention-daily')
    where exists (select 1 from cron.job where jobname = 'autojms-retention-daily');
exception when others then null;
end $$;

select cron.schedule('autojms-retention-daily', '0 18 * * *',
    $cron$ select public.run_retention_cleanup(30); $cron$);
