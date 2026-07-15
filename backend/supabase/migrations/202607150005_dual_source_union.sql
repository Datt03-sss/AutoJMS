-- Dual-source inventory union: combine Big Data ("Theo TTTC quét gửi kiện", 30d)
-- and Stock-check ("Thống kê kiểm kho", tồn 1 ngày) into public.waybills.
-- Goal: capture every waybill ever recorded in inventory at the post office,
-- incl. stray-scan cases ("Xuống hàng kiện đến" jumped/mis-scan) that Big Data may miss.
-- Non-destructive (upsert-only). Applied to project jrqxnviixmagiriqysov. Idempotent.
-- See docs/architecture/inventory-source-comparison.vi.md.

-- 1. Per-source provenance + stray flag
alter table public.waybills
    add column if not exists seen_in_bigdata boolean not null default false,
    add column if not exists seen_in_stockcheck boolean not null default false,
    add column if not exists bigdata_first_seen_at timestamptz,
    add column if not exists bigdata_last_seen_at timestamptz,
    add column if not exists stockcheck_first_seen_at timestamptz,
    add column if not exists stockcheck_last_seen_at timestamptz,
    add column if not exists suspected_stray boolean not null default false;

create index if not exists idx_waybills_suspected_stray
    on public.waybills(site_code)
    where suspected_stray and not is_handled;

-- 2. Ingest a Big Data snapshot (mark provenance). Detail fields are pushed
--    separately via merge_waybill_rows_v2; this only stamps the source + seen times.
create or replace function public.ingest_bigdata_waybills(
    p_site_code text,
    p_waybill_nos text[]
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

    with cleaned as (
        select distinct nullif(trim(x), '') as wb
        from unnest(coalesce(p_waybill_nos, array[]::text[])) as x
    ),
    up as (
        insert into public.waybills (waybill_no, site_code,
            seen_in_bigdata, bigdata_first_seen_at, bigdata_last_seen_at,
            is_in_current_inventory, next_track_at, updated_at)
        select wb, v_site, true, now(), now(), true, now(), now()
        from cleaned where wb is not null
        on conflict (waybill_no) do update set
            site_code = excluded.site_code,
            seen_in_bigdata = true,
            bigdata_first_seen_at = coalesce(public.waybills.bigdata_first_seen_at, now()),
            bigdata_last_seen_at = now(),
            is_in_current_inventory = true,
            updated_at = now()
        returning 1
    )
    select count(*) into v_count from up;
    return coalesce(v_count, 0);
end;
$$;

-- 3. Ingest a Stock-check snapshot (insert-if-missing + mark provenance).
create or replace function public.ingest_stockcheck_waybills(
    p_site_code text,
    p_waybill_nos text[]
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

    with cleaned as (
        select distinct nullif(trim(x), '') as wb
        from unnest(coalesce(p_waybill_nos, array[]::text[])) as x
    ),
    up as (
        insert into public.waybills (waybill_no, site_code,
            seen_in_stockcheck, stockcheck_first_seen_at, stockcheck_last_seen_at,
            is_in_current_inventory, next_track_at, updated_at)
        select wb, v_site, true, now(), now(), true, now(), now()
        from cleaned where wb is not null
        on conflict (waybill_no) do update set
            site_code = excluded.site_code,
            seen_in_stockcheck = true,
            stockcheck_first_seen_at = coalesce(public.waybills.stockcheck_first_seen_at, now()),
            stockcheck_last_seen_at = now(),
            is_in_current_inventory = true,
            updated_at = now()
        returning 1
    )
    select count(*) into v_count from up;
    return coalesce(v_count, 0);
end;
$$;

-- 4. Reconcile provenance -> set suspected_stray for a site.
--    Stray = active, seen by stock-check but NOT by big data (jumped/mis-scan).
--    Clears automatically once big data later confirms the waybill.
create or replace function public.reconcile_inventory_sources(p_site_code text)
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
    set suspected_stray = (w.seen_in_stockcheck and not w.seen_in_bigdata),
        updated_at = case
            when w.suspected_stray <> (w.seen_in_stockcheck and not w.seen_in_bigdata)
            then now() else w.updated_at end
    where w.site_code = v_site
      and w.is_active;

    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

-- 5. Fold provenance into the Big Data detail merge so a single call stamps it too.
create or replace function public.merge_bigdata_detail(
    p_site_code text,
    p_rows jsonb
)
returns integer
language plpgsql
security definer
set search_path = public
as $$
declare
    v_merged integer;
    v_nos text[];
begin
    v_merged := public.merge_waybill_rows_v2(p_site_code, p_rows);
    select array_agg(distinct nullif(trim(r.waybill_no),''))
      into v_nos
    from jsonb_to_recordset(coalesce(p_rows,'[]'::jsonb)) as r(waybill_no text);
    perform public.ingest_bigdata_waybills(p_site_code, coalesce(v_nos, array[]::text[]));
    return v_merged;
end;
$$;

-- 6. Keep stray (unhandled) waybills out of the 30-day retention purge.
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
    delete from public.waybills w
    where public.wb_is_signed_receipt(w.current_status, w.trang_thai_hien_tai);
    get diagnostics n_receipt = row_count;

    delete from public.waybills w
    where public.wb_is_ended(w.current_status, w.trang_thai_hien_tai)
      and w.is_handled;
    get diagnostics n_handled = row_count;

    -- 30-day residency EXCEPT: unhandled "Kết thúc" (arbitration) and
    -- unhandled suspected-stray (kept until newer journey / marked handled).
    delete from public.waybills w
    where w.updated_at < v_cutoff
      and not (public.wb_is_ended(w.current_status, w.trang_thai_hien_tai) and not w.is_handled)
      and not (w.suspected_stray and not w.is_handled);
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

-- 7. Grants (site-scoped helpers callable by client).
grant execute on function public.ingest_bigdata_waybills(text, text[]) to anon, authenticated;
grant execute on function public.ingest_stockcheck_waybills(text, text[]) to anon, authenticated;
grant execute on function public.reconcile_inventory_sources(text) to anon, authenticated;
grant execute on function public.merge_bigdata_detail(text, jsonb) to anon, authenticated;

analyze public.waybills;
