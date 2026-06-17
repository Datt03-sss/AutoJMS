# AutoJMS Installer

## Overview

This folder contains the Inno Setup script and build tools to create a professional Windows installer for AutoJMS (`AutoJMS-Installer-<version>.exe`).

The installer handles:

- First-time installation
- Reinstall / upgrade over existing installation
- Runtime prerequisite detection and installation (.NET 8, WebView2, VC++ Redist)
- Running process detection and graceful shutdown
- User data preservation during reinstall
- Clean uninstall via Programs & Features
- UAC elevation prompt on double-click install for the default `C:\AutoJMS` root

After installation, the app updates itself via Velopack + GitHub Releases (not the installer).

---

## Prerequisites

### 1. Inno Setup 6

Download and install from: https://jrsoftware.org/isdl.php

Install to the **default location** (`C:\Program Files (x86)\Inno Setup 6`).

The build script will detect it automatically. Vietnamese language files are included with the default install.

### 2. .NET 8 SDK (for publish)

You need the .NET 8 SDK to publish the app before building the installer:

```cmd
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts\publish\win-x64
```

Or use the existing `build-release.ps1` script (it publishes to `artifacts\publish\win-x64`).

---

## Folder Structure

```
installer/inno/
├── build-installer.bat          # Double-click launcher
├── build-installer.ps1          # PowerShell build script
├── AutoJMS.iss                  # Inno Setup script
├── README_INSTALLER.md          # This file
├── redist/                      # Optional: offline runtime installers
│   ├── windowsdesktop-runtime-8.0.x-win-x64.exe
│   ├── MicrosoftEdgeWebView2RuntimeInstallerX64.exe
│   └── vc_redist.x64.exe
├── publish/
│   └── win-x64/                 # Optional: copied publish output
├── installer-output/            # Output: AutoJMS_Setup.exe
└── ...
```

---

## How to Build

### Quick Build (double-click)

1. Run `dotnet publish` from the project root first (or use `build-release.ps1`).
2. Double-click `installer\inno\build-installer.bat`.
3. Enter the app version when prompted (e.g. `1.26.5.2`).
4. The installer is created at `installer\inno\installer-output\AutoJMS-Installer-<version>.exe`.

### Advanced Build (PowerShell)

Run from `installer\inno\` folder:

```powershell
.\build-installer.ps1 -Version "1.26.5.2"
```

With custom paths:

```powershell
.\build-installer.ps1 -Version "1.26.5.2" -PublishDir "D:\builds\artifacts\publish\win-x64" -OutputDir "D:\releases"
```

For CI (no pause on error):

```powershell
.\build-installer.ps1 -Version "1.26.5.2" -NoPause
```

---

## Offline Runtime Installers (Optional)

To avoid downloading runtimes during installation, place offline installers in `installer\inno\redist\`:

| File | Purpose | Download URL |
|------|---------|-------------|
| `windowsdesktop-runtime-8.0.x-win-x64.exe` | .NET 8 Desktop Runtime | https://dotnet.microsoft.com/en-us/download/dotnet/8.0 |
| `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` | WebView2 Runtime | https://developer.microsoft.com/en-us/microsoft-edge/webview2/ |
| `vc_redist.x64.exe` | VC++ Redistributable | https://aka.ms/vs/17/release/vc_redist.x64.exe |

**Note:** AutoJMS is published as **self-contained** (`--self-contained true`), so the .NET 8 runtime is bundled with the app. The .NET runtime check is skipped automatically. Only WebView2 and VC++ Redist are checked.

If offline installers are not provided, the installer will download them from Microsoft's official URLs during installation.

---

## How the Installer Works

### Installation Flow

1. **Welcome** — displays welcome page with app info.
2. **License** — standard Inno Setup license page.
3. **Select Directory** — user can choose install folder (default: `C:\AutoJMS`).
4. **Select Start Menu Folder** — user can choose Start Menu group.
5. **Select Tasks** — option to create desktop shortcut.
6. **Ready to Install** — summary of all choices.
7. **Installing** — copies files, installs runtimes if needed.
8. **Finish** — option to launch AutoJMS immediately.

### Prerequisite Checks

During installation, the script checks for:

- **.NET 8 Desktop Runtime** — skipped for self-contained publish (default).
- **WebView2 Runtime** — checked via registry; installed if missing.
- **VC++ Redistributable x64** — checked via registry; installed if missing.

If a runtime is missing, the installer:

1. Looks in `redist\` folder for the offline installer.
2. If not found, downloads from Microsoft's official URL.
3. Installs silently.
4. If installation fails, shows an error and aborts.

### Running Process Detection

If AutoJMS is running during install/uninstall:

1. A dialog asks the user to confirm closing the app.
2. On confirmation, the process is terminated via `taskkill`.
3. Installation/uninstallation proceeds.

### User Data Preservation

- **`{app}\AppData`** — user settings and runtime data live inside the install root and are preserved across Velopack updates.
- **`{app}\AppData\AutoJMS.json`** — configuration file is preserved.
- **`{app}\AppData\secure`** — encrypted license/runtime cache.
- Only files in the installation directory (`{app}`) are removed during uninstall.

### Reinstall / Upgrade

- Running the installer over an existing installation performs an upgrade.
- Old files are overwritten (with `ignoreversion` flag).
- User data under `{app}\AppData` is untouched.
- No need to uninstall first.

### Velopack Migration

If a previous Velopack-based installation is detected at `C:\AutoJMS`, `%LocalAppData%\AutoJMS`, or the chosen install root, the installer:

- Shows a warning.
- Offers to remove the old Velopack installation.
- User settings are preserved.

---

## Troubleshooting

### "AutoJMS.exe is running" error during uninstall

Close the app manually from the system tray, or use Task Manager to end the process.

### "Failed to remove existing application directory"

1. Ensure AutoJMS is not running (check system tray and Task Manager).
2. Restart Windows and try the install again.
3. If the issue persists, manually delete the installation folder and try again.

### "Cannot find Inno Setup" during build

Install Inno Setup 6 from https://jrsoftware.org/isdl.php. Default installation path is required.

### Installer fails at runtime installation step

- Check your internet connection (for download).
- Place offline installers in `installer\inno\redist\` to avoid downloading.
- Run the installer normally; double-clicking it requests admin rights for the default `C:\AutoJMS` root and still lets the user choose the install folder.

### Vietnamese language not displayed

Vietnamese language support is included in the default Inno Setup installation. If not visible, reinstall Inno Setup with full language support.

---

## Important Notes

- The installer is for **first-time install / reinstall / repair only**.
- After installation, AutoJMS updates itself via **Velopack + GitHub Releases**.
- The installer does **not** browse GitHub pages; it only launches the bundled Velopack setup and passes `--installto "{app}"`.
- All update logic after installation is handled by the app itself.
- The installer requests **admin privileges** because the default path is `C:\AutoJMS`; it writes app/runtime files under the user-selected install root.
- The Inno script sets `PrivilegesRequired=admin` and does not declare `PrivilegesRequiredOverridesAllowed`, so a normal double-click install opens UAC and does not silently downgrade to non-admin.
<｜｜DSML｜｜parameter name="endLine" string="true">130

