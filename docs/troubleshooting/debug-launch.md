# Debug Launch

Date: 2026-06-03

## Current Debug Entry Points

Visual Studio:

```powershell
start AutoJMS.sln
```

Set `AutoJMS` as startup project if Visual Studio does not select it automatically.

VS Code:

```text
Run and Debug -> AutoJMS Debug (.NET 8 WinForms)
```

The VS Code launch profile builds:

```text
src/AutoJMS/AutoJMS.csproj
```

and launches:

```text
src/AutoJMS/bin/Debug/net8.0-windows/win-x64/AutoJMS.exe
```

## Notes

- `AUTOJMS_GOOGLE_CREDENTIAL_PATH` is set to `${workspaceFolder}/service_account.json` for local debug only.
- The secret file is not copied into build output.
- `src/AutoJMS/AutoJMS.csproj.user` restores WinForms form subtype metadata for the moved `Forms/` paths.

## Verification

```powershell
dotnet build AutoJMS.sln -c Debug
```

Latest result: build succeeded with 0 errors.
