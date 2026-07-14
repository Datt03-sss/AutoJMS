
## WinForms WebView2 UI Migration Rules
- **Preserve TabControl**: When replacing a specific tab (e.g., `tabDash`) with a WebView2 UI, the WebView2 control must be added **only** to the `Controls` collection of that specific `TabPage`.
- **Do not overlay the form**: Never add the WebView2 directly to the main `Form.Controls` or a `DockStyle.Fill` panel that obscures the main `TabControl`.
- **Keep native navigation**: The native WinForms `TabControl` (tabs like Dashboard, Thời hiệu, CHATBOT) must remain visible and functional. Set `WebView2.Dock = DockStyle.Fill` strictly inside the target `TabPage`.

- **Preserve Native Window Hierarchy**: When a web design mockup includes a "fake" desktop application header (title bar, window controls, logo), **DO NOT** embed this header into the WebView2. 
- Instead, **remove the fake header** from the HTML/CSS.
- **Style the Native TitleBar**: Update the native WinForms/SunnyUI main form properties (TitleColor, TitleForeColor, etc.) to match the aesthetics of the web design's header.
- **Preserve TabControl**: The native WinForms TabControl must remain below the native TitleBar and above the WebView2 control. Never overlay or obscure the TabControl.
