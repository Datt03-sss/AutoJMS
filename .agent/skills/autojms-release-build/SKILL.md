---
name: autojms-release-build
description: Build and publish an AutoJMS release (stable and/or beta) to GitHub Releases via release/build-release.ps1. Use whenever the owner asks to "build release", bump to a new version, publish v1.26.x, or upload stable/beta installers.
metadata:
  type: project-procedure
  owner-approval-required: true
  last-verified: 2026-09-13 (v1.26.12)
---

# AutoJMS Release Build

Runbook for cutting a release with `release/build-release.ps1`. Companion reference:
[github-velopack-release-skill.md](../github-velopack-release-skill.md) (Velopack concepts, version
policy, update flow diagram). This file is the **operational** half: exact commands, the traps that
make the script fail, and how to verify.

## Gate: owner approval

`CLAUDE.md` marks "Build/upload production release" and "Bump version number" as **❌ unless owner
requests**. Do not run this skill speculatively. `release/build-release.ps1` is also a Protected
File: Claude Code may edit it under its standing grant (CLAUDE.md § Protected Files & Areas), any
other agent needs a specific owner request. Running it for a release stays owner-request only.

## Non-negotiable facts (each one has bitten a previous run)

| Fact | Why it matters |
|---|---|
| **Use `pwsh`, not `powershell`** | The script uses the `??` null-coalescing operator (PS7+). Windows PowerShell 5.1 dies with a parse error. `pwsh.exe` lives at `C:\Users\DAT\.dotnet\tools\pwsh.exe`. |
| **`DATAHUB_API_BASE_URL` must be set even without `-Upload`** | Step 3c calls `Get-DataHubPublicObjectUrl`, which **throws** when both `DATAHUB_MANIFEST_BASE_URL` and `DATAHUB_API_BASE_URL` are empty. Value: `https://dev.jmsauto.online`. It is not stored in User/Machine env — set it per-invocation. |
| **Always pass `-SkipDataHubManifestUpload`** | The admin route `PUT {base}/api/v1/admin/manifests/{path}` does not exist yet (documented as an open gap in `backend/BACKEND_OPERATIONS.md`; `GET /manifest/version-latest.json` returns 404 live). Without the flag, `-Upload` fails at the last step *after* the GitHub assets are already published. |
| **The release goes live through `update.xml`, not DataHub** | `VelopackUpdateService` reads `AppConfig.Current.UpdateXmlUrl` (raw.githubusercontent…/AutoJMS-Update/main/update.xml) **first**; `version-latest.json` is only a fallback. `-Upload` pushes update.xml to the GitHub repo, so skipping DataHub does not break updating. |
| **Do not bump `src/AutoJMS/AutoJMS.csproj`** | It has read `1.26.9` since v1.26.9 and that is correct. The script injects `-p:Version` / `-p:AssemblyVersion` / `-p:FileVersion` / `-p:InformationalVersion` at publish time. Editing the csproj adds a pointless diff and risks disagreeing with the packed version. |
| **Binaries live in a different repo** | Source = `Datt03-sss/AutoJMS`. Release assets = `Datt03-sss/AutoJMS-Update`. |
| **Build beta first, then stable** | GitHub marks the newest non-prerelease as "Latest". Stable last ⇒ stable is Latest. |

## Version and tag shapes

| Channel | `-Version` | DisplayVersion | Tag | Prerelease |
|---|---|---|---|---|
| stable | `1.26.12` | `1.26.12` | `v1.26.12-Release` | false |
| beta | `1.26.12-beta.1` | `1.26.12 beta 1` | `v1.26.12-beta.1-Release` | true |

Never pass a four-part version (`1.26.12.1`) — the script rejects it. Never rename a beta into the
next patch number.

## Procedure

### 1. Preflight

```bash
git switch main && git pull --ff-only origin main && git status   # clean tree required
gh auth status                                                    # must say "Logged in"
where.exe vpk pwsh                                                # both must resolve
dotnet build-server shutdown                                      # 7.8 GB box; reclaim RAM first
```

Close AutoJMS if it is running (the script's `Stop-AutoJMSProcesses` handles it, but a running app
is the usual cause of "Access denied" on `Microsoft.VisualBasic.Forms.resources.dll`).

Check the previously published version so you know what you are superseding:

```bash
gh release list --repo Datt03-sss/AutoJMS-Update --limit 10
```

### 2. Write release notes

One file per artifact, UTF-8 **without BOM**, Vietnamese, user-facing wording (feature names as the
user sees them: "IN ĐƠN", "ĐKCH", "Mã tuyến" — not commit subjects):

```
release/notes/1.26.12.md
release/notes/1.26.12-beta.1.md      # same body + trailing "Bản beta, có thể chưa ổn định."
```

Derive the content from `git log --pretty="%h %cd %s" --date=format:"%Y-%m-%d %H:%M"`, taking only
commits **after the previous release build's timestamp** — compare against the mtime of
`release/output/<channel>/*.nupkg`, because the tag date and the last-included commit rarely match.

