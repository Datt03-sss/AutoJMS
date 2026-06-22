using Sunny.UI;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        // FULLSTACK UI IS CODE-FIRST.
        // Runtime layout is the source of truth; WinForms Designer is intentionally inert.
        private void ConfigureFormShell()
        {
            SuspendLayout();
            try
            {
                AutoScaleMode = AutoScaleMode.Dpi;
                BackColor = FullStackBackColor;
                ClientSize = new Size(1353, 767);
                ControlBox = true;
                Font = UiFont;
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = true;
                MinimizeBox = true;
                MinimumSize = new Size(1180, 680);
                Name = nameof(FullStackOperation);
                RectColor = HeaderDark;
                ShowIcon = false;
                ShowTitle = false;
                StartPosition = FormStartPosition.CenterScreen;
                Style = UIStyle.Custom;
                Text = "Điều phối Vận hành Bưu cục Realtime";
                TitleColor = HeaderDark;
                TitleFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                TitleForeColor = Color.White;
                Padding = new Padding(0);
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        private void BuildUiInCode()
        {
            SuspendLayout();
            try
            {
                Controls.Clear();

                _webView = new Microsoft.Web.WebView2.WinForms.WebView2
                {
                    Dock = DockStyle.Fill,
                    Name = "_webView"
                };
                Controls.Add(_webView);

                LoadStarredWaybills();
                _ = InitializeWebView2Async();
            }
            finally
            {
                ResumeLayout(true);
            }
        }
    }
}
