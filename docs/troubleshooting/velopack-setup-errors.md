# Velopack Setup Errors

## Common Errors

### Error: Velopack not installed

**Cause**: Velopack CLI not found

**Fix**: Install with `dotnet tool install -g vpk`

### Error: Update fails

**Cause**: App not in Velopack layout

**Fix**: Install with Inno Setup first

### Error: Binary not found

**Cause**: Wrong path to publish folder

**Fix**: Check `--packDir` parameter

## Debug

Check Velopack layout:
```powershell
vpk list
```

Verify update manager:
```csharp
if (!mgr.IsInstalled)
    ShowInfo("Not in Velopack layout");
```
