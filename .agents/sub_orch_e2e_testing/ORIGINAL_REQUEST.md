# Original User Request

## Initial Request — 2026-06-21T19:23:24Z

You are the E2E Testing Track Orchestrator. Your working directory is d:\v1.2605.2(new-test)\.agents\sub_orch_e2e_testing.
Your mission is to design and implement a comprehensive opaque-box test suite for the new AutoJMS print logic, based on the requirements in d:\v1.2605.2(new-test)\ORIGINAL_REQUEST.md.

Specifically:
1. Initialize your BRIEFING.md and progress.md in your working directory. Start a heartbeat cron.
2. Read d:\v1.2605.2(new-test)\PROJECT.md.
3. Design E2E/Integration test cases covering the 4 tiers described in the Project Pattern:
   - Tier 1: Feature Coverage (>=5 per feature)
     - Case 1: First Print (apiRequestCountForJob = 1, cacheMiss = true, fileFromApiResponse = true, spoolerSubmit = success, TabPrint selection cleared).
     - Verify other basic printing flows.
   - Tier 2: Boundary & Corner Cases (>=5 per feature)
     - Case 2: Reprint within 60s (apiRequestCountForJob = 1, cacheHit = true, fileFromCache = true (no second download), spoolerSubmit = success, selection cleared, JMS print count increments by exactly 1).
     - Case 4: API fail (do not print from cache, selection remains, show light warning).
     - Case 5: Printer submit fail (log PRINT_SPOOLER_FAILED, do not clear selection, no auto-retry causing duplicate print).
   - Tier 3: Cross-Feature Combinations
     - Case 3: Double-click/Rapid spam (Only 1 PrintJob active, no double API request, no duplicate print, log DUPLICATE_PRINT_REQUEST_IGNORED).
   - Tier 4: Real-World Application Scenarios
     - Case 6 & 7: Selection Clearing (After successful print, TabPrint and TabDash clear all selected rows, cells, and currentCell. No custom highlight remains).
4. Create the test project in the `tests/AutoJMS.Tests` directory. Modify `AutoJMS.slnx` at the project root to include it.
5. Create mock implementations or structures if necessary to allow testing the `PrintJobCoordinator` and `PrintService` without interacting with a physical printer or real JMS API. (e.g. mock JmsApiClient or use standard test mock frameworks/interfaces).
6. Verify that the test project builds successfully (using `dotnet build .\AutoJMS.slnx -c Release`).
7. Publish `d:\v1.2605.2(new-test)\TEST_READY.md` containing:
   - Test runner command (e.g. `powershell -ExecutionPolicy Bypass -File .\eng\harness\test.ps1`).
   - Detailed coverage summary of the test cases.
   - Feature checklist.
8. Follow all rules in AGENTS.md, including acquiring the lock in .agent-lock.md before making any edits (e.g. when modifying AutoJMS.slnx or creating test files), and releasing it after build/verification pass.
9. Deliver your handoff.md in your working directory and notify the parent orchestrator via send_message when complete.
