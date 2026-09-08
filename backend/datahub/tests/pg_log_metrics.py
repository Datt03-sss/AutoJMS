#!/usr/bin/env python3
"""Recover commit_p95 and transaction_p95 from a PostgreSQL server log.

Why this exists: docs/review/p0-report-2026-09-07.md records that metrics 2 and 3 of the
runbook's five were never computed for G7/G8, and that both were already sitting in the log
`log_min_duration_statement = 0` produced -- commit_p95 is one grep away, and
transaction_p95 is the BEGIN->COMMIT cadence per backend PID. This is those two
derivations, made reproducible and testable instead of typed once at a prompt.

Reads a log written with `log_line_prefix = '%m [%p] %a '` (the value P0's Task 5 set) and
reports:

  commit_*       -- the duration PostgreSQL itself reports for the COMMIT statement
  transaction_*  -- wall time from a PID's BEGIN line to its next COMMIT line

Read-only: it opens one file, connects to nothing, and writes only JSON on stdout.

  python pg_log_metrics.py --log /path/outside/the/repo/pg.log
  python pg_log_metrics.py --self-test

SECURITY: a `log_min_duration_statement = 0` capture contains every statement the API ran.
Keep the log file OUTSIDE this repository's working tree. `*.log` is gitignored, but the
secret gate's untracked-file pass still reads anything sitting in the tree.
"""

import argparse
import json
import re
import statistics
import sys
import unittest
from datetime import datetime

# `%m [%p] %a ` then PostgreSQL's own `LOG:  duration: N ms  <kind>: <sql>`.
# %a is the application_name and is empty for some backends, so the gap between the PID and
# LOG: is matched loosely. The `<kind>` is `statement` for the simple query protocol and
# `execute`/`parse`/`bind <name>` for the extended protocol Npgsql uses.
LINE_RE = re.compile(
    r"^(?P<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})"
    r".*?\[(?P<pid>\d+)\]"
    r".*?\bduration:\s(?P<ms>\d+\.\d+)\sms"
    r"(?:\s+(?:statement|execute[^:]*|parse[^:]*|bind[^:]*):\s*(?P<sql>.*))?$"
)

TS_FORMAT = "%Y-%m-%d %H:%M:%S.%f"


def pct(values, q):
    # Copied verbatim from baseline_load.py:306-307, where it is nested inside main() and so
    # cannot be imported. It must stay identical: a different percentile method would make
    # these numbers quietly incomparable with the published baselines in report section 5.
    # If one changes, change both.
    return round(statistics.quantiles(values, n=100)[q - 1], 1) if len(values) > 100 else None


def parse(lines):
    """Return (commit_durations_ms, transaction_spans_ms, unpaired_commits, unclosed)."""
    commits = []
    transactions = []
    open_txn = {}
    unpaired_commits = 0

    for line in lines:
        match = LINE_RE.match(line)
        if match is None:
            continue
        sql = (match.group("sql") or "").strip().rstrip(";").upper()
        if not sql:
            continue
        pid = match.group("pid")
        stamp = datetime.strptime(match.group("ts"), TS_FORMAT)

        if sql.startswith("BEGIN"):
            # A second BEGIN without a COMMIT means the first transaction's end is not in
            # this log; the later one wins and the earlier is counted as unclosed at the end.
            open_txn[pid] = stamp
        elif sql.startswith("COMMIT"):
            commits.append(float(match.group("ms")))
            began = open_txn.pop(pid, None)
            if began is None:
                unpaired_commits += 1
            else:
                transactions.append((stamp - began).total_seconds() * 1000.0)

    return commits, transactions, unpaired_commits, len(open_txn)


