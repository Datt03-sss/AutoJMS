# Release Stabilization Plan

## Current Target

```txt
Supabase = control plane
GitHub Releases = binary hosting
Inno Setup = first install
Velopack = in-app update
```

## Rules

- Major update is manual via tab About.
- Do not open GitHub page during update.
- Do not upload `.nupkg` to Supabase.
- Stable and beta must be separate and explicit.
- Version used by Velopack must be valid SemVer.

## File lớn upload GitHub

- `RELEASES`
- `.nupkg`
- `Setup.exe`

GitHub repo:

```txt
Datt03-sss/AutoJMS-Update
```

Tag format:

```txt
v{VelopackVersion}-Release
```

## File nhỏ upload Supabase

- `version-latest.json`
- `hash-manifest.json`
- `selector-update-manifest.json`
- `tier-definitions.sec`
- runtime/selector config `.sec`

## Stabilization Tasks

1. Verify `Release/build-release.bat` and `Release/build-release.ps1` produce the expected assets.
2. Verify stable release is not prerelease.
3. Verify beta release is prerelease.
4. Verify `version-latest.json` points to the right GitHub tag.
5. Verify `hash-manifest.json` contains hash after .NET Reactor.
6. Verify About tab update uses Velopack and does not open browser.
7. Verify update keeps `C:\AutoJMS\AppData`.

## Acceptance Criteria

- Old installed app can discover update from About.
- Update downloads from GitHub Releases.
- Supabase only stores manifest/config/hash/control files.
- App restarts after update.
- User data is preserved.

