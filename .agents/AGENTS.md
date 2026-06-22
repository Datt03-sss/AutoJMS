
## WinForms WebView2 UI Migration Rules
- **Preserve TabControl**: When replacing a specific tab (e.g., `tabDash`) with a WebView2 UI, the WebView2 control must be added **only** to the `Controls` collection of that specific `TabPage`.
- **Do not overlay the form**: Never add the WebView2 directly to the main `Form.Controls` or a `DockStyle.Fill` panel that obscures the main `TabControl`.
- **Keep native navigation**: The native WinForms `TabControl` (tabs like Dashboard, Thời hiệu, CHATBOT) must remain visible and functional. Set `WebView2.Dock = DockStyle.Fill` strictly inside the target `TabPage`.

