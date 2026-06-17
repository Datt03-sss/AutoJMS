# WebView2 Automation Rules

## Core Principle

**WebView2 can ONLY be accessed from the UI thread.** This is the most critical rule.

## UI Thread Enforcement

### Always Marshal to UI Thread

```csharp
// WRONG - will crash
Task.Run(async () => {
    var result = await webView.ExecuteScriptAsync(script);
});

// CORRECT - marshal to UI thread
if (webView.InvokeRequired)
{
    var result = await (Task<string>)webView.Invoke(
        new Func<Task<string>>(async () => await webView.ExecuteScriptAsync(script)));
}
```

### Helper Pattern (UiThread.cs)

Use the existing `UiThread` helper when available:

```csharp
await UiThread.InvokeOnUiAsync(this, async () => 
{
    await webView.EnsureCoreWebView2Async();
    await webView.ExecuteScriptAsync(script);
    return true;
});
```

## Script Execution

### ExecuteScriptAsync

1. Fire-and-forget scripts:
   ```csharp
   await webView.ExecuteScriptAsync("window.scrollTo(0, 0);");
   ```

2. Scripts that return values:
   ```csharp
   string result = await webView.ExecuteScriptAsync(jsCode);
   ```

3. Always wrap in try-catch:
   ```csharp
   try
   {
       string result = await webView.ExecuteScriptAsync(script);
   }
   catch (Exception ex)
   {
       AppLogger.Warning($"Script failed: {ex.Message}");
   }
   ```

### Waiting for Responses

Use `WaitForApiResponseAsync` pattern:

```csharp
private async Task<string> WaitForApiResponseAsync(
    WebView2 webView, 
    Func<string, bool> predicate, 
    int timeoutMs = 10000)
{
    var tcs = new TaskCompletionSource<string>();
    EventHandler<CoreWebView2WebResourceResponseReceivedEventArgs> handler = async (s, args) =>
    {
        if (args.Response.StatusCode == 200)
        {
            var stream = await args.Response.GetContentAsync();
            using var reader = new StreamReader(stream);
            string json = await reader.ReadToEndAsync();
            if (predicate(json))
                tcs.TrySetResult(json);
        }
    };
    
    webView.CoreWebView2.WebResourceResponseReceived += handler;
    
    try
    {
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        return completedTask == tcs.Task ? await tcs.Task : throw new TimeoutException();
    }
    finally
    {
        webView.CoreWebView2.WebResourceResponseReceived -= handler;
    }
}
```

## Selector Configuration

### Vue/Element UI Selectors

JMS frontend uses Vue.js + Element UI. Common selectors:

```javascript
// Element UI Input
document.querySelector('.el-input__inner')

// Button (primary)
document.querySelector('.el-button--primary')

// Form
document.querySelector('.el-form')

// Dialog
document.querySelector('.el-dialog')
```

### Setting Input Values (Vue-specific)

Vue's v-model doesn't respond to direct `.value` assignment. Must use:

```javascript
// WRONG - Vue won't see this
input.value = 'test';
input.dispatchEvent(new Event('input'));

// CORRECT - Vue sees this
const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
    window.HTMLInputElement.prototype, 'value'
).set;
nativeInputValueSetter.call(input, 'test');
input.dispatchEvent(new Event('input', { bubbles: true }));
```

### Form Submission

```javascript
// Click submit button
const btn = document.querySelector('.el-button--primary');
btn && btn.click();
```

## Request Interception

### WebResourceRequested Event

Capture authToken from request headers:

```csharp
webView.CoreWebView2.AddWebResourceRequestedFilter(
    "*", 
    CoreWebView2WebResourceContext.Fetch);

webView.CoreWebView2.AddWebResourceRequestedFilter(
    "*", 
    CoreWebView2WebResourceContext.XmlHttpRequest);

webView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

private void CoreWebView2_WebResourceRequested(object sender, 
    CoreWebView2WebResourceRequestedEventArgs e)
{
    // Probe headers for token
    string[] authHeaderNames = { "authToken", "YL_TOKEN", "token", "accessToken" };
    foreach (var name in authHeaderNames)
    {
        if (e.Request.Headers.Contains(name))
        {
            var token = e.Request.Headers.GetHeader(name);
            if (!string.IsNullOrEmpty(token))
            {
                ApplyCapturedToken(token, "WebView2 request header");
            }
        }
    }
}
```

## Error Handling

### Common Exceptions

1. **COMException (0x80070057)**: Invalid arguments
2. **InvalidOperationException**: WebView2 not initialized
3. **UnauthorizedAccessException**: Cross-thread access

### Timeout Handling

```csharp
try
{
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    string result = await webView.ExecuteScriptAsync(script)
        .WaitAsync(cts.Token);
}
catch (OperationCanceledException)
{
    AppLogger.Warning("Script timeout");
}
```

## Lifecycle Management

### Initialization

```csharp
webView.CreationProperties = new CoreWebView2CreationProperties
{
    UserDataFolder = sharedFolder
};

await webView.EnsureCoreWebView2Async(null);

webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;
webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
```

### Cleanup (Before Update)

```csharp
private void DisposeWebView(WebView2 wv)
{
    try
    {
        if (wv == null) return;
        wv.CoreWebView2?.Stop();
        wv.Dispose();
    }
    catch { }
}
```

## Testing WebView2 Automation

### Manual Testing Steps

1. Open app and navigate to JMS
2. Open DevTools (Ctrl+Shift+I in WebView)
3. Test selectors in console
4. Verify Vue input setter works
5. Test timeout scenarios

### Selector Verification

```javascript
// Check if element exists
document.querySelector('.el-input__inner') !== null

// Check input value setter
const input = document.querySelector('.el-input__inner');
input && console.log(input.value);
```

## Debugging Tips

1. **Check if WebView2 is initialized**
   ```csharp
   if (webView.CoreWebView2 == null)
   {
       await webView.EnsureCoreWebView2Async();
   }
   ```

2. **Log all script results**
   ```csharp
   string result = await webView.ExecuteScriptAsync(script);
   AppLogger.Info($"Script result: {result}");
   ```

3. **Check WebView2 version**
   ```csharp
   string version = webView.CoreWebView2.Environment.BrowserVersion;
   AppLogger.Info($"WebView2 version: {version}");
   ```

4. **Verify origin before reading localStorage**
   ```csharp
   private bool IsJmsOrigin(WebView2 wv)
   {
       string src = wv?.CoreWebView2?.Source;
       return src?.Contains("jtexpress.vn") ?? false;
   }
   ```
