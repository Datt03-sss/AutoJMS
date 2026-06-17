# Release Pipeline Fix Prompt

Use this prompt when fixing release/build/update pipeline issues.

## Non-Negotiable Update Rules

- Supabase does not host `.nupkg`.
- GitHub Releases host:
  - `RELEASES`
  - `.nupkg`
  - `Setup.exe`
- Supabase hosts:
  - `version-latest.json`
  - `hash-manifest.json`
  - `selector-update-manifest.json`
  - `tier-definitions.sec`
- Major update is manual only via the About tab.
- Selector-update can auto update small selector/runtime config.
- Do not open a GitHub browser page during app update.

## Release Components

| Component | Purpose | Location |
|-----------|---------|----------|
| dotnet publish | Build self-contained app | build-release.ps1 |
| .NET Reactor | Protect AutoJMS.dll | src/AutoJMS/AutoJMS.csproj |
| vpk pack | Create Velopack package | build-release.ps1 |
| GitHub Release | Host binaries | build-release.ps1 |
| Supabase Storage | Host manifests | build-release.ps1 |
| Inno Setup | First install | AutoJMS.iss |

## Binary Split

| Type | Size | Host |
|------|------|------|
| .nupkg | ~100MB | GitHub Releases |
| Setup.exe | ~100MB | GitHub Releases |
| RELEASES | ~1KB | GitHub Releases |
| version-latest.json | ~1KB | Supabase Storage |

**Supabase free plan rejects files > 50MB, so .nupkg goes to GitHub.**

## Common Issues

### Issue 1: Build Fails

**Symptom**: dotnet publish fails

**Check**:
1. Is AutoJMS.exe running?
2. Are bin/obj folders locked?
3. Is .NET SDK installed?

**Fix**: build-release.ps1 has Stop-AutoJMSProcesses function

### Issue 2: .NET Reactor Fails

**Symptom**: Reactor step fails, build continues

**Check**:
1. Reactor path exists: `D:\Cshap\.NET Reactor\dotNET_Reactor.Console.exe`
2. tools/reactor/AutoJMS_Reactor.nrproj is valid
3. Target DLL exists

**Fix**: Check PostBuildEvent in .csproj

### Issue 3: vpk pack Fails

**Symptom**: vpk command fails

**Check**:
1. Velopack CLI installed: `vpk --version`
2. Publish folder has AutoJMS.exe
3. Channel parameter is valid

**Fix**: Verify publish folder contents

### Issue 4: GitHub Upload Fails

**Symptom**: gh release create/upload fails

**Check**:
1. `gh auth status` - authenticated?
2. Repository exists: `Datt03-sss/AutoJMS-Update`
3. Tag format correct: stable `v1.26.6-Release`, beta `v1.26.6-beta.1-Release`

**Fix**: Run `gh auth login` if not authenticated

### Issue 5: Supabase Manifest Upload Fails

**Symptom**: version-latest.json not updated

**Check**:
1. SUPABASE_SERVICE_ROLE_KEY set?
2. Bucket exists: autojms-modules
3. Manifest file is valid JSON

**Fix**: Set env var or use Supabase CLI

## Version Format

### VelopackVersion vs DisplayVersion vs InternalBuild

| Release | VelopackVersion | DisplayVersion | InternalBuild |
|---------|-----------------|----------------|---------------|
| Stable | `1.26.6` | `1.26.6` | `1.26.6.0` |
| Beta | `1.26.6-beta.1` | `1.26.6 beta 1` | `1.26.6.1` |

**Rule**: do not convert `1.26.6.1` into `1.26.7`. Four-part values are `InternalBuild` only.

## Release Workflow

### Step 1: Build

```powershell
.\build-release.ps1 -Version "1.26.6" -Channel stable
.\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta
```

### Step 2: Verify Output

```
release/output/stable/
├── AutoJMS-stable-Setup.exe
├── AutoJMS.nupkg
├── RELEASES
└── version-latest.json (local copy)
```

### Step 3: Upload

```powershell
.\build-release.ps1 -Version "1.26.6" -Channel stable -Upload
.\build-release.ps1 -Version "1.26.6-beta.1" -Channel beta -Upload
```

## Update Flow Issues

### Issue: Update Not Detected

**Symptom**: App says "no update available"

**Check**:
1. Is version-latest.json updated in Supabase?
2. Does channel match?
3. Is provider=github?

**Fix**: Upload new version-latest.json

### Issue: Update Download Fails

**Symptom**: Download progress starts, then fails

**Check**:
1. GitHub repo accessible?
2. Release tag exists?
3. Assets present?

**Fix**: Check GitHub release assets

### Issue: Update App Opens Browser

**Symptom**: Browser opens during update

**Fix**: Velopack GithubSource should NOT open browser

```csharp
// Should use:
var source = new GithubSource(repoUrl, null, prerelease, null);

// NOT:
Process.Start("https://github.com/...");
```

## Velopack Configuration

### version-latest.json

```json
{
  "channels": {
    "stable": {
      "version": "1.26.6",
      "displayVersion": "1.26.6",
      "internalBuild": "1.26.6.0",
      "velopackChannel": "stable",
      "provider": "github",
      "githubRepo": "Datt03-sss/AutoJMS-Update",
      "tag": "v1.26.6-Release",
      "prerelease": false,
      "manualOnly": true,
      "mandatory": false
    }
  }
}
```

### provider values

| Provider | Meaning | Source |
|----------|---------|--------|
| github | Binaries from GitHub | Velopack GithubSource |
| supabase | Binaries from Supabase | Velopack SimpleWebSource |

## DO NOT

1. **DO NOT upload .nupkg to Supabase** - Will fail (size limit)
2. **DO NOT skip version-latest.json update** - App won't see update
3. **DO NOT change release tag format** - Velopack expects `v{version}-Release`
4. **DO NOT hardcode version** - Use parameters
5. **DO NOT use four-part values as VelopackVersion** - Use `internalBuild`

## Testing Release

### Test Build

```powershell
.\build-release.ps1 -Version "1.26.6" -Channel stable
```

### Test Installer

1. Run AutoJMS-win-Setup.exe
2. Verify install location
3. Verify shortcuts
4. Launch app

### Test Update

1. Install old version
2. Run app
3. Click "Kiểm tra cập nhật"
4. Verify update flow works

## After Fix

1. Run full build
2. Verify all assets uploaded
3. Test installer
4. Test update flow
5. Verify version-latest.json is correct

