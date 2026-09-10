#!/usr/bin/env python3
"""Harvest real JMS tracking journeys and load them into DataHub as observations.

Why this exists
---------------
`backend/datahub/tests/od1_scan_vocabulary.sql` answers OD-1 -- which scan codes
are terminal -- by reading `waybill_scan_events`. On 2026-09-10 that table on
staging held 7 804 rows of which 7 800 were `BENCH-%` waybills minted by
`baseline_load.py`, which hardcodes `code: 110`. A vocabulary query over
harness output can only ever return the harness's own two codes, so OD-1 could
not be answered from staging at any future date. This tool fetches genuine
journeys from JMS and loads them, so the query has real vocabulary to read.

It deliberately does NOT decide anything. It classifies nothing as terminal and
writes no `jms_event_policies` row -- P0 forbids both (see the runbook's "P0
KHONG duoc lam", item 4). The `--report` table marks terminal *candidates* by
substring only, as a reading aid for the Owner, and says so.

Standard library only: zipfile + xml.etree for .xlsx, urllib for HTTP.

Secrets
-------
No token is ever written to a file or printed. Both are masked first4...last4.
  JMS:     --jms-token      or env JMS_AUTH_TOKEN
  DataHub: --device-token   or env DATAHUB_DEVICE_TOKEN

Harvested journeys are real customer data. They are written under
`docs/manual/samples/`, which .gitignore excludes -- do not commit them.

Usage
-----
  # 1. read the workbook only, no network
  python tools/harvest_jms_events.py --excel docs/manual/waybilltest.xlsx --dry-run

  # 2. probe JMS with a handful of waybills and learn the real detail[] shape
  python tools/harvest_jms_events.py --excel docs/manual/waybilltest.xlsx \
      --jms-token "$JMS_AUTH_TOKEN" --limit 5 --inspect

  # 3. full harvest, then upload
  python tools/harvest_jms_events.py --excel docs/manual/waybilltest.xlsx \
      --jms-token "$JMS_AUTH_TOKEN" \
      --datahub-url https://dev.jmsauto.online \
      --site-id <guid> --device-token "$DATAHUB_DEVICE_TOKEN"

  # 4. re-map or re-upload from saved raw, without touching JMS again
  python tools/harvest_jms_events.py --from-raw docs/manual/samples/jms_raw_*.json --report
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from collections import Counter, OrderedDict
from datetime import datetime, timedelta, timezone

# --------------------------------------------------------------------------
# Constants mirrored from src/AutoJMS/FullStack/Services/FullStackTrackingJourneyService.cs
# --------------------------------------------------------------------------

# Two different hosts, and mixing them up earns a bare nginx 405. AppConfig.cs:24-25
# keeps them apart: `jmsBaseUrl` is the web UI, which only ever appears in the
# Origin/Referer headers; `jmsApiBaseUrl` is the gateway every API call is sent to.
JMS_ORIGIN = "https://jms.jtexpress.vn"
JMS_API_BASE = "https://jmsgw.jtexpress.vn"
JMS_PATH = "operatingplatform/podTracking/inner/query/keywordList"
JMS_ROUTE_NAME = "trackingExpress"
# URL-encoded CJK breadcrumb the JMS gateway expects; copied verbatim from
# FullStackTrackingJourneyService.cs:18-19.
JMS_ROUTER_NAME_LIST = (
    "%E6%93%8D%E4%BD%9C%E5%B9%B3%E5%8F%B0%3E%E5%BF%AB%E4%BB%B6%E6%9F%A5%E8%AF%A2"
    "%3E%E5%BF%AB%E4%BB%B6%E8%B7%9F%E8%B8%AA"
)
JMS_USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
)

# IngestEndpoints.cs:12 rejects a body over 1 MiB with 413. Every observation
# embeds its whole raw JMS event under `payload`, so batches must be capped by
# serialized size, not item count. 768 KiB leaves room for the JSON envelope
# and for one oversized item pushing a batch past the edge.
DATAHUB_MAX_BODY_BYTES = 1024 * 1024
DATAHUB_BATCH_BYTE_BUDGET = 768 * 1024

# The "device" rate limiter allows 240 requests/minute for one device token.
DATAHUB_MIN_REQUEST_INTERVAL_S = 0.30

VN_TZ = timezone(timedelta(hours=7))

XLSX_NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"

# JMS spells the numeric scan code differently across payload versions. Every
# candidate is tried in order and the winner is reported, so a silent miss shows
# up as `code=None` in the vocabulary table rather than being papered over.
CODE_KEYS = ("scanType", "scanTypeCode", "code", "operateType", "scanTypeId", "type")
NAME_KEYS = ("scanTypeName", "scanTypeStr", "operateTypeName", "statusName")
TIME_KEYS = ("scanTime", "eventTime", "operateTime", "createTime", "uploadTime")
STATUS_KEYS = ("status", "waybillStatus", "statusName", "scanStatus")
NETWORK_KEYS = ("scanNetworkCode", "networkCode", "siteCode", "scanSiteCode")
SCAN_BY_KEYS = ("scanByCode", "scanUserCode", "operatorCode", "createUser")

# Substrings the Owner asked to see flagged. These mark rows for HUMAN review;
# nothing downstream reads this list and OD-1 remains unsigned until the Owner
# signs it against od1_scan_vocabulary.sql.
TERMINAL_HINTS = ("签收", "退件", "Ký nhận", "ký nhận", "chuyển hoàn", "Chuyển hoàn")


# --------------------------------------------------------------------------
# Secret handling
# --------------------------------------------------------------------------

def mask(secret: str | None) -> str:
    """first4...last4, per the Secret Policy in CLAUDE.md."""
    if not secret:
        return "<empty>"
    if len(secret) <= 8:
        return "*" * len(secret)
    return f"{secret[:4]}...{secret[-4:]}"


# --------------------------------------------------------------------------
# 1. Excel
# --------------------------------------------------------------------------

def _column_index(cell_ref: str) -> int:
    """'AB12' -> 27 (0-based column). Sparse rows omit empty cells, so the
    letter prefix is the only reliable way to line a value up with its header."""
    letters = re.match(r"([A-Z]+)", cell_ref or "")
    if not letters:
        return 0
    index = 0
    for char in letters.group(1):
        index = index * 26 + (ord(char) - ord("A") + 1)
    return index - 1


def _cell_text(cell: ET.Element, shared: list[str]) -> str:
    cell_type = cell.get("t")
    if cell_type == "inlineStr":
        node = cell.find(XLSX_NS + "is")
        if node is None:
            return ""
        return "".join(t.text or "" for t in node.iter(XLSX_NS + "t")).strip()
    value = cell.find(XLSX_NS + "v")
    if value is None or value.text is None:
        return ""
    text = value.text.strip()
    if cell_type == "s":
        try:
            return shared[int(text)]
        except (ValueError, IndexError):
            return ""
    if cell_type == "str":
        return text
    # Numeric. A waybill typed as a number arrives as '8.02819444675E11'.
    if "E" in text.upper() or (text.replace("-", "").replace(".", "").isdigit() and "." in text):
        try:
            return format(int(float(text)), "d")
        except (ValueError, OverflowError):
            return text
    return text


def read_waybills(path: str, column_header: str) -> tuple[list[str], list[str]]:
    """Return (waybills, warnings). Duplicates are collapsed, order preserved."""
    warnings: list[str] = []
    with zipfile.ZipFile(path) as archive:
        shared: list[str] = []
        if "xl/sharedStrings.xml" in archive.namelist():
            root = ET.fromstring(archive.read("xl/sharedStrings.xml"))
            for si in root.findall(XLSX_NS + "si"):
                shared.append("".join(t.text or "" for t in si.iter(XLSX_NS + "t")).strip())

        sheet_names = [n for n in archive.namelist() if n.startswith("xl/worksheets/sheet")]
        if not sheet_names:
            raise SystemExit(f"{path}: no worksheet found inside the workbook")
        sheet = ET.fromstring(archive.read(sorted(sheet_names)[0]))

    data = sheet.find(XLSX_NS + "sheetData")
    if data is None:
        raise SystemExit(f"{path}: worksheet has no sheetData")
    rows = data.findall(XLSX_NS + "row")
    if not rows:
        raise SystemExit(f"{path}: worksheet is empty")

    header_cells = {
        _column_index(c.get("r", "")): _cell_text(c, shared)
        for c in rows[0].findall(XLSX_NS + "c")
    }
    target = None
    wanted = column_header.strip().casefold()
    for index, text in header_cells.items():
        if text.strip().casefold() == wanted:
            target = index
            break
    if target is None:
        available = ", ".join(f"{v!r}" for v in header_cells.values() if v) or "(none)"
        warnings.append(
            f"header {column_header!r} not found (columns: {available}); "
            f"falling back to the first column"
        )
        target = min(header_cells) if header_cells else 0

    seen: OrderedDict[str, None] = OrderedDict()
    duplicates = 0
    for row in rows[1:]:
        for cell in row.findall(XLSX_NS + "c"):
            if _column_index(cell.get("r", "")) != target:
                continue
            value = _cell_text(cell, shared).strip()
            if not value:
                continue
            if value in seen:
                duplicates += 1
            seen[value] = None
    if duplicates:
        warnings.append(f"{duplicates} duplicate waybill(s) collapsed")
    return list(seen.keys()), warnings


# --------------------------------------------------------------------------
# 2. JMS
# --------------------------------------------------------------------------

def _http_post_json(url: str, headers: dict[str, str], body: dict, timeout: int) -> tuple[int, str]:
    payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
    request = urllib.request.Request(url, data=payload, method="POST")
    for key, value in headers.items():
        request.add_header(key, value)
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            raw = response.read()
            if response.headers.get("Content-Encoding", "").lower() == "gzip":
                raw = gzip.decompress(raw)
            return response.status, raw.decode("utf-8", errors="replace")
    except urllib.error.HTTPError as error:
        raw = error.read()
        if error.headers.get("Content-Encoding", "").lower() == "gzip":
            raw = gzip.decompress(raw)
        return error.code, raw.decode("utf-8", errors="replace")
    except urllib.error.URLError as error:
        return 0, f"URLError: {error.reason}"


def jms_headers(token: str) -> dict[str, str]:
    """Mirrors FullStackTrackingJourneyService.cs:165-176 exactly."""
    return {
        "Content-Type": "application/json;charset=UTF-8",
        "authToken": token,
        "lang": "VN",
        "langType": "VN",
        "timezone": "GMT+0700",
        "routeName": JMS_ROUTE_NAME,
        "routerNameList": JMS_ROUTER_NAME_LIST,
        "Accept": "application/json, text/plain, */*",
        "Origin": JMS_ORIGIN,
        "Referer": JMS_ORIGIN + "/",
        "User-Agent": JMS_USER_AGENT,
    }


def fetch_batches(
    waybills: list[str],
    token: str,
    batch_size: int,
    delay: float,
    timeout: int,
    api_base: str = JMS_API_BASE,
) -> list[dict]:
    """POST the waybills in batches; return one record per batch, raw body kept."""
    url = f"{api_base.rstrip('/')}/{JMS_PATH}"
    headers = jms_headers(token)
    results: list[dict] = []
    total = (len(waybills) + batch_size - 1) // batch_size

    for number, start in enumerate(range(0, len(waybills), batch_size), start=1):
        batch = waybills[start:start + batch_size]
        body = {"keywordList": batch, "trackingTypeEnum": "WAYBILL", "countryId": "1"}
        status, text = _http_post_json(url, headers, body, timeout)

        parsed, parse_error = None, None
        try:
            parsed = json.loads(text)
        except json.JSONDecodeError as error:
            parse_error = f"{error}"

        data_count = 0
        if isinstance(parsed, dict) and isinstance(parsed.get("data"), list):
            data_count = len(parsed["data"])

        print(
            f"  batch {number}/{total}: {len(batch)} waybills -> HTTP {status}, "
            f"data[]={data_count}"
            + (f", parse error: {parse_error}" if parse_error else "")
        )
        if status != 200 or parse_error:
            preview = re.sub(r"\s+", " ", text)[:200]
            print(f"    body preview: {preview}")

        results.append({
            "batch": number,
            "requested": batch,
            "httpStatus": status,
            "fetchedAt": datetime.now(VN_TZ).isoformat(timespec="seconds"),
            "response": parsed if parse_error is None else None,
            "rawBody": text if parse_error is not None else None,
        })

        # Abort a doomed run instead of walking 239 waybills into the same wall.
        if number == 1 and status in (401, 403):
            print(f"    JMS rejected the token ({mask(token)}) with HTTP {status}; stopping.")
            break
        if number < total:
            time.sleep(delay)
    return results


# --------------------------------------------------------------------------
# 3. Mapping to JmsObservation
# --------------------------------------------------------------------------

def _pick(detail: dict, keys: tuple[str, ...]) -> tuple[str | None, str | None]:
    """First non-empty value among `keys`; returns (value, key_that_matched)."""
    for key in keys:
        if key not in detail:
            continue
        value = detail[key]
        if value is None:
            continue
        text = str(value).strip()
        if text and text != "--":
            return text, key
    return None, None


def _as_int(value: str | None) -> int | None:
    if value is None:
        return None
    try:
        return int(str(value).strip())
    except (TypeError, ValueError):
        return None


def normalize_scan_time(value: str | None) -> str | None:
    """To `yyyy-MM-dd HH:mm:ss`, which ScanTimeParser reads as Asia/Ho_Chi_Minh.

    ScanTimeParser.cs:46 also accepts ISO-8601 with an explicit offset, but JMS
    emits wall-clock Vietnam time with no zone, so the space-separated form is
    the honest one -- appending 'Z' would shift every event seven hours."""
    if not value:
        return None
    text = str(value).strip()
    if not text or text == "--":
        return None
    if re.fullmatch(r"\d{13}", text):  # epoch millis
        return datetime.fromtimestamp(int(text) / 1000, VN_TZ).strftime("%Y-%m-%d %H:%M:%S")
    text = text.replace("T", " ")
    if "." in text:
        text = text.split(".", 1)[0]
    text = text.rstrip("Z").strip()
    for fmt in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%d %H:%M", "%Y/%m/%d %H:%M:%S", "%d/%m/%Y %H:%M:%S"):
        try:
            return datetime.strptime(text, fmt).strftime("%Y-%m-%d %H:%M:%S")
        except ValueError:
            continue
    return None


def _same_waybill(left: str | None, right: str | None) -> bool:
    if not left or not right:
        return False
    return left.strip().upper() == right.strip().upper()


def map_observations(
    raw_batches: list[dict],
    fallback_network: str,
) -> tuple[list[dict], dict]:
    """Flatten data[].details[] into JmsObservation dicts plus a diagnostics blob.

    Waybill binding follows FullStackTrackingJourneyService.SelectDetails:
    match `data[].keyword` against the requested waybill first, then fall back
    to the `waybillNo`/`billCode` marker carried inside details[]."""
    observations: list[dict] = []
    key_hits: Counter[str] = Counter()
    detail_keys: Counter[str] = Counter()
    unmapped_time = 0
    matched_waybills: set[str] = set()
    requested_all: set[str] = set()
    envelope_failures: list[dict] = []

    for batch in raw_batches:
        requested_all.update(batch.get("requested") or [])
        response = batch.get("response")
        if not isinstance(response, dict):
            envelope_failures.append({
                "batch": batch.get("batch"),
                "httpStatus": batch.get("httpStatus"),
                "reason": "response body was not JSON",
            })
            continue
        if response.get("succ") is False:
            envelope_failures.append({
                "batch": batch.get("batch"),
                "httpStatus": batch.get("httpStatus"),
                "reason": str(response.get("msg") or response.get("message") or "succ=false"),
            })
            continue
        data = response.get("data")
        if not isinstance(data, list):
            envelope_failures.append({
                "batch": batch.get("batch"),
                "httpStatus": batch.get("httpStatus"),
                "reason": "response has no data[] array",
            })
            continue

        requested = batch.get("requested") or []
        for item in data:
            if not isinstance(item, dict):
                continue
            details = item.get("details")
            if not isinstance(details, list):
                continue

            keyword = str(item.get("keyword") or "").strip()
            waybill = next((w for w in requested if _same_waybill(w, keyword)), None)
            if waybill is None:
                for detail in details:
                    if not isinstance(detail, dict):
                        continue
                    for marker_key in ("waybillNo", "billCode", "keyword"):
                        marker = detail.get(marker_key)
                        hit = next((w for w in requested if _same_waybill(w, str(marker or ""))), None)
                        if hit:
                            waybill = hit
                            break
                    if waybill:
                        break
            if waybill is None:
                waybill = keyword or None
            if not waybill:
                continue

            for detail in details:
                if not isinstance(detail, dict):
                    continue
                detail_keys.update(detail.keys())

                time_value, time_key = _pick(detail, TIME_KEYS)
                scan_time = normalize_scan_time(time_value)
                if scan_time is None:
                    unmapped_time += 1
                    continue  # ScanTimeParser.ParseRequired would 400 on this row
                if time_key:
                    key_hits[f"time:{time_key}"] += 1

                code_value, code_key = _pick(detail, CODE_KEYS)
                if code_key:
                    key_hits[f"code:{code_key}"] += 1
                name_value, name_key = _pick(detail, NAME_KEYS)
                if name_key:
                    key_hits[f"name:{name_key}"] += 1
                status_value, _ = _pick(detail, STATUS_KEYS)
                network_value, _ = _pick(detail, NETWORK_KEYS)
                scan_by_value, _ = _pick(detail, SCAN_BY_KEYS)

                observations.append({
                    "waybillNo": waybill,
                    "scanTime": scan_time,
                    "code": _as_int(code_value),
                    "status": status_value,
                    "scanTypeName": name_value,
                    "scanNetworkCode": network_value or fallback_network,
                    "scanByCode": scan_by_value,
                    "payload": detail,
                })
                matched_waybills.add(waybill)

    diagnostics = {
        "requestedWaybills": len(requested_all),
        "waybillsWithJourney": len(matched_waybills),
        "observations": len(observations),
        "skippedUnparseableScanTime": unmapped_time,
        "sourceKeyHits": dict(key_hits.most_common()),
        "detailKeyFrequency": dict(detail_keys.most_common()),
        "envelopeFailures": envelope_failures,
        "waybillsWithoutJourney": sorted(requested_all - matched_waybills),
    }
    return observations, diagnostics


# --------------------------------------------------------------------------
# 4. DataHub upload
# --------------------------------------------------------------------------

def _batch_by_bytes(observations: list[dict], budget: int, max_items: int) -> list[list[dict]]:
    """Split so each serialized {"items": [...]} body stays under the 1 MiB cap."""
    batches: list[list[dict]] = []
    current: list[dict] = []
    current_bytes = len('{"items":[]}')
    for observation in observations:
        size = len(json.dumps(observation, ensure_ascii=False).encode("utf-8")) + 1
        if current and (current_bytes + size > budget or len(current) >= max_items):
            batches.append(current)
            current, current_bytes = [], len('{"items":[]}')
        current.append(observation)
        current_bytes += size
    if current:
        batches.append(current)
    return batches


def upload(
    observations: list[dict],
    base_url: str,
    site_id: str,
    device_token: str,
    max_items: int,
    timeout: int,
) -> dict:
    """POST to /jms/observations (unfenced -- no leader lease needed)."""
    url = f"{base_url.rstrip('/')}/api/v1/sites/{site_id}/jms/observations"
    batches = _batch_by_bytes(observations, DATAHUB_BATCH_BYTE_BUDGET, max_items)
    print(f"\n== upload: {len(observations)} observations in {len(batches)} batch(es) ==")
    print(f"   target : {url}")
    print(f"   token  : {mask(device_token)}")

    accepted = duplicates = failed = 0
    failures: list[dict] = []
    for number, batch in enumerate(batches, start=1):
        body = {"items": batch}
        encoded = json.dumps(body, ensure_ascii=False).encode("utf-8")
        if len(encoded) > DATAHUB_MAX_BODY_BYTES:
            failed += len(batch)
            failures.append({"batch": number, "status": "local", "detail": "body over 1 MiB"})
            print(f"  batch {number}/{len(batches)}: SKIPPED, {len(encoded)} bytes exceeds 1 MiB")
            continue

        # Content-derived so a re-run of the same batch is deduplicated by the
        # server rather than double-counted (8-128 chars per IngestEndpoints.cs:51).
        digest = hashlib.sha256(encoded).hexdigest()[:40]
        request = urllib.request.Request(url, data=encoded, method="POST")
        request.add_header("Content-Type", "application/json; charset=utf-8")
        request.add_header("Authorization", f"Bearer {device_token}")
        request.add_header("Idempotency-Key", f"harvest-{digest}")

        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                status = response.status
                text = response.read().decode("utf-8", errors="replace")
        except urllib.error.HTTPError as error:
            status = error.code
            text = error.read().decode("utf-8", errors="replace")
        except urllib.error.URLError as error:
            status = 0
            text = f"URLError: {error.reason}"

        if status == 200:
            try:
                parsed = json.loads(text)
            except json.JSONDecodeError:
                parsed = {}
            batch_accepted = parsed.get("accepted")
            batch_duplicate = parsed.get("duplicates", parsed.get("duplicate"))
            accepted += batch_accepted if isinstance(batch_accepted, int) else len(batch)
            if isinstance(batch_duplicate, int):
                duplicates += batch_duplicate
            print(
                f"  batch {number}/{len(batches)}: {len(batch)} items, {len(encoded)} bytes "
                f"-> 200 accepted={batch_accepted} duplicates={batch_duplicate}"
            )
        else:
            failed += len(batch)
            preview = re.sub(r"\s+", " ", text)[:220]
            failures.append({"batch": number, "status": status, "detail": preview})
            print(f"  batch {number}/{len(batches)}: {len(batch)} items -> HTTP {status}: {preview}")

        time.sleep(DATAHUB_MIN_REQUEST_INTERVAL_S)

    return {"batches": len(batches), "accepted": accepted, "duplicates": duplicates,
            "failed": failed, "failures": failures}


# --------------------------------------------------------------------------
# 5. Reporting
# --------------------------------------------------------------------------

def print_vocabulary(observations: list[dict], diagnostics: dict) -> None:
    print("\n" + "=" * 78)
    print("  SCAN VOCABULARY -- observed, not decided")
    print("=" * 78)
    print(f"  waybills requested        : {diagnostics['requestedWaybills']}")
    print(f"  waybills with a journey   : {diagnostics['waybillsWithJourney']}")
    print(f"  scan events harvested     : {diagnostics['observations']}")
    print(f"  skipped (bad scanTime)    : {diagnostics['skippedUnparseableScanTime']}")

    if not observations:
        print("\n  no observations -- nothing to tabulate")
        return

    times = sorted(o["scanTime"] for o in observations)
    cutoff = (datetime.now(VN_TZ) - timedelta(days=14)).strftime("%Y-%m-%d %H:%M:%S")
    settled = sum(1 for t in times if t < cutoff)
    print(f"  event time range          : {times[0]}  ..  {times[-1]}")
    # od1_scan_vocabulary.sql query 0.2 only counts waybills whose last event is
    # older than 14 days; with zero settled rows that query returns nothing.
    print(f"  events older than 14 days : {settled}  ({100.0 * settled / len(times):.1f}%)"
          f"   <- query 0.2 needs these")

    vocabulary: Counter[tuple[int | None, str | None]] = Counter()
    waybills_per_key: dict[tuple[int | None, str | None], set[str]] = {}
    for observation in observations:
        key = (observation["code"], observation["scanTypeName"])
        vocabulary[key] += 1
        waybills_per_key.setdefault(key, set()).add(observation["waybillNo"])

    # "Final" here means last-by-time within this harvest, which is weaker than
    # the settled-waybill test od1_scan_vocabulary.sql applies. It is a preview.
    last_event: dict[str, tuple[str, tuple[int | None, str | None]]] = {}
    for observation in observations:
        waybill = observation["waybillNo"]
        current = last_event.get(waybill)
        if current is None or observation["scanTime"] > current[0]:
            last_event[waybill] = (observation["scanTime"], (observation["code"], observation["scanTypeName"]))
    final_counts: Counter[tuple[int | None, str | None]] = Counter(k for _, k in last_event.values())

    print(f"\n  distinct (code, name) pairs: {len(vocabulary)}\n")
    print(f"  {'code':>6}  {'events':>7} {'waybills':>9} {'final':>6}  {'?':1}  name")
    print(f"  {'-'*6}  {'-'*7} {'-'*9} {'-'*6}  {'-'*1}  {'-'*40}")
    for (code, name), events in vocabulary.most_common():
        hint = "T" if name and any(h in name for h in TERMINAL_HINTS) else " "
        print(
            f"  {('' if code is None else code):>6}  {events:>7} "
            f"{len(waybills_per_key[(code, name)]):>9} {final_counts.get((code, name), 0):>6}  "
            f"{hint}  {name or '<null>'}"
        )
    print("\n  'T' flags a name containing 签收 / 退件 / Ky nhan / chuyen hoan.")
    print("  It is a reading aid ONLY. OD-1 is signed by the Owner against")
    print("  backend/datahub/tests/od1_scan_vocabulary.sql run on the loaded data.")

    hits = diagnostics.get("sourceKeyHits") or {}
    if hits:
        print("\n  JMS field names actually seen (mapped_field:source_key -> hits):")
        for key, count in hits.items():
            print(f"    {key:<28} {count}")

    failures = diagnostics.get("envelopeFailures") or []
    if failures:
        print(f"\n  {len(failures)} batch envelope failure(s):")
        for failure in failures[:10]:
            print(f"    batch {failure['batch']}: HTTP {failure['httpStatus']} -- {failure['reason']}")

    missing = diagnostics.get("waybillsWithoutJourney") or []
    if missing:
        shown = ", ".join(missing[:10])
        more = f" (+{len(missing) - 10} more)" if len(missing) > 10 else ""
        print(f"\n  {len(missing)} waybill(s) returned no journey: {shown}{more}")


def print_inspection(diagnostics: dict, raw_batches: list[dict], samples: int) -> None:
    print("\n" + "=" * 78)
    print("  RAW details[] SHAPE -- for P1 schema/partition design")
    print("=" * 78)
    frequency = diagnostics.get("detailKeyFrequency") or {}
    print(f"  {len(frequency)} distinct keys across all details[] objects\n")
    for key, count in frequency.items():
        print(f"    {key:<32} {count}")

    printed = 0
    for batch in raw_batches:
        response = batch.get("response")
        if not isinstance(response, dict) or not isinstance(response.get("data"), list):
            continue
        for item in response["data"]:
            if not isinstance(item, dict):
                continue
            for detail in item.get("details") or []:
                if printed >= samples:
                    return
                print(f"\n  --- sample detail #{printed + 1} ---")
                print("  " + json.dumps(detail, ensure_ascii=False, indent=2).replace("\n", "\n  "))
                printed += 1


# --------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Harvest real JMS journeys into DataHub observations (OD-1 evidence).",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    source = parser.add_argument_group("source")
    source.add_argument("--excel", help="workbook of waybills, e.g. docs/manual/waybilltest.xlsx")
    source.add_argument("--column", default="Mã vận đơn", help="header of the waybill column")
    source.add_argument("--from-raw", help="re-map a previously saved jms_raw_*.json (skips JMS)")
    source.add_argument("--limit", type=int, help="use only the first N waybills")

    jms = parser.add_argument_group("JMS")
    jms.add_argument("--jms-token", default=os.environ.get("JMS_AUTH_TOKEN"),
                     help="authToken; prefer env JMS_AUTH_TOKEN")
    jms.add_argument("--jms-api-base",
                     default=os.environ.get("AUTOJMS_JMS_API_BASE_URL", JMS_API_BASE),
                     help=f"JMS API gateway (default {JMS_API_BASE}); NOT the web UI host")
    jms.add_argument("--batch-size", type=int, default=50, help="waybills per JMS request")
    jms.add_argument("--jms-delay", type=float, default=1.0, help="seconds between JMS requests")
    jms.add_argument("--timeout", type=int, default=60, help="HTTP timeout, seconds")

    hub = parser.add_argument_group("DataHub")
    hub.add_argument("--datahub-url", default=os.environ.get("DATAHUB_URL"),
                     help="base URL, e.g. https://dev.jmsauto.online")
    hub.add_argument("--site-id", default=os.environ.get("DATAHUB_SITE_ID"), help="site GUID")
    hub.add_argument("--device-token", default=os.environ.get("DATAHUB_DEVICE_TOKEN"),
                     help="device bearer token; prefer env DATAHUB_DEVICE_TOKEN")
    hub.add_argument("--site-code", default="214A03",
                     help="fallback scanNetworkCode when JMS omits one")
    hub.add_argument("--max-items", type=int, default=100, help="max observations per request")

    out = parser.add_argument_group("output")
    out.add_argument("--out-dir", default="docs/manual/samples", help="where raw + mapped JSON go")
    out.add_argument("--dry-run", action="store_true", help="read the workbook only; no network")
    out.add_argument("--inspect", action="store_true", help="dump raw details[] keys and samples")
    out.add_argument("--inspect-samples", type=int, default=2, help="sample details to print")
    out.add_argument("--report", action="store_true", help="print the vocabulary table")
    out.add_argument("--no-save", action="store_true", help="do not write files")
    return parser


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        # Vietnamese and CJK scan names must survive a non-UTF-8 console.
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    args = build_parser().parse_args(argv)
    if not args.excel and not args.from_raw:
        print("error: one of --excel or --from-raw is required", file=sys.stderr)
        return 2

    raw_batches: list[dict] = []
    stamp = datetime.now(VN_TZ).strftime("%Y%m%d%H%M%S")

    if args.from_raw:
        print(f"== reading saved raw: {args.from_raw} ==")
        with open(args.from_raw, "r", encoding="utf-8") as handle:
            saved = json.load(handle)
        raw_batches = saved.get("batches") if isinstance(saved, dict) else saved
        if not isinstance(raw_batches, list):
            print("error: raw file does not contain a batches array", file=sys.stderr)
            return 2
        print(f"   {len(raw_batches)} batch(es) restored")
    else:
        print(f"== reading workbook: {args.excel} ==")
        waybills, warnings = read_waybills(args.excel, args.column)
        for warning in warnings:
            print(f"   WARN {warning}")
        print(f"   {len(waybills)} waybill(s) from column {args.column!r}")
        if waybills:
            print(f"   first: {waybills[0]}   last: {waybills[-1]}")
        if args.limit:
            waybills = waybills[:args.limit]
            print(f"   limited to {len(waybills)}")
        if args.dry_run:
            print("\n-- dry run: no JMS call, no upload --")
            return 0
        if not args.jms_token:
            print("error: --jms-token or env JMS_AUTH_TOKEN is required", file=sys.stderr)
            return 2

        print(f"\n== JMS: {args.jms_api_base.rstrip('/')}/{JMS_PATH} ==")
        print(f"   authToken {mask(args.jms_token)}, batch {args.batch_size}, "
              f"delay {args.jms_delay}s")
        raw_batches = fetch_batches(
            waybills, args.jms_token, args.batch_size, args.jms_delay, args.timeout,
            args.jms_api_base,
        )

        if not args.no_save:
            os.makedirs(args.out_dir, exist_ok=True)
            raw_path = os.path.join(args.out_dir, f"jms_raw_{stamp}.json")
            with open(raw_path, "w", encoding="utf-8") as handle:
                json.dump(
                    {"harvestedAt": datetime.now(VN_TZ).isoformat(timespec="seconds"),
                     "endpoint": f"{args.jms_api_base.rstrip(chr(47))}/{JMS_PATH}",
                     "batches": raw_batches},
                    handle, ensure_ascii=False, indent=1,
                )
            print(f"\n   raw saved: {raw_path} ({os.path.getsize(raw_path):,} bytes)")

    observations, diagnostics = map_observations(raw_batches, args.site_code)

    if not args.no_save and observations:
        os.makedirs(args.out_dir, exist_ok=True)
        mapped_path = os.path.join(args.out_dir, "harvested_observations.json")
        with open(mapped_path, "w", encoding="utf-8") as handle:
            json.dump({"items": observations}, handle, ensure_ascii=False, indent=1)
        print(f"   mapped saved: {mapped_path} ({os.path.getsize(mapped_path):,} bytes)")

    if args.inspect:
        print_inspection(diagnostics, raw_batches, args.inspect_samples)
    if args.report or not (args.site_id and args.device_token):
        print_vocabulary(observations, diagnostics)

    if args.datahub_url and args.site_id and args.device_token:
        if not observations:
            print("\n-- nothing mapped, skipping upload --")
            return 1
        summary = upload(
            observations, args.datahub_url, args.site_id, args.device_token,
            args.max_items, args.timeout,
        )
        print(f"\n   accepted={summary['accepted']} duplicates={summary['duplicates']} "
              f"failed={summary['failed']} over {summary['batches']} batch(es)")
        if summary["failed"]:
            return 1
    elif not args.dry_run and not args.from_raw:
        print("\n-- upload skipped: --datahub-url, --site-id and --device-token all required --")

    return 0


if __name__ == "__main__":
    sys.exit(main())
