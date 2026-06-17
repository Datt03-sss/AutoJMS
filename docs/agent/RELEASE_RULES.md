# AutoJMS Release Rules

Releasing a production version is a critical operation. These rules define the process and boundaries.

---

## 1. Owner-Only Operation
**AI agents must NEVER trigger a production release.**
Only the human project owner is authorized to:
- Tag git commits.
- Run `build-release.ps1` with the `-Upload` flag.
- Distribute Inno Setup `.exe` installers.
- Update release manifests on Supabase Storage.

---

## 2. Release Steps (Owner Only)

1. **Verify Solution**: Run the full verification suite on the `main` branch.
2. **Bump Version**: Update version numbers in `src/AutoJMS/AutoJMS.csproj`:
   ```xml
   <Version>X.Y.Z</Version>
   <AssemblyVersion>X.Y.Z.0</AssemblyVersion>
   <FileVersion>X.Y.Z.0</FileVersion>
   <InformationalVersion>X.Y.Z</InformationalVersion>
   ```
3. **Execute Release Build**:
   ```powershell
   .\release\build-release.ps1 -Version "X.Y.Z.0" -Channel stable
   ```
4. **Smoke Test locally**: Run the generated binary in `release/output/` and verify that the `ABOUT` tab shows the correct version.
5. **Publish to Production**:
   ```powershell
   .\release\build-release.ps1 -Version "X.Y.Z.0" -Channel stable -Upload
   ```

---

## 3. Agent Dry-Run Guidelines
AI agents can simulate a release to test compile and publish paths. This must be done strictly locally without remote uploads:
- **Command**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\eng\harness\release-dryrun.ps1
  ```
- **Validation**:
  - Confirms compilation in Release mode.
  - Generates binary packages in a local dry-run folder.
  - Ensures no network calls are made to GitHub Releases or Supabase storage buckets.
