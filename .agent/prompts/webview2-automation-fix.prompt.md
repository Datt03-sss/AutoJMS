# WebView2 Automation Fix Prompt

Use this prompt when fixing WebView2 automation issues.

## Critical Rule

**WebView2 can ONLY be accessed from the UI thread.**

If you see code accessing WebView2 from a background thread, that's the bug.

## Common Issues

### Issue 1: Cross-Thread Access

```csharp
// BUG: Wrong thread
Task.Run(async () => {
    await webView.ExecuteScriptAsync(script);  // CRASH
});

// FIX: Marshal to UI thread
if (webView.InvokeRequired)
{
    await (Task)webView.Invoke(new Func<Task>(async () => 
        await webView.ExecuteScriptAsync(script)));
}
```

### Issue 2: WebView2 Not Initialized

```csharp
// BUG: Accessing before ready
await webView.ExecuteScriptAsync(script);

// FIX: Ensure initialized
if (webView.CoreWebView2 == null)
{
    await webView.EnsureCoreWebView2Async();
}
await webView.ExecuteScriptAsync(script);
```

### Issue 3: Vue Input Value Not Setting

```csharp
// BUG: Direct assignment doesn't work with Vue
input.value = 'test';
input.dispatchEvent(new Event('input'));

// FIX: Use Vue-compatible setter
var nativeSetter = Object.getOwnPropertyDescriptor(
    window.HTMLInputElement.prototype, 'value').set;
nativeSetter.call(input, 'test');
input.dispatchEvent(new Event('input', { bubbles: true }));
```

### Issue 4: Timeout Not Handled

```csharp
// BUG: No timeout
string result = await webView.ExecuteScriptAsync(script);

// FIX: Add timeout
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
try
{
    var task = webView.ExecuteScriptAsync(script);
    await task.WaitAsync(cts.Token);
}
catch (OperationCanceledException)
{
    AppLogger.Warning("Script timeout");
}
```

## Fix Workflow

### Step 1: Identify the Issue

1. Read the WebViewAutomation.cs file
2. Find the script execution code
3. Check for thread safety issues
4. Check for selector issues

### Step 2: Test Selectors

Use browser DevTools to verify selectors:

```javascript
// Check if selector exists
document.querySelector('.el-input__inner')

// Check Vue input setter
const input = document.querySelector('.el-input__inner');
if (input) {
    const nativeSetter = Object.getOwnPropertyDescriptor(
        HTMLInputElement.prototype, 'value').set;
    nativeSetter.call(input, 'test');
    input.dispatchEvent(new Event('input', { bubbles: true }));
}
```

### Step 3: Apply Fix

Apply the minimal fix:

```csharp
private async Task<bool> FillFormAsync(WebView2 webView, string waybillNo)
{
    // Ensure UI thread
    if (webView.InvokeRequired)
    {
        return await (Task<bool>)webView.Invoke(new Func<Task<bool>>(
            async () => await FillFormAsync(webView, waybillNo)));
    }

    // Ensure initialized
    if (webView.CoreWebView2 == null)
    {
        await webView.EnsureCoreWebView2Async();
    }

    // Vue-compatible input setter
    string script = $@"
        (function() {{
            var input = document.querySelector('.el-input__inner');
            if (input) {{
                var setter = Object.getOwnPropertyDescriptor(
                    window.HTMLInputElement.prototype, 'value').set;
                setter.call(input, '{waybillNo}');
                input.dispatchEvent(new Event('input', {{ bubbles: true }}));
                return true;
            }}
            return false;
        }})();
    ";

    string result = await webView.ExecuteScriptAsync(script);
    return result?.Trim() == "true";
}
```

### Step 4: Add Error Handling

```csharp
try
{
    var success = await FillFormAsync(webView, waybillNo);
    if (!success)
    {
        throw new Exception("Form field not found");
    }
}
catch (Exception ex)
{
    AppLogger.Error($"FillFormAsync failed: {ex.Message}");
    throw;
}
```

## Selector Reference

### Element UI Selectors

| Element | Selector |
|---------|----------|
| Input | `.el-input__inner` |
| Primary button | `.el-button--primary` |
| Form | `.el-form` |
| Dialog | `.el-dialog` |
| Table row | `.el-table__row` |
| Select dropdown | `.el-select-dropdown` |

### Vue Input Setter Pattern

```javascript
(function() {
    var input = document.querySelector('.el-input__inner');
    if (input) {
        var setter = Object.getOwnPropertyDescriptor(
            window.HTMLInputElement.prototype, 'value').set;
        setter.call(input, 'YOUR_VALUE');
        input.dispatchEvent(new Event('input', { bubbles: true }));
    }
})();
```

## Common Errors

| Error | Cause | Fix |
|-------|--------|-----|
| COMException (0x80070057) | Invalid arguments to WebView2 | Check parameters |
| InvalidOperationException | WebView2 not initialized | Call EnsureCoreWebView2Async first |
| UnauthorizedAccessException | Cross-thread access | Marshal to UI thread |
| TimeoutException | Script too slow | Increase timeout |

## Testing

### Manual Test Steps

1. Open app
2. Navigate to JMS
3. Open DevTools (right-click → Inspect)
4. Test selector in console
5. Verify Vue setter works
6. Run automated flow
7. Check logs for errors

### Automated Test

```csharp
[Fact]
public async Task FillForm_WithValidSelector_SetsValue()
{
    // Arrange
    var webView = new WebView2();
    await webView.EnsureCoreWebView2Async();
    await webView.CoreWebView2.NavigateToHtml("<input class='el-input__inner'/>");

    // Act
    var result = await FillFormAsync(webView, "test123");

    // Assert
    Assert.True(result);
}
```

## After Fix

1. Test in actual app
2. Verify selector still works after JMS updates
3. Check for regressions in other automation flows
4. Update selector documentation if patterns changed
