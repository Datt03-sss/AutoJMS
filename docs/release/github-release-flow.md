# GitHub Release Flow

## Repository

```
Datt03-sss/AutoJMS-Update
```

## Release Assets

| Asset | Purpose |
|-------|---------|
| RELEASES | Velopack index |
| AutoJMS-{VelopackVersion}-full.nupkg | Velopack package |
| AutoJMS-win-Setup.exe | Manual/first install fallback |

## Tag Format

```
Stable: v{VelopackVersion}-Release
Beta:   v{VelopackVersion}
```

Examples:
- v1.26.6-Release (stable)
- v1.26.6-beta.1 (beta, prerelease=true)

## Build and Upload

```powershell
# Build
.\build-release.ps1 -Version "1.26.6" -Channel stable
.\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta

# Upload
.\build-release.ps1 -Version "1.26.6" -DisplayVersion "1.26.6.1" -Channel stable -Upload
.\build-release.ps1 -Version "1.26.6-beta.1" -DisplayVersion "1.26.6.2" -Channel beta -Upload
```

## Manual Upload

```powershell
# Authenticate
gh auth login

# Create release
gh release create v1.26.6-Release `
    --repo Datt03-sss/AutoJMS-Update `
    --title "v1.26.6"

# Upload assets
gh release upload v1.26.6-Release `
    release/output/stable/RELEASES `
    release/output/stable/AutoJMS-1.26.6-full.nupkg `
    release/output/stable/AutoJMS-win-Setup.exe `
    --repo Datt03-sss/AutoJMS-Update

# Beta only
gh release create v1.26.6-beta.1 `
    --repo Datt03-sss/AutoJMS-Update `
    --title "v1.26.6 beta 1" `
    --notes "Beta test release." `
    --prerelease
```

## update.xml

After assets are uploaded, update:

```text
https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml
```

`update.xml` is read by the About dialog for:

- `velopackVersion`
- `displayVersion`
- `internalBuild`
- `releaseTag`
- `setupUrl`
- `releaseNotes`

It is not the Velopack feed. App updates use `GithubSource` against the GitHub Releases API.

## Public Repository

Repository is public - no token needed for download.
