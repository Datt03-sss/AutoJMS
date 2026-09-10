#!/usr/bin/env python3
"""Test harness for the two AutoJMS inventory sources.

Source 1 — the operator's exported workbook (``docs/manual/tonkho30ngay.xlsx``).
Source 2 — the live Doris2 report the desktop app pages through
           (``businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2``).

The two drift apart within minutes, so ``--compare`` diffs them and reports which
side each waybill is missing from. Standard library only: no openpyxl, no pandas,
no requests — the same constraint tools/harvest_jms_events.py runs under.

The API gateway is ``jmsgw.jtexpress.vn``. ``jms.jtexpress.vn`` is the web UI and
only ever appears in Origin/Referer; POSTing to it returns a bare nginx 405.

Examples
--------
    python tools/test_inventory_fetch.py --excel docs/manual/tonkho30ngay.xlsx
    python tools/test_inventory_fetch.py --jms-token "$JMS_AUTH_TOKEN" --inspect
    python tools/test_inventory_fetch.py --excel docs/manual/tonkho30ngay.xlsx \
        --jms-token "$JMS_AUTH_TOKEN" --all-pages --compare

Never pass the token as a literal in a shared shell; export JMS_AUTH_TOKEN instead.
"""

from __future__ import annotations

import argparse
import gzip
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

XLSX_NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"

JMS_ORIGIN = "https://jms.jtexpress.vn"
JMS_API_BASE = "https://jmsgw.jtexpress.vn"
DORIS_PATH = "businessindicator/bigdataReport/detail/take_ret_mon_detail_doris2"

# Mirrors FullStackInventorySyncService.cs:22-23.
INVENTORY_ROUTE_NAME = "DetentionMonitoringDB"
INVENTORY_ROUTER_NAME_LIST = (
    "%E7%BB%8F%E8%90%A5%E6%8C%87%E6%A0%87%3E%E6%B4%BE%E4%BB%B6%E7%AB%AF%3E%E7%95%99%E4%BB%93%E7%9B%91%E6%8E%A7DB"
)

DEFAULT_SITE_CODE = "214A03"
DEFAULT_DIMENSION = "2"
PAGE_SIZE = 100
VN_TZ = timezone(timedelta(hours=7))

# Same probe order FullStackInventorySyncService.ExtractBillcode uses.
BILLCODE_KEYS = ("billcode", "billCode", "waybillNo", "waybill_no", "mailNo")

# The workbook carries child packages as <billcode>-001, so the suffix is legal.
WAYBILL_RE = re.compile(r"^[0-9A-Z]{9,20}(-[0-9]{3})?$")


def mask(secret: str) -> str:
    """first4...last4, per the Secret Policy in CLAUDE.md."""
    if not secret:
        return "<none>"
    return f"{secret[:4]}...{secret[-4:]}" if len(secret) >= 8 else "<short>"


# --------------------------------------------------------------------------
# 1. Excel
# --------------------------------------------------------------------------

def _column_index(ref: str) -> int:
    """'BC12' -> 54. Cells are omitted when empty, so the letters are the only
    reliable way to know which column a value belongs to."""
    index = 0
    for char in ref:
        if not char.isalpha():
            break
        index = index * 26 + (ord(char.upper()) - 64)
    return index - 1


def _cell_text(cell: ET.Element, shared: list[str]) -> str:
    kind = cell.get("t")
    if kind == "s":
        node = cell.find(XLSX_NS + "v")
        if node is None or node.text is None:
            return ""
        try:
            return shared[int(node.text)]
        except (ValueError, IndexError):
            return ""
    if kind == "inlineStr":
        node = cell.find(XLSX_NS + "is")
        return "".join(t.text or "" for t in node.iter(XLSX_NS + "t")) if node is not None else ""
    node = cell.find(XLSX_NS + "v")
    if node is None or node.text is None:
        return ""
    text = node.text
    # Long numeric waybills round-trip through Excel as 8.02819444675E11.
    if "E" in text or "e" in text:
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


