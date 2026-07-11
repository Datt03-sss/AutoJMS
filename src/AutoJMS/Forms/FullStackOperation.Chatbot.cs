using Microsoft.Web.WebView2.WinForms;
using Sunny.UI;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private void BuildChatbotPageCodeFirst()
        {
            tabChat.SuspendLayout();
            try
            {
                uiTableLayoutPanel3 = new UITableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = Padding.Empty,
                    Padding = new Padding(5)
                };
                uiTableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430F));
                uiTableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                uiTableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                tabChat_leftPanel = new UITableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 4,
                    Margin = new Padding(0, 0, 5, 0),
                    Padding = Padding.Empty
                };
                tabChat_leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
                tabChat_leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
                tabChat_leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
                tabChat_leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

                uiPanel4 = CreatePlainPanel();
                uiPanel4.Padding = new Padding(8, 5, 8, 5);
                tabChat_userAvatar = new UIAvatar
                {
                    Dock = DockStyle.Left,
                    Width = 42,
                    Text = "Z",
                    Font = UiBoldFont
                };
                tabChat_userName = new UILinkLabel
                {
                    Dock = DockStyle.Fill,
                    Text = "Zalo Chatbot",
                    Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                uiPanel4.Controls.Add(tabChat_userName);
                uiPanel4.Controls.Add(tabChat_userAvatar);

                uiPanel15 = CreatePlainPanel();
                uiPanel15.Padding = new Padding(6);
                uiTableLayoutPanel17 = CreateInlineLayout(3);
                uiTableLayoutPanel17.ColumnStyles.Clear();
                uiTableLayoutPanel17.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
                uiTableLayoutPanel17.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                uiTableLayoutPanel17.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95F));
                uiLabel5 = CreatePlainLabel("Lọc");
                tabChat_statusSelect = CreateComboBox("tabChat_statusSelect");
                tabChat_statusSelect.Items.Add("Tất cả");
                tabChat_statusSelect.SelectedIndex = 0;
                tabChat_btnReload = new UISymbolButton
                {
                    Dock = DockStyle.Fill,
                    Text = "Làm mới",
                    Symbol = 61473,
                    SymbolSize = 14,
                    Radius = 6,
                    Font = new Font("Segoe UI", 8.5F),
                    MinimumSize = new Size(1, 1)
                };
                uiTableLayoutPanel17.Controls.Add(uiLabel5, 0, 0);
                uiTableLayoutPanel17.Controls.Add(tabChat_statusSelect, 1, 0);
                uiTableLayoutPanel17.Controls.Add(tabChat_btnReload, 2, 0);
                uiPanel15.Controls.Add(uiTableLayoutPanel17);

                uiPanel6 = CreatePlainPanel();
                uiPanel6.Padding = new Padding(6);
                uiTableLayoutPanel19 = CreateInlineLayout(2);
                tabChat_btnStart = new UISymbolButton
                {
                    Dock = DockStyle.Fill,
                    Text = "Bắt đầu nhắc",
                    Symbol = 61973,
                    SymbolSize = 16,
                    Radius = 6,
                    FillColor = AccentGreen,
                    FillHoverColor = Color.FromArgb(0, 175, 110),
                    Font = UiBoldFont,
                    MinimumSize = new Size(1, 1)
                };
                uiTableLayoutPanel20 = CreateInlineLayout(2);
                uiLabel3 = CreatePlainLabel("Chu kỳ");
                tabChat_timeSelect = CreateComboBox("tabChat_timeSelect");
                tabChat_timeSelect.Items.AddRange(new object[] { "30 PHÚT", "1 GIỜ", "2 GIỜ" });
                tabChat_timeSelect.SelectedIndex = 0;
                uiTableLayoutPanel20.Controls.Add(uiLabel3, 0, 0);
                uiTableLayoutPanel20.Controls.Add(tabChat_timeSelect, 1, 0);
                uiTableLayoutPanel19.Controls.Add(tabChat_btnStart, 0, 0);
                uiTableLayoutPanel19.Controls.Add(uiTableLayoutPanel20, 1, 0);
                uiPanel6.Controls.Add(uiTableLayoutPanel19);

                uiPanel7 = CreatePlainPanel();
                uiPanel7.Padding = new Padding(6);
                uiTableLayoutPanel16 = new UITableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 3,
                    RowCount = 1,
                    Margin = Padding.Empty
                };
                for (int i = 0; i < 3; i++)
                    uiTableLayoutPanel16.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
                tabChat_sumFollow = CreateMetricText("Theo dõi: 0");
                tabChat_hasKVD = CreateMetricText("KVD: 0");
                tabChat_hasXNCH = CreateMetricText("XNCH: 0");
                uiTableLayoutPanel16.Controls.Add(tabChat_sumFollow, 0, 0);
                uiTableLayoutPanel16.Controls.Add(tabChat_hasKVD, 1, 0);
                uiTableLayoutPanel16.Controls.Add(tabChat_hasXNCH, 2, 0);
                uiPanel7.Controls.Add(uiTableLayoutPanel16);

                uiPanel5 = CreatePlainPanel();
                uiPanel5.Padding = new Padding(5);
                tabChat_dataGrid = CreateGrid("tabChat_dataGrid");
                uiPanel5.Controls.Add(tabChat_dataGrid);

                tabChat_leftPanel.Controls.Add(uiPanel4, 0, 0);
                tabChat_leftPanel.Controls.Add(uiPanel15, 0, 1);
                tabChat_leftPanel.Controls.Add(uiPanel6, 0, 2);
                tabChat_leftPanel.Controls.Add(uiPanel5, 0, 3);

                tabChat_webViewZalo = new WebView2
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty,
                    DefaultBackgroundColor = Color.White
                };

                uiTableLayoutPanel3.Controls.Add(tabChat_leftPanel, 0, 0);
                uiTableLayoutPanel3.Controls.Add(tabChat_webViewZalo, 1, 0);
                tabChat.Controls.Add(uiTableLayoutPanel3);
            }
            finally
            {
                tabChat.ResumeLayout(false);
            }
        }
    }
}
