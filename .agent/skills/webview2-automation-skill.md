# WebView2 Automation Skill

## Overview

AutoJMS uses Microsoft WebView2 for browser automation of JMS website (Vue.js + Element UI).

## Key Rules

1. **WebView2 MUST be accessed from UI thread**
2. **Always EnsureCoreWebView2Async before use**
3. **Use Vue-compatible input setters**
4. **Handle timeouts**

## Initialization

```csharp
// Set up creation properties
var props = new CoreWebView2CreationProperties
{
    UserDataFolder = sharedFolder  // Shared for same browser profile
};

// Assign to WebView2 control
webView.CreationProperties = props;

// Initialize
await webView.EnsureCoreWebView2Async(null);

// Configure
webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0...";
webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
```

## Token Capture

```csharp
// Add request filter
webView.CoreWebView2.AddWebResourceRequestedFilter(
    "*",
    CoreWebView2WebResourceContext.Fetch);
webView.CoreWebView2.AddWebResourceRequestedFilter(
    "*",
    CoreWebView2WebResourceContext.XmlHttpRequest);

// Handle events
webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;

private void OnWebResourceRequested(object sender, 
    CoreWebView2WebResourceRequestedEventArgs e)
{
    // Check headers for token
    var headers = e.Request.Headers;
    string[] tokenNames = { "authToken", "YL_TOKEN", "token" };
    
    foreach (var name in tokenNames)
    {
        if (headers.Contains(name))
        {
            var token = headers.GetHeader(name);
            if (!string.IsNullOrEmpty(token) && token.Length > 20)
            {
                ApplyCapturedToken(token);
            }
        }
    }
}
```

## Vue Input Setter

Element UI's v-model doesn't respond to `.value =`. Must use:

```javascript
(function() {
    var input = document.querySelector('.el-input__inner');
    if (input) {
        var setter = Object.getOwnPropertyDescriptor(
            window.HTMLInputElement.prototype, 'value').set;
        setter.call(input, 'YOUR_VALUE');
        input.dispatchEvent(new Event('input', { bubbles: true }));
        return true;
    }
    return false;
})()
```

## Form Submission

```javascript
// Click primary button
var btn = document.querySelector('.el-button--primary:not(.is-disabled)');
if (btn) btn.click();
```

## Wait for Response

```csharp
private async Task<string> WaitForResponseAsync(
    WebView2 webView,
    Func<string, bool> isTargetResponse,
    int timeoutMs = 10000)
{
    var tcs = new TaskCompletionSource<string>();
    
    EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs> handler = async (s, args) =>
    {
        if (args.Response.StatusCode == 200)
        {
            try
            {
                var stream = await args.Response.GetContentAsync();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                
                if (isTargetResponse(json))
                    tcs.TrySetResult(json);
            }
            catch { }
        }
    };
    
    webView.CoreWebView2.WebResourceResponseReceived += handler;
    
    try
    {
        var completed = await Task.WhenAny(
            tcs.Task, 
            Task.Delay(timeoutMs));
            
        if (completed != tcs.Task)
            throw new TimeoutException();
            
        return await tcs.Task;
    }
    finally
    {
        webView.CoreWebView2.WebResourceResponseReceived -= handler;
    }
}
```

## Selector Reference

| Element | Selector |
|---------|----------|
| Input field | `.el-input__inner` |
| Primary button | `.el-button--primary` |
| Submit button | `.el-button--primary:not(.is-disabled)` |
| Form | `.el-form` |
| Dialog | `.el-dialog` |
| Table | `.el-table` |
| Table row | `.el-table__row` |
| Checkbox | `.el-checkbox__input` |

## Error Handling

```csharp
try
{
    string result = await webView.ExecuteScriptAsync(script);
}
catch (Exception ex) when (ex.Message.Contains("0x80070057"))
{
    // Invalid arguments
    AppLogger.Warning("Script arguments invalid");
}
catch (Exception ex) when (ex.Message.Contains("No such interface"))
{
    // WebView2 not initialized
    await webView.EnsureCoreWebView2Async();
}
```

## Common Issues

| Issue | Cause | Fix |
|-------|--------|-----|
| Script fails silently | Exception swallowed | Add try-catch with logging |
| Vue input not set | Wrong setter | Use nativeInputValueSetter |
| 401 on all calls | Token not captured | Check header capture |
| Timeout | Network slow | Increase timeout |
