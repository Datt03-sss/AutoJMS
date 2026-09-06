-- od1_scan_vocabulary.sql — read-only evidence for Owner Decision OD-1
-- (the terminal scan-code list) per docs/review/p0-execution-runbook.vi.md, step 0.
--
--   ./run-sql.sh --env-file /path/to/.env.staging ../tests/od1_scan_vocabulary.sql
--
-- OD-1 is the decision that eventually lets retention purge live projections, so it
-- has to be signed off evidence rather than the plausible-looking pair already in
-- the client. This file only reads; it decides nothing. §29.2 of the v4.6 contract
-- forbids picking the list here, and the runbook forbids seeding jms_event_policies
-- during P0.
--
-- waybill_scan_events stores BOTH scan_type_code and scan_type_name
-- (001_core.sql:61-62), and IngestRepository.InsertEventAsync writes each one
-- verbatim from the client's JmsObservation — the server normalises neither. So the
-- vocabulary below is whatever the client sent, in whichever language it sent it.
--
-- The whole file is read-only by construction: default_transaction_read_only makes
-- every statement below unable to write even if this file is edited later. The
-- runbook mandates re-running this against PRODUCTION before P6, and that guard is
-- what makes doing so safe.
SET default_transaction_read_only = on;

-- 0.1 and 0.2 scan the whole table and sort it, on a table with no index that can
-- serve them (ix_waybill_scan_events_site_waybill_time is ASC on the third key while
-- 0.2 needs it DESC). Cheap on staging, potentially minutes on production. The cap
-- turns "did this wedge production?" into a clean error. Raise it deliberately if a
-- production run needs longer.
SET statement_timeout = '15min';

-- 0.0 Does ILIKE actually fold Vietnamese in THIS database?
--     The stack runs postgres:16-alpine (docker-compose.yml:90) with no
--     POSTGRES_INITDB_ARGS, and musl has no locale support, so the database is
--     likely created with LC_CTYPE=C. Under C, lower() folds ASCII A-Z and nothing
--     else, which means ILIKE '%Ký nhận%' would NOT match 'KÝ NHẬN CPN'. Query 0.3
--     is written not to depend on the answer, but the answer decides whether an
--     OD-1 policy keyed on scan_type_name can rely on case-insensitive matching at
--     all. If ctype is 'C' or 'POSIX', any name-keyed policy must match exactly or
--     normalise before comparing.
SELECT current_database()                        AS database,
       datcollate                                AS lc_collate,
       datctype                                  AS lc_ctype,
       pg_encoding_to_char(encoding)             AS encoding,
       upper('ký nhận') = 'KÝ NHẬN'              AS folds_vietnamese
  FROM pg_database
 WHERE datname = current_database();

-- 0.1 Toàn bộ từ vựng scan type thật, kèm tần suất
SELECT scan_type_code,
       scan_type_name,
       count(*)                        AS events,
       count(DISTINCT waybill_no)      AS waybills,
       min(event_occurred_at)          AS first_seen,
       max(event_occurred_at)          AS last_seen
  FROM waybill_scan_events
 GROUP BY scan_type_code, scan_type_name
 ORDER BY events DESC;

-- 0.2 BẰNG CHỨNG TERMINAL: scan type nào là event CUỐI CÙNG của một waybill
--     và waybill đó đã "yên" >= 14 ngày (loại đơn còn đang chạy).
--
--     Two percentages, because they answer different questions and the naive one
--     understates a genuinely terminal code:
--
--       pct_final          — times_final over EVERY occurrence of the code, ever.
--                            The denominator includes waybills that are still
--                            running, so a perfectly terminal code that has been
--                            busy in the last 14 days scores far below 100%.
--       pct_final_settled  — times_final over the settled waybills that carried the
--                            code at all. This is the question OD-1 actually asks:
--                            "when a finished waybill contains this code, is it the
--                            last thing that happened to it?" Read this column.
--
--     Both are reported so the numbers can be compared with the runbook as written.
WITH last_event AS (
    SELECT DISTINCT ON (site_id, waybill_no)
           site_id, waybill_no, scan_type_code, scan_type_name, event_occurred_at
      FROM waybill_scan_events
     ORDER BY site_id, waybill_no, event_occurred_at DESC, id DESC
),
settled AS (
    SELECT * FROM last_event
     WHERE event_occurred_at < now() - interval '14 days'
),
totals AS (
    SELECT scan_type_code, scan_type_name, count(*) AS total_occurrences
      FROM waybill_scan_events
     GROUP BY scan_type_code, scan_type_name
),
-- Settled waybills that carried the code anywhere in their history, terminal or not.
-- Deduplicated in a subquery rather than with count(DISTINCT (site_id, waybill_no)):
-- a parenthesised pair inside an aggregate is a row constructor, which is legal but
-- reads like a two-argument call. This says the same thing unambiguously.
settled_carrying AS (
    SELECT scan_type_code, scan_type_name, count(*) AS settled_waybills
      FROM (SELECT DISTINCT e.scan_type_code, e.scan_type_name, e.site_id, e.waybill_no
              FROM waybill_scan_events e
              JOIN settled s
                ON s.site_id = e.site_id
               AND s.waybill_no = e.waybill_no) d
     GROUP BY scan_type_code, scan_type_name
)
SELECT s.scan_type_code,
       s.scan_type_name,
       count(*)                                              AS times_final,
       t.total_occurrences,
       round(100.0 * count(*) / NULLIF(t.total_occurrences,0), 1) AS pct_final,
       c.settled_waybills,
       round(100.0 * count(*) / NULLIF(c.settled_waybills,0), 1)  AS pct_final_settled
  FROM settled s
  JOIN totals t
    ON t.scan_type_code IS NOT DISTINCT FROM s.scan_type_code
   AND t.scan_type_name IS NOT DISTINCT FROM s.scan_type_name
  LEFT JOIN settled_carrying c
    ON c.scan_type_code IS NOT DISTINCT FROM s.scan_type_code
   AND c.scan_type_name IS NOT DISTINCT FROM s.scan_type_name
 GROUP BY s.scan_type_code, s.scan_type_name, t.total_occurrences, c.settled_waybills
 ORDER BY times_final DESC;

