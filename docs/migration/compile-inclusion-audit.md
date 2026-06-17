# Compile Inclusion Audit

Audit scope: current root `src/AutoJMS/AutoJMS.csproj`, `AutoJMS.slnx`, module project files, project content metadata, resources and output-copy behavior.

This is a plan-only audit. No project file was modified.

## Project model

- `src/AutoJMS/AutoJMS.csproj` uses `Microsoft.NET.Sdk`.
- Target: `net8.0-windows`.
- WinForms enabled: `<UseWindowsForms>true</UseWindowsForms>`.
- SDK-style default items are active unless explicitly removed.
- `AutoJMS.slnx` includes:
  - `src/AutoJMS/AutoJMS.csproj`
  - `src\AutoJMS.Abstractions\AutoJMS.Abstractions.csproj`

## Compile inclusion

### Main app project

`src/AutoJMS/AutoJMS.csproj` does not explicitly list every root `.cs` file. With SDK defaults, these compile by default:

- Root `*.cs`, including `Program.cs`, `Main.cs`, `FullStackOperation.cs`, service/model/config files.
- Root `*.Designer.cs`, including `Main.Designer.cs`, `FullStackOperation.Designer.cs`, `frmLogin.Designer.cs`.
- `ModuleSystem/**/*.cs`.
- `Properties/Resources.Designer.cs`, with explicit metadata update:

```xml
<Compile Update="Properties\Resources.Designer.cs">
  <DesignTime>True</DesignTime>
  <AutoGen>True</AutoGen>
  <DependentUpon>Resources.resx</DependentUpon>
</Compile>
```

### Explicit compile removal

The main app excludes `archive/old-module-system/**` from default app compilation:

```xml
<Compile Remove="archive\old-module-system\**\*.cs" />
<Content Remove="archive\old-module-system\**\*" />
<EmbeddedResource Remove="archive\old-module-system\**\*" />
<None Remove="archive\old-module-system\**\*" />
```

Impact:

- `src/AutoJMS.Abstractions` is not compiled as loose source into main app.
- It is compiled through `ProjectReference`.
- `archive/old-module-system/AutoJMS.RetryPolicy` and `archive/old-module-system/AutoJMS.Selectors` are not referenced by `AutoJMS.slnx` currently. They may compile individually, but main app does not build them by default. Status: `NEED VERIFY`.

### ProjectReference

```xml
<ProjectReference Include="src\AutoJMS.Abstractions\AutoJMS.Abstractions.csproj" />
```

`AutoJMS.Abstractions.csproj` targets `net8.0` and contains `ModuleInterfaces.cs`.

## Embedded resources

SDK defaults embed `.resx` files unless removed. Current relevant resources:

- `Main.resx`
- `FullStackOperation.resx`
- `frmLogin.resx`
- `Properties/Resources.resx`

`Properties/Resources.resx` has explicit metadata:

```xml
<EmbeddedResource Update="Properties\Resources.resx">
  <Generator>PublicResXFileCodeGenerator</Generator>
  <LastGenOutput>Resources.Designer.cs</LastGenOutput>
</EmbeddedResource>
```

Migration risk:

- Moving WinForms `.cs`, `.Designer.cs`, and `.resx` files must preserve dependent file relationships.
- Moving `Properties/Resources.resx` must preserve generated `Resources.Designer.cs` behavior.
- Moving `Resources/` images requires verifying `.resx` references and designer resource paths.

## Content includes and output copy

Explicit content items:

```xml
<Content Include="favicon.ico" />
<Content Include="modules\app-manifest.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('modules\app-manifest.json')" />
<Content Include="modules\active_modules.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('modules\active_modules.json')" />
<Content Include="modules\modules-cache.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('modules\modules-cache.json')" />
<Content Include="modules\selectors.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('modules\selectors.json')" />
<Content Include="modules\config.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('modules\config.json')" />
<Content Include="tier-definitions.json" CopyToOutputDirectory="PreserveNewest" />
```

Output-copy behavior:

- `tier-definitions.json` is always copied to output.
- Root `modules/*.json` files are copied only when each file exists.
- `favicon.ico` is explicit content and application icon; output-copy behavior is not explicitly set in project metadata.

## None includes

SDK-style projects may include non-code files as `None` by default. Current files that are likely non-compiled and not explicitly copied include:

- `AutoJMS.json`
- `AutoJMS.csproj.user`
- `tools/reactor/AutoJMS_Reactor.nrproj`
- root markdown/text files
- `service_account.json`

Migration risk:

- `service_account.json` is sensitive and must not be moved into a tracked backend target.
- `AutoJMS.json` may be a runtime/default settings template; effective runtime dependency is `NEED VERIFY`.
- `tools/reactor/AutoJMS_Reactor.nrproj` is used by the Release Reactor target path in `src/AutoJMS/AutoJMS.csproj` and release tooling; moving it requires script/project updates.

## Files currently compile into main app

Current main app compile scope includes:

- `Program.cs`
- `Main.cs`
- `Main.Designer.cs`
- `FullStackOperation.cs`
- `FullStackOperation.Designer.cs`
- `frmLogin.cs`
- `frmLogin.Designer.cs`
- all root production `.cs` service/model/config files
- `ModuleSystem/*.cs`
- `Properties/Resources.Designer.cs`

Current main app embedded resources include:

- `Main.resx`
- `FullStackOperation.resx`
- `frmLogin.resx`
- `Properties/Resources.resx`

Current main app copied/runtime content includes:

- `tier-definitions.json`
- existing root `modules/*.json` files if present

## Files currently copied to output

Confirmed by project metadata:

- `tier-definitions.json`
- `modules/app-manifest.json` if present
- `modules/active_modules.json` if present
- `modules/modules-cache.json` if present
- `modules/selectors.json` if present
- `modules/config.json` if present

`favicon.ico` is explicit content/application icon but does not have explicit `CopyToOutputDirectory`.

## Missing file status

- Root `modules/` folder is not present in current top-level listing.
- Missing root `modules/*.json` no longer blocks build because all root module content includes are conditional.
- Runtime requirement for absent defaults remains `NEED VERIFY`.

## Files that should stay outside source

These should not live under `src/AutoJMS` after migration:

- `.agent/`
- `docs/`
- `ServerStructure/` after copied/migrated to `backend/`
- `release/` after migrated to `tools/release`
- `installer/inno/` after migrated to `tools/installer`
- `bin/`
- `obj/`
- `publish/`
- `release/output/`
- `installer/inno/installer-output/`
- `.vs/`
- secrets such as `service_account.json`

## Recommended migration checks before source move

1. Create a branch.
2. Move only one category per commit.
3. Update `.slnx` / solution path.
4. Update `src/AutoJMS/AutoJMS.csproj` paths for content/resource/Reactor references.
5. Verify WinForms Designer dependent files.
6. Run `dotnet build src/AutoJMS/AutoJMS.csproj -c Debug`.
7. Run Release script dry-run only after script paths are updated.
8. Smoke test app launch and BASE tabs.


