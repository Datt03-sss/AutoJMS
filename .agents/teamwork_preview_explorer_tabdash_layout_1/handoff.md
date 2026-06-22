# Handoff Report: FullStackOperation WebView2 Layout Investigation

## 1. Observation

In the investigation of why the WebView2 control (`_webView`) obscures/covers the top navigation tabs (Dashboard, Thời hiệu, CHATBOT) on the `FullStackOperation` form, we observed the following configuration across the partial class files:

*   **Definition & Instantiation**:
    In `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs` (lines 16-35):
    ```csharp
    private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
    ...
    private void BuildDashboardPageCodeFirst()
    {
        tabDash.SuspendLayout();
        try
        {
            ...
            _webView = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Dock = DockStyle.Fill
            };
            tabDash.Controls.Add(_webView);
    ```
    This adds `_webView` directly as a child of `tabDash` with `DockStyle.Fill`.

*   **Initialization Call in Constructor**:
    In `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs` (line 38):
    ```csharp
            LoadStarredWaybills();
            _ = InitializeWebView2Async();
    ```
    `BuildDashboardPageCodeFirst()` is executed as part of `BuildUiInCode()` inside the form's constructor:
    In `src/AutoJMS/Forms/FullStackOperation.cs` (line 97-100):
    ```csharp
    public FullStackOperation()
    {
        ConfigureFormShell();
        BuildUiInCode();
    ```
    And `BuildUiInCode()` calls `BuildDashboardPageCodeFirst()` before the tab pages are added to the tab control and before the tab control is added to the form:
    In `src/AutoJMS/Forms/FullStackOperation.Layout.cs` (lines 86-91):
    ```csharp
            BuildDashboardPageCodeFirst();
            BuildChatbotPageCodeFirst();

            uiTabControl1.TabPages.Add(tabDash);
            uiTabControl1.TabPages.Add(tabChat);
            Controls.Add(uiTabControl1);
    ```

*   **Asynchronous WebView2 Initialization**:
    In `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs` (lines 405-412):
    ```csharp
    private async System.Threading.Tasks.Task InitializeWebView2Async()
    {
        if (_webViewInitialized) return;
        try
        {
            var userDataFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppData", "BrowserData");
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
    ```

*   **Contrast: Chatbot WebView2 Lifecycle**:
    In `src/AutoJMS/Forms/FullStackOperation.cs` (lines 939-943), the chatbot's webview `tabChat_webViewZalo` is initialized only when the user selects the Chatbot tab:
    ```csharp
    private async void uiTabControl1_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (uiTabControl1.SelectedTab == tabChat)
        {
            await InitZaloWebViewAsync();
        }
    }
    ```

*   **Contrast: Main Form WebView2 Lifecycle**:
    In `src/AutoJMS/Forms/Main.cs` (lines 431-463), the main form forces handle creation by querying the `.Handle` property of the WebViews in `OnLoad`, ensuring handles are built before calling `EnsureCoreWebView2Async`:
    ```csharp
    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ...
        _ = tabHome_webView.Handle;
        _ = tabDKCH_webView.Handle;
        _ = tabPrint_printPreview.Handle;

        try
        {
            await tabHome_webView.EnsureCoreWebView2Async(null);
            await tabDKCH_webView.EnsureCoreWebView2Async(null);
            await tabPrint_printPreview.EnsureCoreWebView2Async(null);
    ```

---

## 2. Logic Chain

1.  **Premature HWND binding**: When `InitializeWebView2Async` calls `EnsureCoreWebView2Async` in the form's constructor, none of the controls (`_webView`, `tabDash`, `uiTabControl1`, or the form itself) have created Win32 window handles (`Handle` is `IntPtr.Zero`).
2.  **Parenting Failure**: Because WebView2 renders as a native Win32 child window, the WebView2 runtime requires a stable parent window handle (HWND) hierarchy to correctly anchor and clip the browser window. Since the handles do not exist yet, WebView2 fails to bind properly to the nested `tabDash` page.
3.  **Visual Obscuration**: When the Form is later loaded and the window handles are generated, the orphaned WebView2 window fails to scale/contain itself within the boundaries of `tabDash`. It gets drawn starting from the coordinate `(0, 0)` of the form's client area (or the tab control's client area) as a topmost native window.
4.  **Header Coverage**: As a result of this coordinate/parenting mismatch, the native browser window covers the entire top of the form, rendering over the tab headers drawn by `uiTabControl1` (Dashboard, Thời hiệu, CHATBOT) and blocking all tab switching interaction.
5.  **Validation via Chatbot and Main Form**: The Chatbot WebView2 (`tabChat_webViewZalo`) does not exhibit this bug because it is initialized on-demand via the `SelectedIndexChanged` event *after* the Form handle is fully created. Similarly, the main form (`Main.cs`) works correctly because it forces handle creation using `_ = control.Handle` during `OnLoad` before calling `EnsureCoreWebView2Async`.

---

## 3. Caveats

*   This investigation is purely read-only and does not modify the codebase.
*   We assume that the layout definition in `FullStackOperation.Layout.cs` is correct, and that `tabDash` itself is positioned and sized correctly by `uiTabControl1`.
*   We assume that Sunny.UI's `UITabControl` does not have an inherent bug preventing child controls from docking, as long as native HWND binding is performed correctly.

---

## 4. Conclusion

The layout issue where the WebView2 control obscures the top navigation headers is caused by **early initialization of the WebView2 environment in the constructor** before Win32 window handles are created. 

To fix this, the initialization of `_webView` must be deferred until the Form has loaded and its handle is created, mimicking the working implementation in `Main.cs`.

---

## 5. Verification Method

To verify the issue and validate the proposed fix:
1.  **Build and Run**: Run the application and open the "Điều phối Vận hành Bưu cục Realtime" form. Observe that the top navigation tabs are hidden/covered.
2.  **Apply Recommendations**:
    *   In `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs`, remove `_ = InitializeWebView2Async();` from `BuildDashboardPageCodeFirst()`.
    *   In `src/AutoJMS/Forms/FullStackOperation.cs`, add the following code at the start of `FullStackOperation_Load()`:
        ```csharp
        if (_webView != null)
        {
            _ = _webView.Handle;
            _ = InitializeWebView2Async();
        }
        ```
3.  **Run Build**: Compile the project to ensure no syntax/compilation issues:
    ```powershell
    dotnet restore .\AutoJMS.slnx
    dotnet build .\AutoJMS.slnx -c Release
    ```
4.  **Confirm Layout Fix**: Run the application and check if the top navigation tabs (Dashboard, Thời hiệu, CHATBOT) are now visible and selectable, and that the WebView2 control is constrained strictly inside the Dashboard tab page.
