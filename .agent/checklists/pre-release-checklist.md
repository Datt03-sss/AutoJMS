# Pre-Release Checklist

Use this checklist before any release.

## Version

- [ ] Version updated in src/AutoJMS/AutoJMS.csproj
- [ ] Version follows semver
- [ ] Changelog updated

## Code

- [ ] All features complete
- [ ] All bugs fixed
- [ ] No known critical issues
- [ ] No TODO comments for features

## Security

- [ ] No secrets in code
- [ ] Tokens masked in production
- [ ] .NET Reactor applied (if configured)
- [ ] .gitignore updated if needed

## Testing

### Functional

- [ ] HOME tab works
- [ ] DKCH1 works
- [ ] DKCH2 works
- [ ] TRACKING works
- [ ] PRINT works
- [ ] ABOUT works

### Auth Flow

- [ ] Online license verify works
- [ ] Offline fallback works
- [ ] Heartbeat works
- [ ] Token capture works

### Tier

- [ ] BASE tier works
- [ ] ULTRA tier works
- [ ] Background jobs only on ULTRA
- [ ] FullStackOperation on ULTRA only

### Update

- [ ] Update check works
- [ ] Download works
- [ ] Restart works
- [ ] No browser opens

## Build

- [ ] Build succeeds
- [ ] Self-contained verified
- [ ] Output size reasonable
- [ ] .NET Reactor applied

## Installer

- [ ] Clean install works
- [ ] Reinstall works
- [ ] Uninstall works
- [ ] Shortcuts correct

## Manifest

- [ ] version-latest.json updated
- [ ] hash-manifest.json updated
- [ ] GitHub release created
- [ ] Assets uploaded

## Documentation

- [ ] Release notes written
- [ ] Changelog updated
- [ ] docs/audit updated if needed

