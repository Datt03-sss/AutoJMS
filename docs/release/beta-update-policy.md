# Beta Update Policy

## Purpose

Beta builds are test releases for the next stable target. They must be understandable from the version label alone.

AutoJMS uses three separate values:

| Field | Example | Purpose |
|-------|---------|---------|
| `VelopackVersion` | `1.26.6-beta.1` | Technical SemVer used by Velopack. |
| `DisplayVersion` | `1.26.6 beta 1` | User-facing text in About/release notes. |
| `InternalBuild` | `1.26.6.1` | Four-part assembly/file build metadata. |

## Beta Sequence

For stable target `1.26.6`:

```txt
1.26.6-beta.1
1.26.6-beta.2
1.26.6-beta.3
1.26.6
```

After final stable `1.26.6` is published, the next beta line is:

```txt
1.26.7-beta.1
```

Do not continue `1.26.6-beta.4` after `1.26.6` stable is live.

## Channel Behavior

| Installed Version | User Chooses | Expected Result |
|-------------------|--------------|-----------------|
| `1.26.6` | Stable | No beta offered. |
| `1.26.6` | Beta | Offer `1.26.7-beta.1` only if beta channel has moved to next patch. |
| `1.26.6-beta.1` | Beta | Offer `1.26.6-beta.2` when available. |
| `1.26.6-beta.3` | Stable | Offer `1.26.6` final; SemVer treats final as newer than prerelease of same patch. |
| `1.26.7-beta.1` | Stable `1.26.6` | Ask for downgrade confirmation before switching. |

## Release Rules

- Beta GitHub Releases must be marked `prerelease`.
- Beta `version-latest.json` entries must set `prerelease: true` and `velopackChannel: "beta"`.
- Stable `version-latest.json` entries must set `prerelease: false` and `velopackChannel: "stable"`.
- Stable users must not receive beta updates unless they explicitly choose Beta in the About update dialog.
- Major updates remain manual only; no background major update.

## Forbidden Patterns

Do not publish beta as:

```txt
VelopackVersion = 1.26.7
DisplayVersion = 1.26.6.1
```

This hides the beta line from the user and makes rollback/debug unclear.

Use:

```txt
VelopackVersion = 1.26.6-beta.1
DisplayVersion = 1.26.6 beta 1
InternalBuild = 1.26.6.1
```
