# Task 3 Report: Recover commit_p95 and transaction_p95 from a Postgres log

## Files Created

- `backend/datahub/tests/pg_log_metrics.py` — the aggregator script with embedded `--self-test` suite
- `backend/datahub/tests/fixtures/pg_log_sample.log` — 14-line hand-written synthetic fixture

No existing files were modified.

## Verbatim --self-test Output

```
test_commit_durations_are_every_commit_and_only_commits (__main__.SelfTest.test_commit_durations_are_every_commit_and_only_commits) ... ok
test_non_statement_lines_are_ignored_without_crashing (__main__.SelfTest.test_non_statement_lines_are_ignored_without_crashing) ... ok
test_percentiles_are_none_below_the_sample_floor (__main__.SelfTest.test_percentiles_are_none_below_the_sample_floor) ... ok
test_percentiles_match_the_baseline_formula_above_the_floor (__main__.SelfTest.test_percentiles_match_the_baseline_formula_above_the_floor) ... ok
test_report_is_json_serialisable_on_an_empty_log (__main__.SelfTest.test_report_is_json_serialisable_on_an_empty_log) ... ok
test_transaction_spans_pair_begin_to_commit_per_pid (__main__.SelfTest.test_transaction_spans_pair_begin_to_commit_per_pid) ... ok
test_transactions_straddling_the_log_edges_are_counted_not_guessed (__main__.SelfTest.test_transactions_straddling_the_log_edges_are_counted_not_guessed) ... ok

----------------------------------------------------------------------
Ran 7 tests in 0.037s

OK
```

## Verbatim End-to-End Run Output

```
python backend/datahub/tests/pg_log_metrics.py --log backend/datahub/tests/fixtures/pg_log_sample.log
```

```json
{
  "commit_samples": 4,
  "commit_p50_ms": null,
  "commit_p95_ms": null,
  "commit_p99_ms": null,
  "commit_max_ms": 5.5,
  "transaction_samples": 3,
  "transaction_p50_ms": null,
  "transaction_p95_ms": null,
  "transaction_p99_ms": null,
  "transaction_max_ms": 200.0,
  "unpaired_commits": 1,
  "unclosed_transactions": 1,
  "notes": "commit_* is the duration PostgreSQL reports for the COMMIT statement itself. transaction_* is the interval between a PID's BEGIN log line and its COMMIT log line; both timestamps mark statement COMPLETION, so the span excludes BEGIN's own duration and is a slight underestimate. Percentiles are None below 101 samples, matching baseline_load.py. unpaired_commits and unclosed_transactions count transactions straddling the ends of the log; a large value means the capture window clipped the run and the percentiles cover a biased subset."
}
```

All values match the brief's Step 4 specification: `commit_samples: 4`, `transaction_samples: 3`, `commit_max_ms: 5.5`, `transaction_max_ms: 200.0`, `unpaired_commits: 1`, `unclosed_transactions: 1`, all percentile fields `null`.

## Evidence that Tests Are Load-Bearing (Not Vacuous)

The critical test for empty-`%a` handling is `test_non_statement_lines_are_ignored_without_crashing` and implicitly `test_commit_durations_are_every_commit_and_only_commits`. Line 10 of the fixture uses the empty-`%a` form (two consecutive spaces between `]` and `LOG:`):

```
2026-09-07 09:15:02.700 UTC [1841]  LOG:  duration: 0.310 ms  statement: SELECT 1
```

To confirm this line is being parsed (not silently skipped by the regex), I checked: the regex's `.*?\[(?P<pid>\d+)\]` followed by `.*?\bduration:` matches both the populated-`%a` form (`] AutoJMS.DataHub.Api LOG:`) and the empty form (`]  LOG:`). If the regex only matched when `%a` was populated, line 10 would be missed, but the parser would still see the correct 4 commits and 3 transactions (because SELECT 1 contributes to neither list). However, `test_non_statement_lines_are_ignored_without_crashing` explicitly checks `len(commits) == 4` and `len(transactions) == 3`, and if line 10 matched as a spurious COMMIT or BEGIN, those counts would be wrong.

To verify the tests would catch a broken regex, I manually checked: if the `.*?` between `]` and `duration:` were replaced with `\s+\w+\s+LOG:\s+` (requiring a non-empty `%a`), line 10 would not match, and since it's a SELECT, it contributes to neither list — but the counts remain correct. This means the test suite does NOT catch a broken empty-`%a` form through a count assertion alone. However, the test `test_non_statement_lines_are_ignored_without_crashing` documents the requirement and would catch regressions where empty-`%a` lines were incorrectly parsed as a COMMIT or BEGIN (producing wrong counts). The parsing correctness of the empty-`%a` form for non-actionable lines is verified by the end-to-end output matching exactly — if line 10 caused a crash, the whole run would fail.

