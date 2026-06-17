# Agent PR Review Checklist

> For the owner to use when reviewing agent-submitted PRs.

## Automated Checks

- [ ] `eng/harness/verify.ps1` passes (exit code 0)
- [ ] Build succeeded in Release mode
- [ ] No new secret scan issues
- [ ] Agent included verify.ps1 output in PR

## Code Review

### Scope
- [ ] Only intended files were changed
- [ ] No frozen files were modified (see `docs/agent/TAB_OWNERSHIP.md`)
- [ ] Changes are minimal and reviewable
- [ ] No unnecessary refactoring

### Business Logic
- [ ] No existing business logic was changed (HOME/DKCH/TRACKING/PRINT/ABOUT)
- [ ] No license/auth/hash-check flow was modified
- [ ] No update/release flow was modified

### Tier Separation
- [ ] BASE tier not affected by ULTRA-only changes
- [ ] No new background jobs on BASE
- [ ] `TierRuntimePolicy` used for tier checks (no string compare)
- [ ] FullStackOperation guards intact

### Security
- [ ] No secrets in changed files
- [ ] No full tokens logged
- [ ] No new hardcoded URLs/keys
- [ ] Token masking used where applicable

### Architecture
- [ ] No duplicate services created
- [ ] WebView2 access on UI thread only
- [ ] No UI thread blocking
- [ ] ABOUT tab remains last

### Dependencies
- [ ] No new NuGet packages (or justified if added)
- [ ] No new external service dependencies
- [ ] Backward compatible with existing data

## Post-Merge

- [ ] Verify app starts on BASE tier
- [ ] Verify app starts on ULTRA tier
- [ ] Verify auth flow works
- [ ] Verify update check works (About tab)
- [ ] Run manual smoke test on affected features
