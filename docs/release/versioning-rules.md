# Versioning Rules

## Core Rule

AutoJMS has three version values with separate jobs:

| Field | Purpose | Used For |
|-------|---------|----------|
| `VelopackVersion` / manifest `version` | Technical update identity | Velopack package compare/update |
| `DisplayVersion` / manifest `displayVersion` | User-facing label | About tab, release notes |
| `InternalBuild` / manifest `internalBuild` | Build metadata when needed | About tab and diagnostics |

`VelopackVersion` must always be valid SemVer. Do not use four-part versions as `packVersion`.

## Canonical Formats

### Stable Release

| Field | Value |
|-------|-------|
| `VelopackVersion` | `1.26.6` |
| `DisplayVersion` | `1.26.6.1` |
| `InternalBuild` | `20260607.1` |
| `velopackChannel` | `stable` |
| GitHub tag | `v1.26.6-Release` |
| GitHub prerelease | `false` |

### Beta Release

| Field | Value |
|-------|-------|
| `VelopackVersion` | `1.26.6-beta.1` |
| `DisplayVersion` | `1.26.6.2` |
| `InternalBuild` | `20260607.beta.1` |
| `velopackChannel` | `beta` |
| GitHub tag | `v1.26.6-beta.1` |
| GitHub prerelease | `true` |

## Forbidden Conversion

Never convert `1.26.6.1` into `1.26.7`.

Four-part values are internal build metadata only. They must not be passed to:

- `vpk pack --packVersion`
- MSBuild `/p:Version`
- update.xml `channel.velopackVersion`
- GitHub release tag

## Manifest Schema

`update.xml` must contain these fields for each release channel:

```xml
<AutoJMSUpdateManifest schemaVersion="2">
  <updatedAt>2026-06-07T00:00:00+07:00</updatedAt>
  <githubRepo>https://github.com/Datt03-sss/AutoJMS-Update</githubRepo>
  <channels>
    <channel name="stable" enabled="true" prerelease="false">
      <velopackVersion>1.26.6</velopackVersion>
      <displayVersion>1.26.6.1</displayVersion>
      <internalBuild>20260607.1</internalBuild>
      <releaseTag>v1.26.6-Release</releaseTag>
      <setupUrl>https://github.com/Datt03-sss/AutoJMS-Update/releases/download/v1.26.6-Release/AutoJMS-win-Setup.exe</setupUrl>
      <mandatory>false</mandatory>
      <manualOnly>true</manualOnly>
      <releaseNotes>Stable release.</releaseNotes>
    </channel>
  </channels>
</AutoJMSUpdateManifest>
```

## Channel Rules

| Scenario | Rule |
|----------|------|
| Stable user on `1.26.6` | Do not offer `1.26.6-beta.1`. |
| Beta user on `1.26.6-beta.1` | Offer `1.26.6-beta.2`. |
| Beta user on `1.26.6-beta.3` | Offer stable final `1.26.6` only when user explicitly selects stable. This is an upgrade in SemVer precedence. |
| User moves from newer patch/beta to older stable | Ask for downgrade confirmation. |
| Next beta after stable `1.26.6` | Must be `1.26.7-beta.1`, not `1.26.6-beta.4`. |

Major updates are manual only. The user must click the About tab and choose `Stable` or `Beta`.

## Build Script Rules

`release/build-release.ps1` accepts:

```powershell
.\build-release.ps1 -Version "1.26.6" -Channel stable
.\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta
```

It must reject stable inputs like:

```txt
1.26.05
1.26.6.1
1.26.7.1
```

Interactive beta mode may offer to convert `1.26.6.1` to `1.26.6-beta.1`; it must never convert that value to `1.26.7`.

## Version Locations

| Location | Rule |
|----------|------|
| `src/AutoJMS/AutoJMS.csproj` | Default local metadata only. Release script overrides package/display/internal values. |
| `release/build-release.ps1` | Source of release version generation. |
| `update.xml` in `Datt03-sss/AutoJMS-Update` | About dialog UI manifest. |
| GitHub Releases | Stable tag `v{VelopackVersion}-Release`; beta tag `v{VelopackVersion}`. |

## Bump Rules

| Change | Stable Bump | Beta Bump |
|--------|-------------|-----------|
| Bug fix | Patch, e.g. `1.26.7` | Same target patch, e.g. `1.26.7-beta.1` |
| New feature | Minor, e.g. `1.27.0` | Same target minor, e.g. `1.27.0-beta.1` |
| Breaking change | Major, e.g. `2.0.0` | Same target major, e.g. `2.0.0-beta.1` |