def report_excel(path: str, column: str) -> list[str]:
    waybills, warnings = read_waybills(path, column)
    print(f"[excel] file       : {path}")
    print(f"[excel] column     : {column!r}")
    print(f"[excel] waybills   : {len(waybills):,}")
    for warning in warnings:
        print(f"[excel] WARNING    : {warning}")
    if not waybills:
        return waybills

    lengths = Counter(len(w) for w in waybills)
    prefixes = Counter(w[:3] for w in waybills)
    malformed = [w for w in waybills if not WAYBILL_RE.match(w.upper())]

    print(f"[excel] first/last : {waybills[0]} .. {waybills[-1]}")
    print("[excel] lengths    : " + ", ".join(f"{n}→{c:,}" for n, c in sorted(lengths.items())))
    print("[excel] prefixes   : " + ", ".join(f"{p}→{c:,}" for p, c in prefixes.most_common(6)))
    children = [w for w in waybills if "-" in w]
    if children:
        print(f"[excel] child pkgs : {len(children):,} carry a -NNN suffix (e.g. {children[0]})")
    if malformed:
        print(f"[excel] malformed  : {len(malformed):,} (e.g. {malformed[:3]})")
    else:
        print(f"[excel] malformed  : 0 — every code matches {WAYBILL_RE.pattern}")
    return waybills


# --------------------------------------------------------------------------
# 2. Doris2 API
# --------------------------------------------------------------------------

def doris_headers(token: str) -> dict[str, str]:
    """Mirrors FullStackInventorySyncService.FetchOnePageAsync + JmsApiClient."""
    return {
        "Content-Type": "application/json;charset=UTF-8",
        "authToken": token,
        "lang": "VN",
        "langType": "VN",
        "timezone": "GMT+0700",
        "routeName": INVENTORY_ROUTE_NAME,
        "routerNameList": INVENTORY_ROUTER_NAME_LIST,
        "Accept": "application/json, text/plain, */*",
        "Origin": JMS_ORIGIN,
        "Referer": JMS_ORIGIN + "/",
        "User-Agent": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
            "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        ),
    }


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


def _find_records(root) -> tuple[list, str]:
    """Same candidate ladder as FullStackInventorySyncService.FindRecordsArray."""
    for path in (
        ("data", "records"), ("data", "list"), ("data", "rows"),
        ("data", "data"), ("data", "result"),
        ("records",), ("rows",), ("list",), ("data",),
    ):
        node = root
        ok = True
        for key in path:
            if not isinstance(node, dict) or key not in node:
                ok = False
                break
            node = node[key]
        if ok and isinstance(node, list):
            return node, ".".join(path)
    return [], "(none)"


def _find_int(root, paths) -> tuple[int, str]:
    for path in paths:
        node = root
        ok = True
        for key in path:
            if not isinstance(node, dict) or key not in node:
                ok = False
                break
            node = node[key]
        if ok and isinstance(node, int):
            return node, ".".join(path)
    return 0, "(none)"


def extract_billcode(record: dict) -> str | None:
    for key in BILLCODE_KEYS:
        value = record.get(key)
        if isinstance(value, str) and value.strip():
            return value.strip()
        if isinstance(value, (int, float)):
            return format(int(value), "d")
    return None


def fetch_page(
    token: str,
    site_code: str,
    start: str,
    end: str,
    page: int,
    dimension: str,
    size: int,
    timeout: int,
    api_base: str,
    is_flag: str | None,
) -> dict:
    url = f"{api_base.rstrip('/')}/{DORIS_PATH}"
    body = {
        "current": page,
        "size": size,
        "dimension": dimension,
        "actionSiteCode": site_code,
        "startDate": start,
        "endDate": end,
        "countryId": "1",
    }
    if is_flag is not None:
        body["isFlag"] = is_flag

    started = time.monotonic()
    status, text = _http_post_json(url, doris_headers(token), body, timeout)
    elapsed = time.monotonic() - started

    result = {
        "page": page, "status": status, "elapsed": elapsed,
        "records": [], "total": 0, "pages": 0, "error": None,
        "records_path": "(none)", "total_path": "(none)", "raw": None,
    }
    if status != 200:
        result["error"] = f"HTTP {status}: {text[:200]}"
        return result
    try:
        parsed = json.loads(text)
    except json.JSONDecodeError as error:
        result["error"] = f"response is not JSON ({error}); first 200 chars: {text[:200]}"
        return result

    result["raw"] = parsed
    if parsed.get("code") != 1 and parsed.get("succ") is not True:
        result["error"] = f"JMS code={parsed.get('code')} msg={parsed.get('msg')!r}"
        return result

    records, records_path = _find_records(parsed)
    total, total_path = _find_int(parsed, [
        ("data", "total"), ("data", "totalRecords"), ("data", "recordsTotal"),
        ("data", "count"), ("total",), ("totalRecords",),
    ])
    pages, _ = _find_int(parsed, [
        ("data", "pages"), ("data", "totalPage"), ("data", "totalPages"),
        ("pages",), ("totalPages",),
    ])
    result.update(records=records, total=total, pages=pages,
                  records_path=records_path, total_path=total_path)
    return result


