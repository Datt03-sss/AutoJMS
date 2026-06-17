#nullable enable
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoJMS.Diagnostics.AppCapture
{
    public sealed class UserActionCaptureService
    {
        private readonly IAppCaptureManager _capture;

        public UserActionCaptureService(IAppCaptureManager capture)
        {
            _capture = capture ?? AppCaptureManager.Instance;
        }

        public void CaptureFormShown(Form form, string source)
        {
            if (!_capture.IsEnabled || form == null) return;
            form.Shown += (_, _) => Record(source, "FormShown", form.Name, form.Text);
            form.FormClosed += (_, _) => Record(source, "FormClosed", form.Name, form.Text);
        }

        public void CaptureTabControl(TabControl tabControl, string source)
        {
            if (!_capture.IsEnabled || tabControl == null) return;
            tabControl.SelectedIndexChanged += (_, _) =>
            {
                var tab = tabControl.SelectedTab;
                Record(source, "TabSelected", tab?.Name ?? "", tab?.Text ?? "");
            };
        }

        public void CaptureButton(Control control, string source, string actionName)
        {
            if (!_capture.IsEnabled || control == null) return;
            control.Click += (_, _) => Record(source, actionName, control.Name, control.Text);
        }

        public void CaptureTextEnter(Control control, string source, string actionName, Func<string>? valueProvider = null)
        {
            if (!_capture.IsEnabled || control == null) return;
            control.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                string value = valueProvider?.Invoke() ?? GetText(control);
                Record(source, actionName, control.Name, "", value);
            };
        }

        public void CaptureGridDoubleClick(DataGridView grid, string source, string actionName, Func<string>? valueProvider = null)
        {
            if (!_capture.IsEnabled || grid == null) return;
            grid.CellDoubleClick += (_, _) =>
            {
                string value = valueProvider?.Invoke() ?? "";
                Record(source, actionName, grid.Name, "", value);
            };
        }

        public void Record(string source, string eventName, string controlName, string text = "", string value = "")
        {
            if (!_capture.IsEnabled) return;
            _capture.RecordEvent(new AppCaptureEvent
            {
                Category = "user.action",
                Source = source,
                EventName = eventName,
                WaybillNo = LooksLikeWaybillField(controlName) ? AppCaptureRedactor.RedactText(value) : value,
                Data = new Dictionary<string, object?>
                {
                    ["controlName"] = controlName,
                    ["text"] = AppCaptureRedactor.RedactText(text),
                    ["value"] = ShouldRedactControl(controlName) ? "<redacted>" : AppCaptureRedactor.RedactText(value)
                }
            });
        }

        private static string GetText(Control control)
        {
            try { return control.Text ?? ""; }
            catch { return ""; }
        }

        private static bool ShouldRedactControl(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName)) return false;
            return controlName.Contains("password", StringComparison.OrdinalIgnoreCase)
                || controlName.Contains("token", StringComparison.OrdinalIgnoreCase)
                || controlName.Contains("license", StringComparison.OrdinalIgnoreCase)
                || controlName.Contains("key", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikeWaybillField(string controlName)
        {
            if (string.IsNullOrWhiteSpace(controlName)) return false;
            return controlName.Contains("waybill", StringComparison.OrdinalIgnoreCase)
                || controlName.Contains("bill", StringComparison.OrdinalIgnoreCase);
        }
    }
}

