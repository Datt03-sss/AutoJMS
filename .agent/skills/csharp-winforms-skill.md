# C# WinForms Skill

## Overview

AutoJMS is a .NET 8 WinForms application using SunnyUI library.

## Project Structure

```csharp
// Entry point - Program.cs
static class Program
{
    [STAThread]
    static void Main()
    {
        VelopackApp.Build().Run();
        // ... initialization
        Application.Run(new Main(sessionTier));
    }
}
```

## WinForms Conventions

### Form Lifecycle

```csharp
public partial class Main : UIForm
{
    public Main(string tier)
    {
        InitializeComponent();
        // Constructor - minimal work
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Initialization that needs controls created
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Post-show operations (e.g., pre-create forms)
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Cleanup
        base.OnFormClosing(e);
    }
}
```

### UI Thread Access

```csharp
// CORRECT: Marshal to UI thread
if (this.InvokeRequired)
{
    this.Invoke(new Action(() => UpdateUI()));
}

// CORRECT: Async marshal
if (this.InvokeRequired)
{
    await (Task)this.Invoke(new Func<Task>(async () => await AsyncWork()));
}
```

### Timer Usage

```csharp
// Windows Forms Timer (UI thread)
var timer = new System.Windows.Forms.Timer();
timer.Interval = 1000;
timer.Tick += (s, e) => DoWork();
timer.Start();

// Cleanup
timer.Stop();
timer.Dispose();
```

### DataGridView

```csharp
// Double buffering for performance
var prop = grid.GetType().GetProperty(
    "DoubleBuffered",
    BindingFlags.Instance | BindingFlags.NonPublic);
prop?.SetValue(grid, true, null);

// Standard settings
grid.ReadOnly = true;
grid.AllowUserToAddRows = false;
grid.AllowUserToDeleteRows = false;
grid.MultiSelect = true;
grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
```

## SunnyUI Components

```csharp
// SunnyUI form base
public partial class Main : Sunny.UI.UIForm { }

// SunnyUI DataGridView
var grid = new Sunny.UI.UIDataGridView();

// SunnyUI button
var btn = new Sunny.UI.UIButton();

// SunnyUI message tip
Sunny.UI.UIMessageTip.ShowInfo("Message");
Sunny.UI.UIMessageTip.ShowWarning("Warning");
Sunny.UI.UIMessageTip.ShowError("Error");
```

## WebView2 Integration

```csharp
// Initialize
await webView.EnsureCoreWebView2Async();
webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = true;

// Navigate
webView.CoreWebView2.Navigate(url);

// Execute script (UI thread only!)
string result = await webView.ExecuteScriptAsync("document.title");

// Add request filter
webView.CoreWebView2.AddWebResourceRequestedFilter(
    "*",
    CoreWebView2WebResourceContext.Fetch);
```

## Async Patterns

```csharp
// Good: async void event handler
private async void btn_Click(object sender, EventArgs e)
{
    try
    {
        btn.Enabled = false;
        await DoWorkAsync();
    }
    finally
    {
        btn.Enabled = true;
    }
}

// Good: CancellationToken
private async Task DoWorkAsync(CancellationToken ct)
{
    using var client = new HttpClient();
    var response = await client.GetAsync(url, ct);
    var content = await response.Content.ReadAsStringAsync(ct);
}
```

## Resource Management

```csharp
// Dispose pattern
public class MyForm : Form, IDisposable
{
    private bool _disposed;

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _timer?.Stop();
                _timer?.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
```

## Logging

```csharp
// Use AppLogger
AppLogger.Info("Information message");
AppLogger.Warning("Warning message");
AppLogger.Error("Error occurred", exception);
AppLogger.Action("User action");
```
