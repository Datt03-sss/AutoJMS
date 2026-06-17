# C# WinForms Rules

## General Guidelines

### Thread Safety

1. **All UI access must be on UI thread**
   - Use `Invoke` or `BeginInvoke` for cross-thread calls
   - Use `UiThread.InvokeOnUiAsync()` helper when available
   - WebView2 can ONLY be accessed from UI thread

2. **Background operations must be truly async**
   - Use `async/await`, not `.GetAwaiter().GetResult()`
   - Return `Task` for methods that do async work
   - Use `CancellationToken` for cancellation

3. **Never block UI thread**
   - Long operations → `Task.Run` or `async`
   - File I/O → `async` methods
   - Network calls → `HttpClient` async methods

### WinForms Specific

1. **Control lifecycle**
   - Check `IsDisposed` before accessing
   - Check `IsHandleCreated` before accessing
   - Use `InvokeRequired` guard pattern

2. **Form lifecycle**
   - `OnLoad` for initialization
   - `OnShown` for post-load operations
   - `FormClosing` for cleanup
   - Never do heavy work in constructor

3. **Timer usage**
   - Use `System.Windows.Forms.Timer` for UI thread
   - Use `System.Timers.Timer` for background
   - Always `Dispose` timers

### WebView2 Rules

1. **Initialization**
   - Always `EnsureCoreWebView2Async` before use
   - Set `UserDataFolder` for persistent state
   - Set `UserAgent` consistently

2. **Script execution**
   - Use `ExecuteScriptAsync` (fire-and-forget)
   - Use `WaitForApiResponseAsync` for response capture
   - Always handle exceptions

3. **UI thread requirement**
   - `ExecuteScriptAsync` MUST be on UI thread
   - Use `UiThread.InvokeOnUiAsync()` helper

## Pattern Examples

### Correct UI Thread Access

```csharp
// CORRECT: Marshal to UI thread
public async Task<string> GetTokenFromWebViewAsync()
{
    if (this.InvokeRequired)
    {
        return await UiThread.InvokeOnUiAsync(this, async () => 
        {
            await RefreshAuthTokenCoreAsync();
            return JmsAuthStateService.CurrentToken;
        });
    }
    await RefreshAuthTokenCoreAsync();
    return JmsAuthStateService.CurrentToken;
}
```

### Correct Background Work

```csharp
// CORRECT: True async background work
private async Task ExecuteSyncWorkflowAsync(CancellationToken ct, ...)
{
    await SupabaseDbService.InitializeAsync();
    // ... async work
}

// WRONG: Blocking UI thread
// var result = SomeAsyncMethod().GetAwaiter().GetResult();
```

### Correct Timer Usage

```csharp
// CORRECT: Timer with proper cleanup
_autoSyncTimer = new System.Windows.Forms.Timer();
_autoSyncTimer.Interval = 1000;
_autoSyncTimer.Tick += async (s, e) => await HandleAutoSyncTickAsync(_appCts.Token);
_autoSyncTimer.Start();

// In FormClosing:
_autoSyncTimer?.Stop();
_autoSyncTimer?.Dispose();
```

### Correct Control Access

```csharp
// CORRECT: Check before access
private void UpdateDkchButtonsByState(bool isRunning)
{
    if (tabDKCH_btnDKCH1 == null || tabDKCH_btnDKCH1.IsDisposed) return;
    // ... safe access
}
```

## SunnyUI Guidelines

1. Use `Sunny.UI.UIForm` as base for forms
2. Use `Sunny.UI.UIDataGridView` for DataGridView
3. Use `Sunny.UI.UIMessageTip` for notifications
4. Use `Sunny.UI.UIButton` for buttons
5. Use `Sunny.UI.UILabel` for labels

## DataGridView Guidelines

1. Enable double buffering for large datasets:
   ```csharp
   var prop = grid.GetType().GetProperty("DoubleBuffered", 
       BindingFlags.Instance | BindingFlags.NonPublic);
   prop?.SetValue(grid, true, null);
   ```

2. Apply standard settings:
   ```csharp
   grid.ReadOnly = true;
   grid.AllowUserToAddRows = false;
   grid.AllowUserToDeleteRows = false;
   grid.MultiSelect = true;
   ```

3. Handle `DataBindingComplete` for column configuration

## Logging Guidelines

1. Use `AppLogger.Info/Warning/Error` for structured logging
2. Log before and after significant operations
3. Log exceptions with context
4. Mask sensitive data (tokens, passwords)
5. Use `AppLogger.Action` for user-initiated actions

## File Organization

### Single-File Classes

For smaller services, keep in single file with related types:
- `JmsApiClient.cs` - API client and related models
- `SupabaseDbService.cs` - Database operations
- `InventorySyncService.cs` - Inventory sync logic

### Designer Files

Always keep `.Designer.cs` paired with main `.cs`:
- `Main.cs` + `Main.Designer.cs`
- `frmLogin.cs` + `frmLogin.Designer.cs`
- `FullStackOperation.cs` + `FullStackOperation.Designer.cs`

## Error Handling

1. **Catch specific exceptions first**
2. **Log with context**
3. **Show user-friendly messages** (not stack traces)
4. **Don't swallow exceptions silently** (at minimum log it)
5. **Use CancellationToken** for cancellation
