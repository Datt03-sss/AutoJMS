# Inno Setup First Install

## Overview

Inno Setup creates the bootstrapper installer for first-time installation.

## When to Use

- First install on new machine
- Reinstall over existing
- Clean reinstall
- Uninstall and reinstall

## When NOT to Use

- In-app updates (use Velopack)

## Installer Output

```
AutoJMS-win-Setup.exe
```

## Installation Layout

```
C:\AutoJMS\
├── current\              ← Velopack installs here
├── packages\             ← Velopack cache
├── AppData\             ← User data
│   ├── AutoJMS.json
│   ├── secure\
│   │   ├── AutoJMS.secure
│   │   └── license.dat
│   ├── logs\
│   │   └── debug.log
│   ├── cache\
│   ├── BrowserData\
│   ├── Downloads\
│   │   └── Vận đơn đã in\
│   └── ZaloProfile\
└── AutoJMS.exe          ← Velopack stub
```

## Prerequisites

### Required

| Prerequisite | Action |
|-------------|--------|
| WebView2 Runtime | Install or download |

### Optional (Self-Contained)

| Prerequisite | Action |
|-------------|--------|
| .NET 8 Runtime | Install if not present |
| VC++ Redist | Install if not present |

## Build Script

```powershell
# Build installer
iscc installer/inno/AutoJMS.iss

# Or use the helper script
.\installer\inno\build-installer.ps1
```

## Key Inno Settings

```iss
[Setup]
PrivilegesRequired=admin
OutputBaseFilename=AutoJMS-win-Setup
Compression=lzma2
SolidCompression=yes

[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\current"; Permissions: users-modify
Name: "{app}\packages"; Permissions: users-modify
Name: "{app}\AppData"; Permissions: users-modify
```

## Testing

1. Uninstall existing
2. Delete C:\AutoJMS
3. Run installer
4. Verify layout
5. Launch app
6. Verify login works

