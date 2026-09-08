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
Keep the log file OUTSIDE this repository's working tree, and do not rely on the secret
gate to catch one left inside it -- NO pass of the gate reads it. `check-secrets.ps1`
part 2 skips it because `.log` is not in `$sourceExtensions`, and part 5 skips it because
it enumerates with `git ls-files --others --exclude-standard`, which drops ignored paths;
the script's own header states that exclusion is deliberate. So `*.log` being gitignored
keeps a capture out of `git add .`, and that is the whole of the protection: a capture
sitting in the tree is scanned by nothing and will report a clean gate.
"""

import argparse
import ast
import io
import json
import os
import pathlib
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
    # Copied verbatim from the `pct` closure nested inside `main()` in baseline_load.py,
    # where it cannot be imported. It must stay identical: a different percentile method
    # would make these numbers quietly incomparable with the published baselines in report
    # section 5. If one changes, change both.
    return round(statistics.quantiles(values, n=100)[q - 1], 1) if len(values) > 100 else None


def parse(lines):
    """Return (commit_durations_ms, transaction_spans_ms, unpaired_commits, unclosed).

    unclosed_transactions counts distinct PIDs that still hold an open BEGIN when the log
    ends. Multiple unmatched BEGINs on the same PID contribute only one entry — a second
    BEGIN on a PID that already has one open silently overwrites the earlier timestamp.

    ROLLBACK on a PID with no open BEGIN is silently dropped and does NOT increment any
    counter. This is a deliberate asymmetry with COMMIT: a COMMIT on a PID with no open
    BEGIN DOES increment unpaired_commits. Adding a counter for orphaned ROLLBACKs would
    change the frozen 13-key output contract.
    """
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
        try:
            stamp = datetime.strptime(match.group("ts"), TS_FORMAT)
        except ValueError:
            continue  # skip lines whose timestamp passes the regex but has impossible values

        if sql.startswith("BEGIN"):
            # A second BEGIN on a PID that already has one open silently overwrites the
            # earlier timestamp. The abandoned BEGIN is NOT individually counted anywhere;
            # multiple unmatched BEGINs on the same PID still contribute only one entry
            # to unclosed_transactions at the end.
            open_txn[pid] = stamp
        elif sql.startswith("COMMIT"):
            commits.append(float(match.group("ms")))
            began = open_txn.pop(pid, None)
            if began is None:
                unpaired_commits += 1
            else:
                transactions.append((stamp - began).total_seconds() * 1000.0)
        elif sql.startswith("ROLLBACK"):
            open_txn.pop(pid, None)  # close the transaction without recording a span

    return commits, transactions, unpaired_commits, len(open_txn)


_NOTES = (
    "commit_* is the duration PostgreSQL reports for the COMMIT statement itself. "
    "transaction_* is the interval between a PID's BEGIN log line and its COMMIT log "
    "line; both timestamps mark statement COMPLETION, so the span excludes BEGIN's "
    "own duration and is a slight underestimate. Percentiles are None below 101 "
    "samples, matching baseline_load.py. unpaired_commits counts COMMITs whose "
    "matching BEGIN is not in this log window; unclosed_transactions counts distinct "
    "PIDs still holding an open BEGIN at log end (multiple unmatched BEGINs on one "
    "PID count as one unclosed entry)."
)


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
        "notes": _NOTES,
    }


class _LineCounter:
    """Wrap an iterable of log lines, tallying total and LINE_RE-matching lines as they stream.

    main() feeds this into parse() instead of materialising the whole file, keeping
    memory O(1). After parse() returns, main() reads .total and .matched to decide
    whether to emit the no-match warning.
    """

    def __init__(self, iterable):
        self._it = iterable
        self.total = 0
        self.matched = 0

    def __iter__(self):
        for line in self._it:
            self.total += 1
            if LINE_RE.match(line):
                self.matched += 1
            yield line


def _check_no_match_warning(total_lines, matched_lines):
    """Emit a warning to stderr when lines were present but none matched LINE_RE.

    Stdout is not touched so the caller's JSON remains parseable.
    """
    if total_lines > 0 and matched_lines == 0:
        print(
            "WARNING: the log contained lines but none matched the expected prefix "
            "format '%m [%p] %a '. Check your postgresql.conf log_line_prefix and "
            "log_min_duration_statement settings.",
            file=sys.stderr,
        )


class SelfTest(unittest.TestCase):
    FIXTURE = "fixtures/pg_log_sample.log"

    def fixture_lines(self):
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

    def test_empty_app_name_lines_are_parsed_for_begin_and_commit(self):
        # When %a is empty the prefix collapses to two consecutive spaces between ] and LOG:.
        # Both a BEGIN/COMMIT pair (PID 8001) and a lone COMMIT (PID 8002, unpaired) on
        # empty-%a lines must be recognised. If the regex is tightened to require a
        # non-empty application_name token, all three lines miss: commit_samples drops to 0,
        # transactions is empty, and unpaired drops to 0 -- all wrong.
        lines = [
            "2026-09-07 10:00:00.100 UTC [8001]  LOG:  duration: 0.030 ms  statement: BEGIN\n",
            "2026-09-07 10:00:00.200 UTC [8001]  LOG:  duration: 1.500 ms  statement: COMMIT\n",
            "2026-09-07 10:00:00.300 UTC [8002]  LOG:  duration: 2.000 ms  statement: COMMIT\n",
        ]
        commits, transactions, unpaired, unclosed = parse(lines)
        self.assertEqual(2, len(commits))          # two COMMITs, both on empty-%a lines
        self.assertEqual(1, len(transactions))     # one paired BEGIN/COMMIT span
        self.assertAlmostEqual(100.0, transactions[0], places=1)  # 100 ms span
        self.assertEqual(1, unpaired)              # PID 8002's COMMIT has no BEGIN
        self.assertEqual(0, unclosed)

    def test_report_full_output_matches_fixture_expectations(self):
        # Exercises every wiring path in report() where the fixture data is sufficient:
        # commit source vs transaction source, max() vs min(), presence and content of
        # the notes key, and counter wiring where values differ. The fixture's 4 and 3
        # samples are below the percentile floor so all percentiles are null; its counters
        # both equal 1 so a counter swap is symmetric — those two mis-wirings require a
        # separate above-floor test.
        result = report(*parse(self.fixture_lines()))
        self.assertEqual({
            "commit_samples": 4,
            "commit_p50_ms": None,
            "commit_p95_ms": None,
            "commit_p99_ms": None,
            "commit_max_ms": 5.5,
            "transaction_samples": 3,
            "transaction_p50_ms": None,
            "transaction_p95_ms": None,
            "transaction_p99_ms": None,
            "transaction_max_ms": 200.0,
            "unpaired_commits": 1,
            "unclosed_transactions": 1,
            "notes": _NOTES,
        }, result)

    def test_report_wiring_above_floor_catches_quantile_and_counter_swaps(self):
        # Kills two mutants that survive test_report_full_output_matches_fixture_expectations:
        # (a) commit_p95_ms wired to the p99 quantile — indistinguishable when all
        #     percentiles are null, but 191.9 != 200.0 here; and
        # (b) unpaired_commits / unclosed_transactions swapped — indistinguishable when
        #     both are 1, but 2 != 7 here.
        # Values are pre-computed via statistics.quantiles: for 201 evenly-spaced floats
        # p95=191.9 and p99=200.0; for 151 evenly-spaced floats p95=144.4 and p99=150.5.
        commits = [float(n) for n in range(1, 202)]       # 201 samples
        transactions = [float(n) for n in range(1, 152)]  # 151 samples
        result = report(commits, transactions, 2, 7)
        self.assertEqual({
            "commit_samples": 201,
            "commit_p50_ms": 101.0,
            "commit_p95_ms": 191.9,
            "commit_p99_ms": 200.0,
            "commit_max_ms": 201.0,
            "transaction_samples": 151,
            "transaction_p50_ms": 76.0,
            "transaction_p95_ms": 144.4,
            "transaction_p99_ms": 150.5,
            "transaction_max_ms": 151.0,
            "unpaired_commits": 2,
            "unclosed_transactions": 7,
            "notes": _NOTES,
        }, result)

    def test_rollback_closes_transaction_without_recording_span(self):
        # BEGIN then ROLLBACK must yield unclosed=0, not unclosed=1. The old behaviour
        # conflated a rolled-back transaction with a capture-window clip.
        lines = [
            "2026-09-07 11:00:00.100 UTC [9001] TestApp LOG:  duration: 0.025 ms  statement: BEGIN\n",
            "2026-09-07 11:00:00.200 UTC [9001] TestApp LOG:  duration: 0.850 ms  statement: ROLLBACK\n",
            # Paired transaction on a different PID to confirm it is unaffected
            "2026-09-07 11:00:00.300 UTC [9002] TestApp LOG:  duration: 0.020 ms  statement: BEGIN\n",
            "2026-09-07 11:00:00.400 UTC [9002] TestApp LOG:  duration: 1.000 ms  statement: COMMIT\n",
        ]
        commits, transactions, unpaired, unclosed = parse(lines)
        self.assertEqual([1.000], commits)          # ROLLBACK is not a COMMIT
        self.assertEqual(1, len(transactions))      # only the 9002 BEGIN/COMMIT pair
        self.assertAlmostEqual(100.0, transactions[0], places=1)
        self.assertEqual(0, unpaired)
        self.assertEqual(0, unclosed)               # ROLLBACK closed 9001's open transaction

    def test_rollback_on_pid_with_no_open_transaction_is_silently_dropped(self):
        # ROLLBACK on a PID that has no open BEGIN is silently ignored and does NOT
        # increment any counter. This is deliberate asymmetry with COMMIT: a COMMIT on a
        # PID with no open BEGIN DOES increment unpaired_commits. The asymmetry is frozen —
        # there is no JSON key for orphaned ROLLBACKs and the 13-key contract is frozen.
        lines = [
            "2026-09-07 12:00:00.100 UTC [9010] App LOG:  duration: 0.050 ms  statement: ROLLBACK\n",
            "2026-09-07 12:00:00.200 UTC [9011] App LOG:  duration: 0.300 ms  statement: COMMIT\n",
        ]
        commits, transactions, unpaired, unclosed = parse(lines)
        self.assertEqual([0.3], commits)   # COMMIT's duration; ROLLBACK contributes nothing
        self.assertEqual([], transactions)
        self.assertEqual(1, unpaired)      # COMMIT on no-BEGIN: increments unpaired (asymmetric)
        self.assertEqual(0, unclosed)      # ROLLBACK on no-BEGIN: silently dropped, not counted

    def test_percentiles_are_none_below_the_sample_floor(self):
        self.assertIsNone(pct([1.0, 2.0, 3.0], 95))

    def test_percentiles_match_the_baseline_formula_above_the_floor(self):
        # Fixed expected values, not a re-derivation: asserting round(quantiles(...)[94], 1)
        # would compare this function against its own body and pass for any formula. 201
        # evenly spaced points under the exclusive method give p50 = 101.0 and p95 = 191.9.
        values = [float(n) for n in range(1, 202)]
        self.assertEqual(101.0, pct(values, 50))
        self.assertEqual(191.9, pct(values, 95))
        # Bidirectional drift check: verify this copy's pct body is identical to
        # baseline_load.py's so the two stay in sync. Uses ast.unparse so whitespace
        # differences don't matter. Three conditions can prevent this check from running:
        # (1) baseline_load.py is absent, (2) Python < 3.9 (no ast.unparse) — both are
        # legitimate skips; (3) the pct anchor moved — that is a defect, caught below.
        baseline_path = pathlib.Path(__file__).parent / "baseline_load.py"
        if not baseline_path.exists() or not hasattr(ast, "unparse"):
            return  # legitimate skips: file absent or Python < 3.9

        with open(baseline_path, encoding="utf-8") as fh:
            baseline_tree = ast.parse(fh.read())
        with open(__file__, encoding="utf-8") as fh:
            this_tree = ast.parse(fh.read())

        def _find_nested(tree, outer, inner):
            for node in ast.walk(tree):
                if isinstance(node, ast.FunctionDef) and node.name == outer:
                    for child in ast.walk(node):
                        if isinstance(child, ast.FunctionDef) and child.name == inner:
                            return child
            return None

        def _find_top(tree, name):
            for node in tree.body:
                if isinstance(node, ast.FunctionDef) and node.name == name:
                    return node
            return None

        baseline_pct = _find_nested(baseline_tree, "main", "pct")
        this_pct = _find_top(this_tree, "pct")
        if baseline_pct is None or this_pct is None:
            self.fail(
                "pct anchor not found — baseline_load.py exists and Python supports "
                "ast.unparse, so if pct moved out of main() there or out of module scope "
                "here, update the _find_nested / _find_top calls above to match"
            )
        self.assertEqual(
            ast.unparse(baseline_pct.body),
            ast.unparse(this_pct.body),
            "pct body diverged from baseline_load.py — update both copies together",
        )

    def test_no_match_emits_warning_to_stderr(self):
        # A log with unrecognised lines emits a clear WARNING to stderr naming both
        # relevant postgresql.conf settings; stdout stays clean.
        old_stderr = sys.stderr
        sys.stderr = io.StringIO()
        try:
            _check_no_match_warning(2, 0)
            output = sys.stderr.getvalue()
        finally:
            sys.stderr = old_stderr
        self.assertIn("WARNING", output)
        self.assertIn("log_line_prefix", output)
        self.assertIn("log_min_duration_statement", output)

    def test_no_match_warning_silent_on_empty_input(self):
        old_stderr = sys.stderr
        sys.stderr = io.StringIO()
        try:
            _check_no_match_warning(0, 0)
            output = sys.stderr.getvalue()
        finally:
            sys.stderr = old_stderr
        self.assertEqual("", output)

    def test_streaming_warning_fires_on_prefix_mismatch(self):
        # _LineCounter tallies lines as they stream through parse() without buffering.
        # When total > 0 and matched == 0, main()'s inline check emits the warning.
        # Verify the counts are correct for a non-matching log.
        lines = ["not a postgres log line\n", "also not a log line\n"]
        counter = _LineCounter(iter(lines))
        parse(counter)
        self.assertEqual(2, counter.total)
        self.assertEqual(0, counter.matched)

    def test_streaming_warning_silent_on_matching_log(self):
        # A log with at least one LINE_RE match must not trigger the warning.
        lines = [
            "2026-09-07 09:15:02.100 UTC [5001] App LOG:  duration: 0.812 ms  statement: COMMIT\n",
        ]
        counter = _LineCounter(iter(lines))
        parse(counter)
        self.assertGreater(counter.matched, 0)

    def test_impossible_timestamp_is_skipped(self):
        # A line whose timestamp passes LINE_RE (right digit counts) but has an impossible
        # calendar value (month 13) is silently skipped, not raised. Subsequent valid lines
        # are still processed, confirming the except clause is inside the per-line loop.
        lines = [
            "2026-13-07 09:15:02.100 UTC [1001] App LOG:  duration: 0.812 ms  statement: COMMIT\n",
            "2026-09-07 09:15:02.200 UTC [1001] App LOG:  duration: 1.500 ms  statement: COMMIT\n",
        ]
        commits, _, _, _ = parse(lines)
        self.assertEqual([1.5], commits)  # only the valid-timestamp line contributes

    def test_missing_log_file_exits_with_code_1(self):
        old_argv = sys.argv
        sys.argv = ["pg_log_metrics.py", "--log", "nonexistent_file_xyz_abc_123.log"]
        try:
            with self.assertRaises(SystemExit) as ctx:
                main()
            self.assertEqual(1, ctx.exception.code)
        finally:
            sys.argv = old_argv

    def test_directory_input_exits_with_code_1(self):
        # Passing a directory path to --log must produce a clean error and exit 1,
        # not a bare PermissionError or IsADirectoryError traceback.
        dir_path = os.path.dirname(os.path.abspath(__file__))
        old_argv = sys.argv
        sys.argv = ["pg_log_metrics.py", "--log", dir_path]
        try:
            with self.assertRaises(SystemExit) as ctx:
                main()
            self.assertEqual(1, ctx.exception.code)
        finally:
            sys.argv = old_argv

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

    if os.path.isdir(args.log):
        print(f"error: --log must be a file, not a directory: {args.log}", file=sys.stderr)
        sys.exit(1)

    try:
        with open(args.log, encoding="utf-8", errors="replace") as handle:
            counter = _LineCounter(handle)
            commits, transactions, unpaired, unclosed = parse(counter)
    except FileNotFoundError:
        print(f"error: log file not found: {args.log}", file=sys.stderr)
        sys.exit(1)

    _check_no_match_warning(counter.total, counter.matched)
    print(json.dumps(report(commits, transactions, unpaired, unclosed), indent=2))


if __name__ == "__main__":
    main()