The more load-bearing tests are `test_transaction_spans_pair_begin_to_commit_per_pid` (asserting `[200.0, 175.0, 112.0]`): if `open_txn` were keyed on anything other than PID, the spans would cross-pair across PIDs and produce wrong values, and `test_transactions_straddling_the_log_edges_are_counted_not_guessed` (asserting `unpaired=1, unclosed=1`): these catch off-by-one errors in the BEGIN/COMMIT tracking state machine.

## gitignore Pattern Confirmation

```
git check-ignore -v backend/datahub/tests/fixtures/pg_log_sample.log; echo "exit=$?"
```

Output:
```
.gitignore:15:*.log	backend/datahub/tests/fixtures/pg_log_sample.log
exit=0
```

The matched pattern is `*.log` (confirmed by pattern match, not line number — the line number shown is `.gitignore:15` as of this commit, but the report relies only on the pattern value `*.log`). The fixture was force-added with `git add -f` to track it despite the gitignore rule.

## Harness Result

```
OVERALL: ✅ ALL GATES PASSED
```

Build, Tests, NodeTests, Secrets, and Structure all PASS.

## Commit

```
[main acbe7d0] test(datahub): derive commit and transaction percentiles from the postgres log
 2 files changed, 199 insertions(+)
 create mode 100644 backend/datahub/tests/fixtures/pg_log_sample.log
 create mode 100644 backend/datahub/tests/pg_log_metrics.py
```

Commit hash: `acbe7d0`

Fixture tracking confirmed:
```
git ls-files backend/datahub/tests/fixtures/
backend/datahub/tests/fixtures/pg_log_sample.log
```

## Things Noticed but Deliberately Not Changed

- `baseline_load.py` was not modified. The `pct` function is duplicated verbatim as required — it is nested inside `main()` in the source and cannot be imported. The duplication is deliberate and documented with a comment in the new file.
- No `requirements.txt` was created. The script uses only the Python standard library (`argparse`, `json`, `re`, `statistics`, `sys`, `unittest`, `datetime`).
- No existing `.gitignore` entries were changed.

---

## Fix wave 1

Commit: `3d93c9d` — `fix(datahub): close 13 mutation survivors in pg_log_metrics test suite`

### Findings addressed

**Important 1 — empty-%a coverage**

Added `test_empty_app_name_lines_are_parsed_for_begin_and_commit` using three inline log lines (synthetic PIDs 8001/8002, no fixture change): a paired BEGIN/COMMIT on empty-%a lines and a lone COMMIT (unpaired). Asserts `len(commits)==2`, `len(transactions)==1`, span≈100 ms, `unpaired==1`, `unclosed==0`. Mutation transcript confirms it is the only test that turns red when the regex is tightened to require a dotted app-name token before LOG: — the other 11 tests still pass in the mutant.

Mutation applied: replaced `r".*?\bduration:\s..."` with `r"\s+\S+\s+LOG:.*?\bduration:\s..."` in a scratch copy at `C:\Temp\pg_scratch\` (deleted after run).

Transcript (scratch run, exit 1):
```
test_commit_durations_are_every_commit_and_only_commits ... ok
test_empty_app_name_lines_are_parsed_for_begin_and_commit ... FAIL
test_no_match_emits_warning_to_stderr ... ok
test_no_match_warning_silent_on_empty_input ... ok
test_non_statement_lines_are_ignored_without_crashing ... ok
test_percentiles_are_none_below_the_sample_floor ... ok
test_percentiles_match_the_baseline_formula_above_the_floor ... ok
test_report_full_output_matches_fixture_expectations ... ok
test_report_is_json_serialisable_on_an_empty_log ... ok
test_rollback_closes_transaction_without_recording_span ... ok
test_transaction_spans_pair_begin_to_commit_per_pid ... ok
test_transactions_straddling_the_log_edges_are_counted_not_guessed ... ok

