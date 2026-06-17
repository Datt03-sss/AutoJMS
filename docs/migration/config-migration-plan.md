# Config Migration Plan

## Overview

This document describes the configuration data flow and potential migration paths.

## Current Config Architecture

### Config Files

| File | Location | Encrypted | Purpose |
|------|----------|----------|---------|
| AutoJMS.json | UserDataDir | No | User settings |
| AutoJMS.json | InstallDir | No | Default template |
| AutoJMS.secure | UserDataDir/secure | Yes (AES) | Runtime secrets |
| license.dat | UserDataDir/secure | Yes (DPAPI) | License key |

### Config Loading Flow

```
Program.Main
    ↓
AppConfig.LoadBootstrap()
    ├─► Load AutoJMS.json from InstallDir (defaults)
    ├─► Load AutoJMS.json from UserDataDir (user overrides)
    ├─► Override with env vars if present
    └─► Return AppConfig instance

License verified
    ↓
InitializeServicesFromLicense(config)
    ├─► Set Supabase URLs
    ├─► Set manifest URLs
    └─► Set tier definitions
```

### Environment Variable Override

```csharp
// AppConfig.cs pattern
if (Environment.GetEnvironmentVariable("AUTOJMS_FIREBASE_URL") is string url)
    Config.FirebaseUrl = url;
```

## Migration Considerations

### 1. Secure Config Encryption

**Current**: AES-CBC-HMACSHA256 with machine-derived key

**Risk**: Key is derived from machine info, so reinstalling Windows breaks config

**Mitigation**: User must re-enter license key after OS reinstall

### 2. AutoJMS.json Template

**Location**: InstallDir (shipped with app)

**Purpose**: Default values for new users or corrupted configs

**Migration**: Keep at root, document clearly

### 3. User Data Location

**Current**: `AppPaths.InstallRoot\AppData\`

**Issue**: Inside install directory may require admin to modify

**Recommendation**: Keep current structure, Velopack handles permissions

## No Immediate Migration Needed

Current config architecture is functional and well-documented.
