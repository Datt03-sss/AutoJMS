# GitHub Velopack Release Skill

## Overview

GitHub Releases hosts Velopack binaries. Supabase hosts small control-plane manifests.

## Binary Split

| Asset | Size | Host |
|-------|------|------|
| .nupkg | ~100MB | GitHub Releases |
| *Setup.exe | ~100MB | GitHub Releases |
| RELEASES | ~1KB | GitHub Releases |
| version-latest.json | ~1KB | Supabase Storage |

**Supabase free plan rejects files > 50MB.**

## Velopack Concepts

### Pack ID

```
AutoJMS
```

### Channel

```
stable | beta
```

### Version

```
stable: 1.26.6
beta:   1.26.6-beta.1
```

### Release Tag

```
stable: v1.26.6-Release
beta:   v1.26.6-beta.1-Release
```

## Build Flow

```powershell
# 1. Publish
dotnet publish -c Release -r win-x64 --self-contained -o publish/win-x64

# 2. Velopack pack
vpk pack `
    --packId AutoJMS `
    --packVersion 1.26.6 `
    --packDir publish/win-x64 `
    --mainExe AutoJMS.exe `
    --outputDir Release/output/stable `
    --channel stable

# 3. Upload to GitHub
gh release create v1.26.6-Release `
    --repo Datt03-sss/AutoJMS-Update `
    --title "v1.26.6"
    
gh release upload v1.26.6-Release `
    Release/output/stable/*.nupkg `
    --repo Datt03-sss/AutoJMS-Update
    
gh release upload v1.26.6-Release `
    Release/output/stable/*Setup.exe `
    --repo Datt03-sss/AutoJMS-Update

# 4. Update Supabase manifest
# (upload version-latest.json to Supabase)
```

## Velopack GithubSource

```csharp
// In VelopackUpdateService
if (ch.IsGithubProvider)
{
    // Public repo - no token needed
    var source = new GithubSource(
        "https://github.com/Datt03-sss/AutoJMS-Update",
        null,  // no token
        ch.Prerelease,  // prerelease flag
        null  // no custom downloader
    );
}
```

## Version Policy

| Release | VelopackVersion | DisplayVersion | InternalBuild |
|---------|-----------------|----------------|---------------|
| Stable | `1.26.6` | `1.26.6` | `1.26.6.0` |
| Beta | `1.26.6-beta.1` | `1.26.6 beta 1` | `1.26.6.1` |

Never convert `1.26.6.1` into `1.26.7`. Four-part values are internal build metadata only.

## Release Tag Format

| Channel | Tag | Prerelease |
|---------|-----|------------|
| stable | v1.26.6-Release | false |
| beta | v1.26.6-beta.1-Release | true |

## Update Flow

```
User clicks "Kiểm tra cập nhật"
    │
    ▼
VelopackUpdateService.CheckAndUpdateAsync()
    │
    ▼
Read version-latest.json from Supabase
    │
    ▼
provider=github → Use GithubSource
    │
    ▼
Check GitHub Releases API
    │
    ▼
Compare version
    │
    ▼
If newer:
    │
    ▼
Show confirm dialog
    │
    ▼
Download from GitHub
    │
    ▼
PrepareForUpdate
    │
    ▼
ApplyUpdatesAndRestart
```

## Important Notes

1. **GithubSource does NOT open browser** - Velopack reads GitHub API directly
2. **No token needed** - Public repo has public release assets
3. **RELEASES file required** - Velopack needs this for update detection
4. **SemVer compatible** - Version must be semantic version compatible

## Common Issues

| Issue | Cause | Solution |
|-------|--------|----------|
| No update found | version-latest.json not updated | Upload to Supabase |
| Download fails | Wrong repo/tag | Check tag format |
| 404 on assets | Assets not uploaded | Check gh upload |
| Wrong version shown | DisplayVersion does not match VelopackVersion line | Use `1.26.6 beta 1` for `1.26.6-beta.1`; do not hide beta as `1.26.7` |
