# AutoJMS — Release Manual

> Release is an **owner-only** operation. Agents must NEVER release production.

## Release Types

| Type | Tool | Who | When |
|------|------|-----|------|
| First install | Inno Setup | Owner | New machine |
| Major version | Velopack + GitHub Releases | Owner | Version bump |
| Small config | Supabase selector-update | Owner/Auto | Selector changes |

## Agent Restrictions

### ❌ NEVER
- Run `build-release.ps1 -Upload`
- Create GitHub releases or tags
- Upload `.nupkg` files anywhere
- Modify `release/build-release.ps1` without request
- Modify `installer/inno/AutoJMS.iss` without request
- Push to Supabase Storage production manifests
- Modify `version-latest.json` on Supabase

### ✅ ALLOWED
- Run `eng/harness/release-dryrun.ps1` for validation
- Build in Release mode locally
- Verify version numbers match
- Document release notes in PR

## Owner Release Process

### Major Release

1. Merge approved PR to `main`
2. Update version in `AutoJMS.csproj`:
   ```xml
   <Version>X.Y.Z</Version>
   <AssemblyVersion>X.Y.Z.0</AssemblyVersion>
   <FileVersion>X.Y.Z.0</FileVersion>
   <InformationalVersion>X.Y.Z</InformationalVersion>
   ```
3. Run release build:
   ```powershell
   .\release\build-release.ps1 -Version "X.Y.Z.0" -Channel stable
   ```
4. Verify output in `release/output/`
5. Upload:
   ```powershell
   .\release\build-release.ps1 -Version "X.Y.Z.0" -Channel stable -Upload
   ```
6. Verify GitHub Release created
7. Update `version-latest.json` on Supabase if needed
8. Test update from About tab on a test machine

### Small Config Update

1. Update selector/config files
2. Upload to Supabase Storage
3. App auto-detects on next license check

### Inno Setup (First Install)

1. Build installer from `installer/inno/AutoJMS.iss`
2. Distribute `.exe` installer manually

## Version-Latest Manifest

```json
{
  "schemaVersion": 1,
  "channels": {
    "stable": {
      "version": "1.26.6",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
      "tag": "v1.26.6-Release"
    }
  }
}
```

## Velopack Flow

```
About Tab → Check Update → Supabase manifest → GitHub Releases API
→ Download nupkg → Velopack apply → Restart
```

No browser opens. No manual download. All via Velopack SDK.