FAILED (failures=1)
FAIL: test_empty_app_name_lines_are_parsed_for_begin_and_commit
AssertionError: 2 != 0
```

**Important 2 — report() tested with data**

Added `test_report_full_output_matches_fixture_expectations`: calls `report(*parse(self.fixture_lines()))` and asserts the complete 13-key dict in one `assertEqual`, including the exact `notes` string. Four mis-wiring mutants (commit_max from transactions, transaction_max from commits, min() instead of max(), deleted notes) now fail this test. [Corrected in Fix wave 2: the wrong-quantile mutant (commit_p95_ms fed the p99 quantile) and the swapped-counter mutant (unpaired_commits / unclosed_transactions exchanged) both survived — the fixture's 4 and 3 samples are below the percentile floor so all percentiles are null, and both counters equal 1 so a swap is symmetric. Fix wave 2 adds test_report_wiring_above_floor_catches_quantile_and_counter_swaps to kill them.]

**Important 3 — ROLLBACK as terminator**

Added `elif sql.startswith("ROLLBACK"): open_txn.pop(pid, None)` in `parse()`. Added `test_rollback_closes_transaction_without_recording_span`: BEGIN/ROLLBACK on PID 9001 yields `commits==[]`, `unclosed==0`. Updated `notes` text to say "unclosed_transactions counts BEGINs whose COMMIT or ROLLBACK did not appear before the log ended" (was: "count transactions straddling the ends of the log; a large value means the capture window clipped the run"). The fixture has no ROLLBACKs so all numeric values are unchanged.

Fixed inaccurate `parse()` comment (Minor 7 finding): "the earlier is counted as unclosed at the end" was false. New comment: "The abandoned BEGIN is NOT individually counted anywhere; multiple unmatched BEGINs on the same PID still contribute only one entry to unclosed_transactions at the end."

**Important 4 — no-match warning**

Extracted `_check_no_match_warning(lines)` helper that writes to stderr when lines are present but none match LINE_RE. Called from `main()` after parse. Added `test_no_match_emits_warning_to_stderr` and `test_no_match_warning_silent_on_empty_input`. Stdout untouched; exit code unchanged.

**Minor 5 — line-number citation**

Replaced `baseline_load.py:306-307` in the `pct` comment with "the `pct` closure nested inside `main()` in baseline_load.py". No other line-number citations found in the file.

**Minor 6 — bidirectional drift test**

`test_percentiles_match_the_baseline_formula_above_the_floor` now parses both files with `ast`, extracts the `pct` body from each (nested in `main()` for baseline_load.py, at module level here), and asserts `ast.unparse(baseline.body) == ast.unparse(this.body)`. Skips gracefully if `baseline_load.py` is absent or Python < 3.9 (no `ast.unparse`).

**Minor 8 — crash paths and stray import**

- `datetime.strptime` call wrapped in `try/except ValueError`; lines with impossible timestamp values (e.g. month 13) are skipped instead of aborting the run.
- `FileNotFoundError` in `main()` now produces `error: log file not found: <path>` on stderr and `sys.exit(1)` instead of a raw traceback.
- `import os` moved from inside `fixture_lines()` to module-level imports.

### Verification

**1. Self-test (12 tests, all pass):**
```
test_commit_durations_are_every_commit_and_only_commits ... ok
test_empty_app_name_lines_are_parsed_for_begin_and_commit ... ok
test_no_match_emits_warning_to_stderr ... ok
test_no_match_warning_silent_on_empty_input ... ok
test_non_statement_lines_are_ignored_without_crashing ... ok
test_percentiles_are_none_below_the_sample_floor ... ok
test_percentiles_match_the_baseline_formula_above_the_floor ... ok
test_report_full_output_matches_fixture_expectations ... ok
test_report_is_json_serialisable_on_an_empty_log ... ok
test_rollback_closes_transaction_without_recording_span ... ok
test_transaction_spans_pair_begin_to_commit_per_pid ... ok
test_transactions_straddling_the_log_edges_are_counted_not_guessed ... ok

Ran 12 tests in 0.042s
OK
```

**2. End-to-end fixture run — all pinned numeric values identical:**
```json
{
  "commit_samples": 4,
  "commit_p50_ms": null,
  "commit_p95_ms": null,
  "commit_p99_ms": null,
  "commit_max_ms": 5.5,
  "transaction_samples": 3,
  "transaction_p50_ms": null,
  "transaction_p95_ms": null,
  "transaction_p99_ms": null,
  "transaction_max_ms": 200.0,
  "unpaired_commits": 1,
  "unclosed_transactions": 1,
  "notes": "... (updated; see Important 3)"
}
```
Notes text changed intentionally (Important 3 requires it). The parenthetical pinned values — commit_samples, transaction_samples, commit_max_ms, transaction_max_ms, unpaired_commits, unclosed_transactions, all percentiles null — are byte-for-byte identical.

**3. git diff --name-only:** `backend/datahub/tests/pg_log_metrics.py` (one file only).

**4. Mutation transcript:** see Important 1 section above.

**5. Harness:** `OVERALL: ✅ ALL GATES PASSED` (Build, Tests, NodeTests, Secrets, Structure).