def report(commits, transactions, unpaired_commits, unclosed):
    return {
        "commit_samples": len(commits),
        "commit_p50_ms": pct(commits, 50),
        "commit_p95_ms": pct(commits, 95),
        "commit_p99_ms": pct(commits, 99),
        "commit_max_ms": round(max(commits), 1) if commits else None,
        "transaction_samples": len(transactions),
        "transaction_p50_ms": pct(transactions, 50),
        "transaction_p95_ms": pct(transactions, 95),
        "transaction_p99_ms": pct(transactions, 99),
        "transaction_max_ms": round(max(transactions), 1) if transactions else None,
        "unpaired_commits": unpaired_commits,
        "unclosed_transactions": unclosed,
        "notes": (
            "commit_* is the duration PostgreSQL reports for the COMMIT statement itself. "
            "transaction_* is the interval between a PID's BEGIN log line and its COMMIT log "
            "line; both timestamps mark statement COMPLETION, so the span excludes BEGIN's "
            "own duration and is a slight underestimate. Percentiles are None below 101 "
            "samples, matching baseline_load.py. unpaired_commits and unclosed_transactions "
            "count transactions straddling the ends of the log; a large value means the "
            "capture window clipped the run and the percentiles cover a biased subset."
        ),
    }


class SelfTest(unittest.TestCase):
    FIXTURE = "fixtures/pg_log_sample.log"

    def fixture_lines(self):
        import os
        path = os.path.join(os.path.dirname(os.path.abspath(__file__)), *self.FIXTURE.split("/"))
        with open(path, encoding="utf-8") as handle:
            return handle.readlines()

    def test_commit_durations_are_every_commit_and_only_commits(self):
        commits, _, _, _ = parse(self.fixture_lines())
        self.assertEqual([0.812, 2.400, 5.500, 0.900], commits)

    def test_transaction_spans_pair_begin_to_commit_per_pid(self):
        _, transactions, _, _ = parse(self.fixture_lines())
        # Interleaved PIDs must not cross-pair: 1841's first COMMIT closes 1841's BEGIN,
        # not 1902's, even though 1902 began in between.
        self.assertEqual([200.0, 175.0, 112.0], [round(t, 1) for t in transactions])

    def test_transactions_straddling_the_log_edges_are_counted_not_guessed(self):
        _, _, unpaired, unclosed = parse(self.fixture_lines())
        self.assertEqual(1, unpaired)   # PID 1999 commits, its BEGIN predates the log
        self.assertEqual(1, unclosed)   # PID 2001 begins, its COMMIT postdates the log

    def test_non_statement_lines_are_ignored_without_crashing(self):
        # checkpoint, STATEMENT:, a bare continuation line, and an empty application_name.
        commits, transactions, _, _ = parse(self.fixture_lines())
        self.assertEqual(4, len(commits))
        self.assertEqual(3, len(transactions))

    def test_percentiles_are_none_below_the_sample_floor(self):
        self.assertIsNone(pct([1.0, 2.0, 3.0], 95))

    def test_percentiles_match_the_baseline_formula_above_the_floor(self):
        # Fixed expected values, not a re-derivation: asserting round(quantiles(...)[94], 1)
        # would compare this function against its own body and pass for any formula. 201
        # evenly spaced points under the exclusive method give p50 = 101.0 and p95 = 191.9.
        # If baseline_load.py's pct ever stops producing these, the two have diverged and
        # the numbers are no longer comparable with report section 5.
        values = [float(n) for n in range(1, 202)]
        self.assertEqual(101.0, pct(values, 50))
        self.assertEqual(191.9, pct(values, 95))

    def test_report_is_json_serialisable_on_an_empty_log(self):
        payload = report([], [], 0, 0)
        self.assertIsNone(payload["commit_max_ms"])
        self.assertEqual(0, payload["commit_samples"])
        json.dumps(payload)


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--log", help="path to a PostgreSQL server log, OUTSIDE this repo")
    parser.add_argument("--self-test", action="store_true", help="run the built-in tests")
    args = parser.parse_args()

    if args.self_test:
        suite = unittest.TestLoader().loadTestsFromTestCase(SelfTest)
        result = unittest.TextTestRunner(verbosity=2).run(suite)
        sys.exit(0 if result.wasSuccessful() else 1)

    if not args.log:
        parser.error("--log is required unless --self-test is given")

    with open(args.log, encoding="utf-8", errors="replace") as handle:
        commits, transactions, unpaired, unclosed = parse(handle)

    print(json.dumps(report(commits, transactions, unpaired, unclosed), indent=2))


if __name__ == "__main__":
    main()
