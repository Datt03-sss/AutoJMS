# Pre-Commit Checklist

Use this checklist before any commit.

## Code Quality

- [ ] Code compiles without errors
- [ ] No compiler warnings
- [ ] No TODO comments for features
- [ ] No debug logging left in
- [ ] Code follows existing patterns

## Tier Safety

- [ ] No tier violations introduced
- [ ] BASE has no background jobs
- [ ] ULTRA features guarded
- [ ] TierRuntimePolicy used, not hardcoded checks

## Thread Safety

- [ ] WebView2 accessed on UI thread
- [ ] No cross-thread UI access
- [ ] Timers disposed properly
- [ ] CancellationTokens used

## Security

- [ ] No secrets committed
- [ ] Tokens masked in logs
- [ ] No sensitive data in code

## Testing

- [ ] Manual test on BASE tier
- [ ] Manual test on ULTRA tier
- [ ] Core features work

## Documentation

- [ ] Comments explain intent (not what code does)
- [ ] No obvious redundant comments
- [ ] Audit doc updated if structural change

## Files Changed

- [ ] Only files needed are changed
- [ ] No unintended changes
- [ ] No temporary files committed
