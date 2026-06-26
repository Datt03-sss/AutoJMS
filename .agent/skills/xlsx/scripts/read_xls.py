#!/usr/bin/env python3
"""Read legacy .xls (and modern .xlsx) workbooks: list sheets or preview one.

openpyxl / `extract-text` CANNOT read the legacy BIFF .xls format. This helper
uses the xlrd engine for .xls and falls back to the pandas default for .xlsx.

Usage:
  python read_xls.py <file>                 # list every sheet + row/col counts
  python read_xls.py <file> "<sheet name>"  # preview a sheet (first 10 rows)
  python read_xls.py <file> "<sheet name>" 25   # preview first N rows
  python read_xls.py <file> --to-xlsx       # convert .xls -> .xlsx (LibreOffice)

Deps: pip install xlrd   (only needed for .xls; .xlsx uses openpyxl/pandas)
"""
import sys
from pathlib import Path

import pandas as pd


def engine_for(path: Path):
    return "xlrd" if path.suffix.lower() == ".xls" else None


def list_sheets(path: Path):
    xls = pd.ExcelFile(path, engine=engine_for(path))
    print(f"{path.name}  -  {len(xls.sheet_names)} sheet(s)")
    for s in xls.sheet_names:
        df = pd.read_excel(path, sheet_name=s, header=None, engine=engine_for(path))
        print(f"  [{s}]  rows={df.shape[0]}  cols={df.shape[1]}")


def preview(path: Path, sheet: str, n: int):
    df = pd.read_excel(path, sheet_name=sheet, header=None, engine=engine_for(path))
    with pd.option_context("display.max_columns", None, "display.width", 200):
        print(df.head(n).fillna("").to_string())


def to_xlsx(path: Path):
    import subprocess

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from office.soffice import get_soffice_env

    subprocess.run(
        ["soffice", "--headless", "--convert-to", "xlsx",
         "--outdir", str(path.parent), str(path)],
        check=True, env=get_soffice_env(),
    )
    print(f"wrote {path.with_suffix('.xlsx')}")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        sys.exit(1)
    p = Path(args[0])
    if "--to-xlsx" in args:
        to_xlsx(p)
    elif len(args) >= 2:
        preview(p, args[1], int(args[2]) if len(args) >= 3 else 10)
    else:
        list_sheets(p)
