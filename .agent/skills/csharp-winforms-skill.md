# C# WinForms Skill

## Overview

AutoJMS is a .NET 8 WinForms application built on its own design system,
`AutoJMS.UI.DesignSystem` (prefix `A`). No third-party UI control library.

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

## Design System A* Components

SunnyUI was removed in full (Phase 4, 2026-09-25). Use `AutoJMS.UI.DesignSystem`;
the full control list and its traps are in
[.agent/rules/01-csharp-winforms-rules.md](../rules/01-csharp-winforms-rules.md).

```csharp
using AutoJMS.UI.DesignSystem;

// Form base: plain WinForms Form. Main (AppTitleBar) and ADialog are borderless and paint their
// own title; FullStackOperation, frmLogin, TermsDialog, UpdateChannelDialog keep the Windows one.
public partial class Main : Form { }

// Grid
var grid = new ADataGridView();

// Button — Variant drives fill/hover/press/border from theme tokens
var btn = new AButton { Text = "IN", Variant = AButtonVariant.Primary, Symbol = ASymbols.Print };

// Non-blocking toast (ToolTip-based, 3s, does not pump the message loop)
AToast.Show(this, "Message");
AToast.Warning(this, "Warning");
AToast.Error(this, "Error");

// Modal
AMessageDialog.Show(this, "Message", "Thông báo");
bool ok = AConfirmDialog.Confirm(this, "Xoá bản ghi?", "Xác nhận", "Xoá", "Huỷ", destructive: true);
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