def fetch_all(args, token: str, start: str, end: str) -> tuple[list[dict], list[str]]:
    """Page through Doris2. Returns (raw pages, deduped waybills)."""
    pages: list[dict] = []
    seen: OrderedDict[str, None] = OrderedDict()

    first = fetch_page(token, args.site_code, start, end, 1, args.dimension,
                       args.size, args.timeout, args.jms_api_base, args.is_flag)
    pages.append(first)
    if first["error"]:
        print(f"[doris] page 1     : FAILED — {first['error']}")
        return pages, []

    print(f"[doris] endpoint   : {args.jms_api_base.rstrip('/')}/{DORIS_PATH}")
    print(f"[doris] site       : {args.site_code}  dimension={args.dimension}  size={args.size}"
          + (f"  isFlag={args.is_flag}" if args.is_flag is not None else "  isFlag=(omitted)"))
    print(f"[doris] window     : {start} .. {end}")
    print(f"[doris] page 1     : HTTP 200 in {first['elapsed']:.2f}s — "
          f"{len(first['records'])} records, total={first['total']}, pages={first['pages']} "
          f"(records at {first['records_path']}, total at {first['total_path']})")

    for record in first["records"]:
        code = extract_billcode(record) if isinstance(record, dict) else None
        if code:
            seen[code.upper()] = None

    total_pages = first["pages"] or (
        -(-first["total"] // args.size) if first["total"] > 0 else 1
    )
    wanted = total_pages if args.all_pages else min(args.limit_pages, total_pages)
    if wanted > 1:
        print(f"[doris] paging     : fetching pages 2..{wanted} of {total_pages}")

    for page in range(2, wanted + 1):
        result = fetch_page(token, args.site_code, start, end, page, args.dimension,
                            args.size, args.timeout, args.jms_api_base, args.is_flag)
        pages.append(result)
        if result["error"]:
            print(f"[doris] page {page:<3}   : FAILED — {result['error']}")
            continue
        before = len(seen)
        for record in result["records"]:
            code = extract_billcode(record) if isinstance(record, dict) else None
            if code:
                seen[code.upper()] = None
        print(f"[doris] page {page:<3}   : HTTP 200 in {result['elapsed']:.2f}s — "
              f"{len(result['records'])} records, +{len(seen) - before} new")
        if args.delay > 0:
            time.sleep(args.delay)

    print(f"[doris] waybills   : {len(seen):,} distinct across {len(pages)} page(s)")
    return pages, list(seen.keys())


# --------------------------------------------------------------------------
# 3. Reporting
# --------------------------------------------------------------------------

def print_inspection(pages: list[dict], sample_count: int) -> None:
    records = [r for p in pages for r in p["records"] if isinstance(r, dict)]
    if not records:
        print("[inspect] no records to inspect")
        return

    coverage = Counter()
    non_null = Counter()
    for record in records:
        for key, value in record.items():
            coverage[key] += 1
            if value not in (None, "", []):
                non_null[key] += 1

    print()
    print(f"[inspect] {len(records)} record(s), {len(coverage)} distinct keys")
    print(f"[inspect] {'key':<28} {'present':>8} {'non-null':>9}  sample")
    print("[inspect] " + "-" * 74)
    for key, count in coverage.most_common():
        sample = ""
        for record in records:
            value = record.get(key)
            if value not in (None, "", []):
                sample = str(value)
                break
        if len(sample) > 26:
            sample = sample[:23] + "..."
        print(f"[inspect] {key:<28} {count:>8} {non_null[key]:>9}  {sample}")

    for index, record in enumerate(records[:sample_count], start=1):
        print()
        print(f"[inspect] --- full record {index} ---")
        print(json.dumps(record, ensure_ascii=False, indent=2))


def compare(excel_codes: list[str], api_codes: list[str]) -> None:
    excel_set = {c.upper() for c in excel_codes}
    api_set = {c.upper() for c in api_codes}
    both = excel_set & api_set
    only_excel = excel_set - api_set
    only_api = api_set - excel_set

    print()
    print("[compare] Excel snapshot vs live Doris2")
    print(f"[compare] in both      : {len(both):,}")
    print(f"[compare] only in Excel: {len(only_excel):,} — left inventory since the export")
    print(f"[compare] only in API  : {len(only_api):,} — arrived since the export")
    if excel_set:
        print(f"[compare] agreement    : {100.0 * len(both) / len(excel_set):.1f}% of the Excel rows")
    if only_excel:
        print(f"[compare] sample gone  : {sorted(only_excel)[:5]}")
    if only_api:
        print(f"[compare] sample new   : {sorted(only_api)[:5]}")


def dump_raw(path: str, pages: list[dict], args, start: str, end: str) -> None:
    directory = os.path.dirname(path)
    if directory:
        os.makedirs(directory, exist_ok=True)
    document = {
        "fetchedAt": datetime.now(VN_TZ).strftime("%Y-%m-%d %H:%M:%S%z"),
        "endpoint": f"{args.jms_api_base.rstrip('/')}/{DORIS_PATH}",
        "request": {
            "size": args.size, "dimension": args.dimension,
            "actionSiteCode": args.site_code, "startDate": start, "endDate": end,
            "countryId": "1", **({"isFlag": args.is_flag} if args.is_flag is not None else {}),
        },
        "pages": [
            {"page": p["page"], "status": p["status"], "total": p["total"],
             "pages": p["pages"], "error": p["error"], "body": p["raw"]}
            for p in pages
        ],
    }
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(document, handle, ensure_ascii=False, indent=2)
    print(f"[dump]    raw saved  : {path} ({os.path.getsize(path):,} bytes)")


# --------------------------------------------------------------------------
# 4. CLI
# --------------------------------------------------------------------------

def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Test the AutoJMS inventory sources: the exported workbook and the live Doris2 report.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument("--excel", help="workbook to read waybills from")
    parser.add_argument("--column", default="Mã vận đơn", help="header of the waybill column")

    parser.add_argument("--jms-token", default=os.environ.get("JMS_AUTH_TOKEN", ""),
                        help="JMS authToken (defaults to $JMS_AUTH_TOKEN)")
    parser.add_argument("--jms-api-base", default=os.environ.get("AUTOJMS_JMS_API_BASE_URL", JMS_API_BASE),
                        help=f"API gateway base URL (default {JMS_API_BASE})")
    parser.add_argument("--site-code", default=DEFAULT_SITE_CODE, help="actionSiteCode")
    parser.add_argument("--dimension", default=DEFAULT_DIMENSION,
                        help=f"Doris2 dimension (default {DEFAULT_DIMENSION}; the app currently sends 3)")
    parser.add_argument("--is-flag", default=None,
                        help="send isFlag with this value (the app sends '1'; omitted by default)")
    parser.add_argument("--start-date", "--startDate", dest="start_date",
                        help="yyyy-MM-dd HH:mm:ss (default: 30 days before end)")
    parser.add_argument("--end-date", "--endDate", dest="end_date",
                        help="yyyy-MM-dd HH:mm:ss (default: end of today, VN time)")
    parser.add_argument("--size", type=int, default=PAGE_SIZE, help="page size")
    parser.add_argument("--limit-pages", type=int, default=1, help="how many pages to fetch")
    parser.add_argument("--all-pages", action="store_true", help="fetch every page")
    parser.add_argument("--delay", type=float, default=0.2, help="seconds between pages")
    parser.add_argument("--timeout", type=int, default=60, help="per-request timeout")

    parser.add_argument("--inspect", action="store_true", help="print the record field structure")
    parser.add_argument("--inspect-samples", type=int, default=2, help="how many full records to print")
    parser.add_argument("--dump", help="write the raw pages to this path")
    parser.add_argument("--compare", action="store_true", help="diff the workbook against the live API")
    return parser


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")  # Vietnamese survives a cp1252 console
    args = build_parser().parse_args(argv)

    if not args.excel and not args.jms_token:
        print("Nothing to do: pass --excel, or --jms-token (or set JMS_AUTH_TOKEN).", file=sys.stderr)
        return 2

    excel_codes: list[str] = []
    if args.excel:
        excel_codes = report_excel(args.excel, args.column)

    api_codes: list[str] = []
    if args.jms_token:
        if args.excel:
            print()
        end = args.end_date or datetime.now(VN_TZ).strftime("%Y-%m-%d 23:59:59")
        if args.start_date:
            start = args.start_date
        else:
            end_day = datetime.strptime(end[:10], "%Y-%m-%d")
            start = (end_day - timedelta(days=30)).strftime("%Y-%m-%d 00:00:00")

        print(f"[doris] token      : {mask(args.jms_token)}")
        pages, api_codes = fetch_all(args, args.jms_token, start, end)

        if args.inspect:
            print_inspection(pages, args.inspect_samples)
        if args.dump:
            dump_raw(args.dump, pages, args, start, end)
        if not api_codes and pages and pages[0]["error"]:
            return 1

    if args.compare:
        if excel_codes and api_codes:
            compare(excel_codes, api_codes)
        else:
            print("\n[compare] skipped — needs both --excel and a working --jms-token")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
