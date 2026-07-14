# tabDash Layout Explorer Handoff Report

## 1. Observation
- In `src/AutoJMS/Web/index.html` (lines 24-36), the fake HTML top bar is defined as:
  ```html
  <!-- ===== TOP BAR ===== -->
  <div style="height: 56px; flex: none; background: #11243f; display: flex; align-items: center; padding: 0 18px; color: #fff;">
    <div style="display: flex; align-items: center; gap: 9px;">
      <svg width="22" height="22" viewBox="0 0 24 24" fill="none"><path d="M12 3 L21 19 H3 Z" fill="#4f9bff"/><path d="M12 9 L16.5 17 H7.5 Z" fill="#11243f"/></svg>
      <span style="font-size: 17px; font-weight: 700; letter-spacing: .3px;">AutoJMS</span>
    </div>
    <span style="margin-left: 16px; font-size: 14.5px; font-weight: 500; color: #e6ecf5;">Điều phối Vận hành Bưu cục Realtime</span>
    <div style="margin-left: auto; display: flex; align-items: center; gap: 22px; color: #9fb0c8;">
      <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="5" y1="12" x2="19" y2="12"/></svg>
      <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="6" y1="6" x2="18" y2="18"/><line x1="18" y1="6" x2="6" y2="18"/></svg>
    </div>
  </div>
  ```
- In `src/AutoJMS/Forms/FullStackOperation.Layout.cs` (lines 30-33), the native form shell is styled:
  ```csharp
  Text = "Điều phối Vận hành Bưu cục Realtime";
  TitleColor = HeaderDark; // HeaderDark is Color.FromArgb(17, 36, 63) (#11243f)
  TitleFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
  TitleForeColor = Color.White;
  ```
- In `src/AutoJMS/Forms/FullStackOperation.Theme.cs` (lines 15-16), the colors are:
  ```csharp
  private static readonly Color HeaderDark = Color.FromArgb(17, 36, 63); // #11243f
  private static readonly Color FullStackBackColor = Color.FromArgb(244, 246, 249); // #f4f6f9
  ```
- In `src/AutoJMS/Forms/FullStackOperation.Layout.cs` (lines 49-92), the tab control `uiTabControl1` is instantiated and `tabDash` (TabPage) is added to it, then `uiTabControl1` is added to the form controls:
  ```csharp
  uiTabControl1 = new UITabControl { Dock = DockStyle.Fill, ... };
  tabDash = new TabPage { Name = "tabDash", Text = "Dashboard", ... };
  ...
  uiTabControl1.TabPages.Add(tabDash);
  Controls.Add(uiTabControl1);
  ```
- In `src/AutoJMS/Forms/FullStackOperation.Dashboard.cs` (lines 31-35), the WebView2 control is instantiated and added inside `tabDash`:
  ```csharp
  _webView = new Microsoft.Web.WebView2.WinForms.WebView2
  {
      Dock = DockStyle.Fill
  };
  tabDash.Controls.Add(_webView);
  ```

## 2. Logic Chain
1. To remove the duplicate top bar, the fake HTML header should be hidden or deleted. Modifying `src/AutoJMS/Web/index.html` to hide/delete lines 24-36 is sufficient to remove the HTML title bar.
2. The HTML top bar background uses `#11243f` and text color is `#fff`. The native form title bar in C# is styled with `TitleColor = HeaderDark` (`#11243f`) and `TitleForeColor = Color.White`. Thus, the native form title bar already matches the HTML design colors perfectly.
3. To align the titles completely, the C# form title text (defined in `FullStackOperation.Layout.cs` line 30) should be changed from `"Điều phối Vận hành Bưu cục Realtime"` to `"AutoJMS - Điều phối Vận hành Bưu cục Realtime"`.
4. Adding `_webView` inside `tabDash` with `DockStyle.Fill` ensures it takes up the entire area of the TabPage dashboard without overlapping or obscuring the native tab header control or other tabs (e.g. `tabChat`).

## 3. Caveats
- No code was modified in this phase (read-only investigation).
- `index.html` uses inline styles and does not support dynamic dark theme. The current custom style of `FullStackOperation` matches the HTML light/hybrid layout, which is suitable as long as `index.html` does not introduce dynamic styling.

## 4. Conclusion
- Hide the fake HTML top bar by deleting or setting style `display: none;` on the div at line 25 of `src/AutoJMS/Web/index.html`.
- Change C# form shell text to `"AutoJMS - Điều phối Vận hành Bưu cục Realtime"` in `FullStackOperation.Layout.cs`.
- The native tab control setup and WebView2 docking in `FullStackOperation.Dashboard.cs` are correct and fully adhere to UI migration guidelines.

## 5. Verification Method
- Perform compilation verification by running:
  `dotnet build .\AutoJMS.slnx -c Release`
- Run Inno Setup or run the application and execute the `DASH` command to inspect that the native title bar is visible, styled dark navy, matches the HTML content, and that the fake HTML top bar is gone.