-- 0.3 Đối chiếu với từ vựng client đang dùng (DkchJourneyAnalyzer.Classify)
--     Classify (DkchJourneyAnalyzer.cs:758-759) recognises two signing events:
--       退件签收 / "Ký nhận chuyển hoàn"  -> Kind.SignedReturn
--       快件签收 / "Ký nhận CPN"          -> Kind.SignedCpn
--     Note the mismatch worth carrying into the OD-1 decision:
--     JourneyTextNormalizer.ActionTypeMap maps 快件签收 to the shorter "Ký nhận",
--     not "Ký nhận CPN", so the same underlying event can reach the database under
--     either spelling. The '%Ký nhận%' prefix below is deliberately loose enough to
--     catch both.
--
--     Each Vietnamese pattern appears twice, ILIKE and LIKE. Where LC_CTYPE folds
--     non-ASCII (see 0.0) the ILIKE is the useful one; where it does not, the plain
--     LIKE still matches the exact casing the client emits. The pair can only widen
--     the result, never narrow it. The CJK patterns need no folding.
SELECT scan_type_code, scan_type_name, count(*) AS events
  FROM waybill_scan_events
 WHERE scan_type_name LIKE '%签收%'
    OR scan_type_name LIKE '%退件%'
    OR scan_type_name ILIKE '%Ký nhận%'
    OR scan_type_name LIKE  '%Ký nhận%'
    OR scan_type_name ILIKE '%chuyển hoàn%'
    OR scan_type_name LIKE  '%chuyển hoàn%'
 GROUP BY 1,2
 ORDER BY events DESC;

-- 0.4 scan_type_code có bao giờ NULL không? (JmsObservation.Code là int? nullable)
--     scan_type_name is counted alongside it because the two answers together, not
--     either alone, decide how OD-1 may be keyed. If code has NULLs the policy
--     cannot key on code; if name has NULLs or blanks it cannot key on name; if both
--     do, the policy needs a composite key and an explicit rule for the gaps.
SELECT count(*) FILTER (WHERE scan_type_code IS NULL)                    AS null_code,
       count(*) FILTER (WHERE scan_type_name IS NULL)                    AS null_name,
       count(*) FILTER (WHERE btrim(coalesce(scan_type_name,'')) = '')   AS blank_name,
       count(*)                                                          AS total
  FROM waybill_scan_events;

-- 0.5 Is the code <-> name relationship one-to-one?
--     OD-1 asks the Owner to key the policy on code, name, or both. That choice is
--     only meaningful once it is known whether one code arrives under several names
--     (renaming/translation drift, so a name-keyed policy would miss variants) or
--     one name arrives under several codes (so a code-keyed policy would miss some).
--     Rows here are the ambiguous ones; an empty result means either key works.
--     coalesce() inside every count(DISTINCT ...): count ignores NULLs, so a code
--     seen once as NULL and once as 'X' would otherwise be reported as unambiguous,
--     which is exactly the case a fail-closed policy must not miss.
SELECT 'code -> many names' AS ambiguity,
       coalesce(scan_type_code::text,'<null>') AS key_value,
       count(DISTINCT coalesce(scan_type_name,'<null>')) AS variants,
       string_agg(DISTINCT coalesce(scan_type_name,'<null>'), ' | ' ORDER BY coalesce(scan_type_name,'<null>')) AS seen_as
  FROM waybill_scan_events
 GROUP BY scan_type_code
HAVING count(DISTINCT coalesce(scan_type_name,'<null>')) > 1
UNION ALL
SELECT 'name -> many codes',
       coalesce(scan_type_name,'<null>'),
       count(DISTINCT coalesce(scan_type_code::text,'<null>')),
       string_agg(DISTINCT coalesce(scan_type_code::text,'<null>'), ' | ' ORDER BY coalesce(scan_type_code::text,'<null>'))
  FROM waybill_scan_events
 GROUP BY scan_type_name
HAVING count(DISTINCT coalesce(scan_type_code::text,'<null>')) > 1
 ORDER BY 1, 3 DESC;
