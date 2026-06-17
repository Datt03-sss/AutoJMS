# Release Overview

## Release Components

| Component | Tool | Location |
|-----------|------|----------|
| Build | dotnet publish | build-release.ps1 |
| Protect | .NET Reactor | src/AutoJMS/AutoJMS.csproj |
| Package | Velopack | build-release.ps1 |
| Host Binaries | GitHub Releases | Datt03-sss/AutoJMS-Update |
| Host Update UI Manifest | GitHub raw update.xml | Datt03-sss/AutoJMS-Update |
| First Install | Inno Setup | AutoJMS.iss |
| Update | Velopack | In-app |

## Binary Split

| Type | Size | Host |
|------|------|------|
| .nupkg | ~100MB | GitHub Releases |
| *Setup.exe | ~100MB | GitHub Releases |
| RELEASES | ~1KB | GitHub Releases |
| update.xml | ~1KB | GitHub raw |

## Version Flow

```
Stable:
  VelopackVersion = 1.26.6
  DisplayVersion  = 1.26.6.1
  InternalBuild   = 20260607.1
  GitHub tag      = v1.26.6-Release

Beta:
  VelopackVersion = 1.26.6-beta.1
  DisplayVersion  = 1.26.6.2
  InternalBuild   = 20260607.beta.1
  GitHub tag      = v1.26.6-beta.1
```

`build-release.ps1` does not convert four-part versions into a different Velopack patch. Four-part values are internal build metadata only.

See also: `docs/release/versioning-rules.md` and `docs/release/beta-update-policy.md`.

## Build Process

```
1. dotnet publish
   └─► publish/win-x64/

2. .NET Reactor (optional)
   └─► Protects AutoJMS.dll

3. vpk pack
   └─► release/output/stable/

4. Upload
   ├─► GitHub: .nupkg, Setup.exe, RELEASES
   └─► GitHub repo: update.xml
```

## Update Detection

```
App starts
    │
    └─► SmallUpdateService
            │
            └─► Fetch selector-update-manifest.json
                    │
                    └─► Auto apply if autoApply=true


User clicks "Kiểm tra cập nhật"
    │
    ├─► About dialog reads update.xml raw URL for UI metadata
    │
    ├─► User chooses Stable or Beta
    │
    └─► VelopackUpdateService with ExplicitChannel
            │
            └─► Velopack GithubSource
                    │
                    └─► Check GitHub Releases API
                            │
                            └─► Compare version
```

## Release Checklist

- [ ] Version bumped
- [ ] Build succeeds
- [ ] GitHub release created
- [ ] Assets uploaded: RELEASES, AutoJMS-{VelopackVersion}-full.nupkg, AutoJMS-win-Setup.exe
- [ ] update.xml updated
- [ ] Installer tested
- [ ] Update flow tested
