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
                StartPosition = FormStartPosition.CenterScreen;
                Style = UIStyle.Custom;
                Text = "Điều phối Vận hành Bưu cục Realtime";
                TitleColor = HeaderDark;
                TitleFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
                TitleForeColor = Color.White;
                Padding = new Padding(1, 36, 1, 1);
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

                uiTabControl1 = new UITabControl
                {
                    Dock = DockStyle.Fill,
                    DrawMode = TabDrawMode.OwnerDrawFixed,
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    ItemSize = new Size(140, 34),
                    Margin = Padding.Empty,
                    Padding = new Point(0, 0),
                    SizeMode = TabSizeMode.Fixed,
                    MainPage = string.Empty,
                    MenuStyle = UIMenuStyle.Custom,
                    TabBackColor = HeaderDark,
                    TabSelectedColor = WorkspaceBackColor,
                    TabSelectedForeColor = TextPrimary,
                    TabSelectedHighColor = AccentBlue,
                    TabUnSelectedColor = HeaderDark,
                    TabUnSelectedForeColor = Color.FromArgb(240, 240, 240)
                };

                tabDash = new TabPage
                {
                    Name = "tabDash",
                    Text = "Dashboard",
                    BackColor = FullStackBackColor,
                    UseVisualStyleBackColor = false,
                    Padding = Padding.Empty
                };

                tabChat = new TabPage
                {
                    Name = "tabChat",
                    Text = "CHATBOT",
                    BackColor = FullStackBackColor,
                    UseVisualStyleBackColor = false,
                    Padding = Padding.Empty
                };

                BuildDashboardPageCodeFirst();
                BuildChatbotPageCodeFirst();

                uiTabControl1.TabPages.Add(tabDash);
                uiTabControl1.TabPages.Add(tabChat);
                Controls.Add(uiTabControl1);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private static UITableLayoutPanel CreateInlineLayout(int columnCount)
        {
            var layout = new UITableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columnCount,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            for (int i = 0; i < columnCount; i++)
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columnCount));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            return layout;
        }

        private static UIPanel CreatePlainPanel()
        {
            return new UIPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Padding = Padding.Empty,
                FillColor = PanelBackColor,
                RectColor = Color.FromArgb(225, 229, 235),
                Text = null,
                TextAlignment = ContentAlignment.MiddleCenter,
                MinimumSize = new Size(1, 1)
            };
        }

        private static UIComboBox CreateComboBox(string name)
        {
            return new UIComboBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(3),
                MinimumSize = new Size(1, 1),
                DropDownStyle = UIDropDownStyle.DropDownList
            };
        }

        private static UISymbolLabel CreateToolbarLabel(string text)
        {
            return new UISymbolLabel
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(70, 70, 70),
                Symbol = 61555,
                SymbolSize = 14,
                MinimumSize = new Size(1, 1)
            };
        }

        private static UILabel CreatePlainLabel(string text)
        {
            return new UILabel
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiBoldFont,
                ForeColor = Color.FromArgb(70, 70, 70),
                MinimumSize = new Size(1, 1)
            };
        }

        private static UILabel CreateMetricText(string text)
        {
            return new UILabel
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(70, 70, 70),
                MinimumSize = new Size(1, 1)
            };
        }

        private static UIDataGridView CreateGrid(string name)
        {
            return new UIDataGridView
            {
                Name = name,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                EnableHeadersVisualStyles = false,
                Font = new Font("Segoe UI", 9F),
                GridColor = Color.FromArgb(210, 215, 225),
                RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                SelectedIndex = -1,
                StripeOddColor = Color.White,
                StripeEvenColor = Color.White
            };
        }
    }
}
