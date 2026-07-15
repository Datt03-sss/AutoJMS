-- FullStackOperation inventory-sync helpers for the JMS "Giám sát tồn kho (Big Data)"
-- source. Non-destructive: never deletes rows. Only updates existing + inserts new.
-- Stop condition: waybill whose final status is 'Ký nhận CPN' or 'Kết thúc' is
-- deactivated (is_active=false) and removed from the tracking schedule.
-- Applied to project jrqxnviixmagiriqysov (autojms_database). Idempotent.

-- 1. Configurable set of terminal statuses that stop tracking.
insert into public.app_configs (key, value)
values ('tracking_final_statuses', '["Ký nhận CPN","Kết thúc"]'::jsonb)
on conflict (key) do update set value = excluded.value, updated_at = now();

-- 2. Helper: is a status string terminal?
create or replace function public.is_final_status(p_status text)
returns boolean
language sql
stable
set search_path = public
as $$
    select coalesce(
        exists (
            select 1
            from jsonb_array_elements_text(
                coalesce((select value from public.app_configs where key = 'tracking_final_statuses'),
                         '["Ký nhận CPN","Kết thúc"]'::jsonb)
            ) as s(v)
            where lower(trim(p_status)) = lower(trim(s.v))
        ),
        false
    );
$$;

-- 3. finalize_waybills: mark listed waybills as terminal -> stop tracking.
--    Non-destructive: keeps the row, only flips flags + records final status.
create or replace function public.finalize_waybills(
    p_site_code text,
    p_waybill_nos text[],
    p_final_status text default 'Kết thúc'
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
    set is_active = false,
        is_in_current_inventory = false,
        current_status = coalesce(nullif(trim(p_final_status), ''), current_status),
        trang_thai_hien_tai = coalesce(nullif(trim(p_final_status), ''), trang_thai_hien_tai),
        updated_at = now()
    from (
        select distinct nullif(trim(x), '') as wb
        from unnest(coalesce(p_waybill_nos, array[]::text[])) as x
    ) src
    where w.site_code = v_site
      and w.waybill_no = src.wb
      and src.wb is not null
      and w.is_active;   -- only touch still-active rows

    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

-- 4. mark_left_inventory: rows still active in DB but NOT in the latest inventory
--    snapshot have left the branch inventory. Record the exit without deleting and
--    without stopping tracking (final status is confirmed separately via route API).
create or replace function public.mark_left_inventory(
    p_site_code text,
    p_seen_waybill_nos text[],
    p_left_at text default ''
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
    set is_in_current_inventory = false,
        left_inventory_at = coalesce(nullif(trim(p_left_at), ''), left_inventory_at),
        updated_at = now()
    where w.site_code = v_site
      and w.is_in_current_inventory
      and not exists (
          select 1
          from unnest(coalesce(p_seen_waybill_nos, array[]::text[])) as s(wb)
          where nullif(trim(s.wb), '') = w.waybill_no
      );

    get diagnostics v_count = row_count;
    return v_count;
end;
$$;

-- 5. auto-stop trigger: whenever a row's status is set to a terminal value by any
--    merge RPC, deactivate tracking automatically (defense in depth).
create or replace function public.trg_stop_tracking_on_final()
returns trigger
language plpgsql
set search_path = public
as $$
begin
    if public.is_final_status(new.current_status) or public.is_final_status(new.trang_thai_hien_tai) then
        new.is_active := false;
        new.is_in_current_inventory := false;
    end if;
    return new;
end;
$$;

drop trigger if exists waybills_stop_on_final on public.waybills;
create trigger waybills_stop_on_final
    before insert or update on public.waybills
    for each row execute function public.trg_stop_tracking_on_final();

-- 6. Fast scheduler lookup: still-active waybills that are due for a tracking pull.
create index if not exists idx_waybills_site_due_active
    on public.waybills(site_code, next_track_at)
    where is_active;

-- 7. Grants
grant execute on function public.is_final_status(text) to anon, authenticated;
grant execute on function public.finalize_waybills(text, text[], text) to anon, authenticated;
grant execute on function public.mark_left_inventory(text, text[], text) to anon, authenticated;

analyze public.waybills;
