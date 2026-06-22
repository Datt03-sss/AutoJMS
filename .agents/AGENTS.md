
### UI Architecture Rule (FullStackOperation)
- **WebView2 Single-Page Host:** The "FullStackOperation" form must act exclusively as a full-screen host for the WebView2 control.
- **No Native Tabs:** Do not use "UITabControl" or native WinForms tabs (like Dashboard, Thời hiệu, CHATBOT) in "FullStackOperation". The WebView2 control should be docked ("DockStyle.Fill") directly to the form's "Controls" collection.
- **Navigation:** Any multi-page navigation (e.g., switching between Dashboard and Chatbot) must be handled inside the web application via HTML/JS, not through native WinForms controls.
