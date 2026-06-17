# Inno Setup Skill

## Overview

Inno Setup creates the first-install/reinstall bootstrapper. Velopack handles in-app updates.

## When to Use Inno Setup

| Scenario | Tool |
|----------|------|
| First install | Inno Setup |
| Reinstall | Inno Setup |
| Uninstall | Inno Setup |
| Runtime prerequisites | Inno Setup |
| In-app update | Velopack |

## Install Layout

```
C:\AutoJMS\
├── current\          ← Velopack installs here
├── packages\         ← Velopack cache
├── AppData\          ← User data
│   ├── logs\
│   ├── secure\
│   ├── cache\
│   ├── Downloads\
│   ├── BrowserData\
│   └── AutoJMS.json
└── AutoJMS.exe      ← Velopack stub (shortcut target)
```

## Key Components

### [Dirs] Section

```iss
[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\current"; Permissions: users-modify
Name: "{app}\packages"; Permissions: users-modify
Name: "{app}\AppData"; Permissions: users-modify
```

### [Files] Section

```iss
[Files]
Source: "..\..\release\output\stable\AutoJMS-stable-Setup.exe"; Flags: dontcopy
```

### [Run] Section

```iss
[Run]
Filename: "{app}\AutoJMS.exe"; Description: "Launch AutoJMS"; Flags: nowait postinstall
```

## Prerequisites

### WebView2 (Required)

```pascal
function InstallWebView2IfNeeded(): Boolean;
begin
  Result := True;
  if not IsWebView2Installed() then
  begin
    // Download or extract WebView2 installer
    // Run silently
    // Fatal if fails
  end;
end;
```

### .NET Runtime (Optional - Self-Contained)

```pascal
function InstallDotNetIfNeeded(): Boolean;
begin
  // Since app is self-contained, this is optional
  Result := True;
end;
```

## Velopack Integration

```pascal
function RunVelopackSetup(): Boolean;
var
  TmpDir, SetupPath, AppDir: String;
begin
  TmpDir := ExpandConstant('{tmp}');
  AppDir := ExpandConstant('{app}');
  
  // Extract bundled VelopackSetup.exe
  ExtractTemporaryFile('AutoJMS-stable-Setup.exe');
  SetupPath := TmpDir + '\AutoJMS-stable-Setup.exe';
  
  // Run silently
  Exec(SetupPath, '--silent --installto "' + AppDir + '"', '', SW_SHIDE, ewWaitUntilTerminated, Result);
end;
```

## Uninstall Integration

```iss
[UninstallRun]
Filename: "{app}\current\AutoJMS.exe"; Parameters: "--veloUninstall"; Flags: runhidden
```

## Common Issues

| Issue | Cause | Solution |
|-------|--------|----------|
| WebView2 missing | Required prereq failed | Ensure WebView2 installs correctly |
| Velopack fails | Permissions issue | Grant users-modify on dirs |
| Shortcuts broken | Wrong exe path | Shortcuts must point to root exe |
| Update fails | Old install layout | Check install follows current layout |

## Testing Checklist

- [ ] Clean install on Windows 10
- [ ] Clean install on Windows 11
- [ ] Reinstall over existing
- [ ] Uninstall completely
- [ ] Verify shortcuts work
- [ ] Verify uninstall removes all files
- [ ] Test on different user accounts