### 3. Build + upload beta

```bash
DATAHUB_API_BASE_URL="https://dev.jmsauto.online" pwsh -NoProfile -ExecutionPolicy Bypass \
  -File "./release/build-release.ps1" \
  -Version "1.26.12-beta.1" -Channel beta \
  -ReleaseNotesFile "./release/notes/1.26.12-beta.1.md" \
  -Upload -SkipDataHubManifestUpload
```

### 4. Build + upload stable

```bash
DATAHUB_API_BASE_URL="https://dev.jmsauto.online" pwsh -NoProfile -ExecutionPolicy Bypass \
  -File "./release/build-release.ps1" \
  -Version "1.26.12" -Channel stable \
  -ReleaseNotesFile "./release/notes/1.26.12.md" \
  -Upload -SkipDataHubManifestUpload
```

Each channel needs its own full publish — **do not** reuse `-SkipPublish` across channels, because
the version is baked into the published binaries.

Expect roughly 5–8 minutes per channel on the owner's machine (dotnet publish → `vpk pack` → Inno
installer → `gh release create` + asset upload → update.xml commit). Run it backgrounded and poll
the log rather than blocking on a single long call.

### 5. Verify

```bash
gh release view v1.26.12-Release --repo Datt03-sss/AutoJMS-Update \
  --json tagName,isPrerelease,isDraft -q '.tagName+"  prerelease="+(.isPrerelease|tostring)'
gh release view v1.26.12-Release --repo Datt03-sss/AutoJMS-Update --json assets -q '.assets[].name'
# update.xml — read the blob, NOT the raw CDN (see note below)
gh api repos/Datt03-sss/AutoJMS-Update/contents/update.xml -q '.content' | base64 -d \
  | grep -E "channel name|velopackVersion|releaseTag"
```

`gh release view` has no `isLatest` field — asking for it errors out. Check "Latest" with
`gh release list` instead.

**raw.githubusercontent lags ~5–10 minutes behind the commit.** Right after a build it will still
serve the *previous* version, which looks exactly like a failed upload. Confirm through the
contents API first; only treat a stale raw response as a real failure if it is still stale ten
minutes later.

Each release must carry exactly these 5 assets:

```text
AutoJMS-{ver}-full.nupkg          ~85 MB   Velopack update payload
AutoJMS-win-Setup.exe             ~90 MB   Velopack setup
AutoJMS-Installer-{ver}.exe      ~122 MB   Inno first-install wizard (bundles the runtimes)
releases.{channel}.json             ~250 B index Velopack 1.x actually reads
RELEASES                             ~80 B legacy Squirrel, unused by the client
```

`AutoJMS-{channel}-Portable.zip` is produced into `release/output/<channel>/` but is deliberately
**not** uploaded — hand it over separately if the owner asks. A missing `releases.{channel}.json`
is the classic "no update found".

update.xml must show the new `<velopackVersion>` **for the channel you just built**; the other
channel keeps its old values, which is correct.

### 6. Commit the notes

The build writes `release/output/**` (gitignored) and pushes update.xml to the *other* repo. The
only thing left in the source repo is the notes:

```bash
git add release/notes/ && git commit -m "chore(release): release notes v1.26.12" && git push origin main
```

## Known gaps to report, not to fix

- **DataHub manifests are stale.** `version-latest.json` / `hash-manifest.json` are generated into
  `release/output/<channel>/` but never published, because the admin route is missing. Report this
  in the final summary; do not work around it by pointing the client at a third-party bucket.
- **`AutoJMS.csproj` version drift** is intentional (see table above).

## Failure table

| Symptom | Cause | Fix |
|---|---|---|
| `Unexpected token '??'` | Ran under Windows PowerShell 5.1 | Use `pwsh` |
| `DATAHUB_MANIFEST_BASE_URL or DATAHUB_API_BASE_URL must point to the VPS` | env var unset at step 3c | Prefix the invocation with `DATAHUB_API_BASE_URL=https://dev.jmsauto.online` |
| 404 on `PUT …/admin/manifests/…` at step 4 | Missing `-SkipDataHubManifestUpload` | Add the flag; GitHub assets already uploaded, so re-run is safe |
| `Access denied` on a publish DLL | AutoJMS still running / read-only leftovers | Close the app; the script's `Remove-PathSafe` retries |
| `gh release create failed` | Tag already exists | `gh release delete <tag> --repo Datt03-sss/AutoJMS-Update --cleanup-tag`, or pick the next beta number |
| dotnet publish killed / OOM | 7.8 GB box under pressure | `dotnet build-server shutdown`, close Edge/WebView2/IDEs, re-run — do not "fix" the code |
| App reports no update | update.xml not refreshed, or wrong channel selected in-app | Re-check step 5; `manualOnly=true` means the user must click "Kiểm tra cập nhật" |
