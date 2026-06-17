# Build Errors

## MSB3030 Missing `modules/*.json`

### Symptom

Command:

```powershell
dotnet build src/AutoJMS/AutoJMS.csproj -c Debug
```

Failure:

```text
error MSB3030: Could not copy the file "...\modules\selectors.json" because it was not found.
error MSB3030: Could not copy the file "...\modules\active_modules.json" because it was not found.
error MSB3030: Could not copy the file "...\modules\app-manifest.json" because it was not found.
error MSB3030: Could not copy the file "...\modules\config.json" because it was not found.
error MSB3030: Could not copy the file "...\modules\modules-cache.json" because it was not found.
```

### Cause

`src/AutoJMS/AutoJMS.csproj` included `modules/*.json` files as content. Before the source workspace migration, the expected module JSON files were missing from the project directory, so MSBuild failed while copying non-existent content files.

The module runtime has fallbacks:

- `ActiveModules.LoadLocal()` returns an empty active module set if `active_modules.json` is absent.
- `AppManifest.LoadLocal()` returns a default manifest if `app-manifest.json` is absent.
- Module cache loaders return `null` or empty cache and continue with built-in modules.
- `Program.cs` registers built-in modules before loading active modules.

### Fix Applied

Each optional module JSON content include now has an `Exists(...)` condition:

```xml
<Content Include="modules\app-manifest.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('modules\app-manifest.json')" />
```

This preserves behavior when files exist, and avoids failing the build when they do not.

After the source workspace migration, minimal default files also exist under `src/AutoJMS/modules/`:

- `app-manifest.json`
- `active_modules.json`
- `modules-cache.json`
- `selectors.json`
- `config.json`

### Notes

- No runtime code was changed.
- Minimal default module JSON files were added under `src/AutoJMS/modules/`.
- No secrets were added.
- Release and installer scripts were updated later as part of structure migration path fixes.

### Verification

Verified command:

```powershell
dotnet build src/AutoJMS/AutoJMS.csproj -c Debug
```

Result after fix:

```text
Build succeeded.
0 Error(s)
```

Latest structure migration verification on 2026-06-03:

```text
Build succeeded.
3 Warning(s)
0 Error(s)
```

Remaining warnings are outside this specific fix:

- `PdfiumViewer 2.13.0` package compatibility warning for `net8.0-windows`.
- `WindowsBase` version conflict warning from WebView2 WPF assets.
- Obsolete `GoogleCredential.FromJson` / `GoogleCredential.FromStream` warnings.
- Nullable annotations while nullable is disabled.
- Unawaited Google Sheet calls in `Main.cs`.
- `MSB3061` warnings while deleting old `bin\Debug\...\modules\*.json` files; build still succeeds.

