# DataHub Manifest Errors

## Current Verified Issues

Historical build-time issue:

- `src/AutoJMS/AutoJMS.csproj` referenced root `modules/*.json` files while the root `modules/` folder was missing.
- This previously caused `MSB3030 Could not copy the file ... because it was not found`.
- Current fix: the root `modules/*.json` `Content Include` entries are guarded by `Condition="Exists('...')"`.

Runtime/control-plane issues to verify:

- `hash-manifest.json` sample shape may not match `HashManifest.cs`.
- `tier-definitions.sec` vs `tier-definitions.json` naming is inconsistent across examples.
- `PUT /api/v1/admin/manifests/{objectPath}` — the route `release/build-release.ps1 -Upload`
  posts to — is not implemented in `src/AutoJMS.DataHub.Api`, is absent from
  `backend/datahub/openapi/datahub-v1.yaml`, and has no `Caddyfile` handler. Every publish
  attempt returns 404 until it lands.

Required rule:

- The DataHub control plane carries small manifests/configs only. Binaries — `.nupkg`,
  `RELEASES`, `Setup.exe` — belong in GitHub Releases.

Older notes below are retained.

## Common Errors

### Error: Manifest not found

**Cause**: `version-latest.json` was never published, or `DATAHUB_MANIFEST_BASE_URL` points
somewhere else than `DATAHUB_API_BASE_URL`.

**Fix**: `curl` the full public URL. If it 404s, publish the file on the VPS by hand — `-Upload`
is currently broken (see above).

### Error: Invalid JSON

**Cause**: Malformed JSON

**Fix**: Validate JSON before upload

### Error: Wrong channel

**Cause**: Requested channel doesn't exist

**Fix**: Check version-latest.json structure

## Debug

Check manifest accessible:
```powershell
Invoke-RestMethod https://dev.jmsauto.online/manifest/version-latest.json
```

Verify structure:
```json
{
  "channels": {
    "stable": { ... },
    "beta": { ... }
  }
}
```

