-- Fix: jwt_site_code() crashed with "invalid input syntax for type json"
-- when request.jwt.claims is set to an empty string (e.g. direct SQL sessions).
-- Guard the cast with nullif(..., '') before ::jsonb.
-- Applied to project jrqxnviixmagiriqysov (autojms_database).

create or replace function public.jwt_site_code()
returns text
language sql
stable
set search_path = public
as $$
    select nullif(
        (nullif(current_setting('request.jwt.claims', true), ''))::jsonb ->> 'site_code',
        ''
    );
$$;

grant execute on function public.jwt_site_code() to anon, authenticated;
