# Supabase Manifest Errors

## Current Verified Issues

Historical build-time issue:

- `src/AutoJMS/AutoJMS.csproj` referenced root `modules/*.json` files while the root `modules/` folder was missing.
- This previously caused `MSB3030 Could not copy the file ... because it was not found`.
- Current fix: the root `modules/*.json` `Content Include` entries are guarded by `Condition="Exists('...')"`.

Runtime/control-plane issues to verify:

- `hash-manifest.json` sample shape may not match `HashManifest.cs`.
- `tier-definitions.sec` vs `tier-definitions.json` naming is inconsistent across examples.
- `supabase-migration.sql` does not define the waybill/inventory RPCs called by `SupabaseDbService`.

Required rule:

- Supabase Storage is for small manifests/configs. Do not upload `.nupkg` or large Velopack binaries to Supabase.

Older notes below are retained.

## Common Errors

### Error: Manifest not found

**Cause**: version-latest.json not uploaded

**Fix**: Upload to Supabase Storage

### Error: Invalid JSON

**Cause**: Malformed JSON

**Fix**: Validate JSON before upload

### Error: Wrong channel

**Cause**: Requested channel doesn't exist

**Fix**: Check version-latest.json structure

## Debug

Check manifest accessible:
```powershell
Invoke-RestMethod https://.../version-latest.json
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

