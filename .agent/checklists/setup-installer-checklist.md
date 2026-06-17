# Setup Installer Checklist

Use this checklist when setting up Inno Setup.

## Prerequisites

### Files Needed

- [ ] Velopack Setup (.exe)
- [ ] Runtime prerequisites (optional)

### Runtime Prerequisites

| Prerequisite | Required | Notes |
|-------------|----------|-------|
| WebView2 | Yes | Fatal if missing |
| .NET Runtime | No | Self-contained |
| VC++ Redist | No | Non-fatal |

## Installation Layout

### Verify Layout

```
C:\AutoJMS\
├── current\           ← Velopack
├── packages\
├── AppData\
│   ├── logs\
│   ├── secure\
│   ├── cache\
│   ├── BrowserData\
│   └── Downloads\
└── AutoJMS.exe      ← Stub launcher
```

### Permissions

- [ ] users-modify on all dirs
- [ ] Shortcuts target correct exe

## Test Cases

### Clean Install

1. Uninstall existing
2. Delete C:\AutoJMS
3. Run installer
4. Verify layout
5. Launch app
6. Verify login

### Reinstall

1. Run installer over existing
2. Verify files replaced
3. Verify user data preserved
4. Launch app
5. Verify state

### Uninstall

1. Run uninstaller
2. Verify C:\AutoJMS removed
3. Verify shortcuts removed
4. Verify no leftover files

## Common Issues

| Issue | Cause | Fix |
|-------|-------|-----|
| WebView2 missing | Install failed | Ensure download succeeds |
| Permissions error | Admin required | Run as admin |
| Shortcut broken | Wrong target | Fix exe path |
| Update fails | Old layout | Update installer |
