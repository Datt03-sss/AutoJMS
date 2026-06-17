# Velopack Update

## Overview

Velopack handles in-app updates after first install.

## Update Types

| Type | Trigger | Mechanism |
|------|---------|-----------|
| Small Config | Auto | SmallUpdateService |
| Major Version | Manual | VelopackUpdateService |

## Update Flow

```
User clicks "Kiểm tra cập nhật"
    │
    ├─► Read update.xml raw GitHub URL for UI metadata
    │       └─► https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml
    │
    ├─► Prompt channel
    │       ├─► Stable → ExplicitChannel=stable
    │       └─► Beta   → ExplicitChannel=beta
    │
    ├─► VelopackUpdateService.CheckAndUpdateAsync()
    │       │
    │       ├─► Resolve channel metadata from update.xml, fallback version-latest.json
    │       ├─► Resolve selected channel only
    │       │
    │       ├─► provider=github?
    │       │       │
    │       │       └─► Use GithubSource
    │       │
    │       └─► Check GitHub Releases
    │
    ├─► Newer version found?
    │       │
    │       ├─► NO → Show "Bạn đang dùng phiên bản mới nhất"
    │       │
    │       └─► YES
    │               │
    │               ├─► Show confirm dialog
    │               │
    │               ├─► User confirms?
    │               │       │
    │               │       ├─► Download with progress
    │               │       │
    │               │       └─► PrepareForUpdateAsync()
    │               │               │
    │               │               ├─► Stop services
    │               │               ├─► Dispose WebView2
    │               │               └─► Close FullStackOperation
    │               │
    │               └─► ApplyUpdatesAndRestart()
    │
    └─► NO BROWSER OPENS
```

Stable channel must not fall back to beta. Beta channel must not silently fall back to stable. If the selected channel is missing in `version-latest.json`, the service logs the condition and uses the legacy feed for that same explicit channel.

## update.xml vs Velopack Source

`update.xml` is only the UI/control manifest:

- channel name and enabled flag
- VelopackVersion / DisplayVersion / InternalBuild
- release notes and releaseNotesUrl
- GitHub repo/tag/setupUrl metadata

It is not the Velopack update feed. The app reads it from the raw GitHub URL:

```text
https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml
```

Velopack update source remains one of:

- `GithubSource("https://github.com/Datt03-sss/AutoJMS-Update", null, prerelease, null)`
- `SimpleWebSource(velopackFeedUrl)` where `velopackFeedUrl` is a folder containing `RELEASES`

Do not pass a GitHub Release asset URL such as `.../AutoJMS-win-Setup.exe` as a `SimpleWebSource` feed. `SimpleWebSource` looks for `{baseUri}/RELEASES`, so its base URI must be a real feed folder.

## Velopack GithubSource

```csharp
// No browser opened - Velopack reads GitHub API directly
var source = new GithubSource(
    "https://github.com/Datt03-sss/AutoJMS-Update",
    null,  // no token (public repo)
    prerelease: ch.Prerelease,
    downloader: null
);
```

`UpdateOptions.ExplicitChannel` is always set to the user-selected channel:

```csharp
var options = new UpdateOptions
{
    ExplicitChannel = channel,
    AllowVersionDowngrade = allowDowngrade
};
```

Downgrade is disabled by default. If the selected channel points to a lower SemVer than the installed version, the user must confirm before `AllowVersionDowngrade` is enabled.

## Version Policy

| Release Type | VelopackVersion | DisplayVersion | InternalBuild |
|--------------|-----------------|----------------|---------------|
| Stable | `1.26.6` | `1.26.6` | `1.26.6.0` |
| Beta | `1.26.6-beta.1` | `1.26.6 beta 1` | `1.26.6.1` |

Do not publish `VelopackVersion=1.26.7` with `DisplayVersion=1.26.6.1`. Beta builds must use SemVer prerelease syntax so Velopack and users agree about the release line.

## PrepareForUpdateAsync

Stops all services before update:

1. Cancel _appCts
2. Stop _autoSyncTimer
3. Stop ZaloService
4. Close FullStackOperation
5. Release Supabase lease
6. Dispose WebView2
7. Wait 800ms

## Testing

1. Install old version
2. Click "Kiểm tra cập nhật"
3. Choose Stable and verify beta is not offered to stable clients
4. Choose Beta and verify beta prerelease is checked
5. Verify prompt shows only when newer selected-channel version exists
6. Confirm update
7. Verify download progress
8. Verify restart
9. Verify new version runs
