using AutoJMS.UI.DesignSystem;
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
                ShowIcon = false;
                StartPosition = FormStartPosition.CenterScreen;
                Text = "AutoJMS - Điều phối Vận hành Bưu cục Realtime";
                // RectColor/Style/Title*/Padding bỏ hết: đó là thanh tiêu đề GIẢ mà UIForm
                // vẽ bên trong vùng client, và Padding(1,36,1,1) chính là chỗ chừa cho nó.
                // Form thường dùng thanh tiêu đề thật của Windows, nằm ngoài vùng client.
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

                // ATabControl giấu hẳn dải tab của Windows, nên đầu tab do TopNavigation vẽ.
                // Panel bọc ngoài là bắt buộc: control thêm SAU được dock TRƯỚC, nên grid phải
                // vào trước rồi mới tới thanh nav thì nav mới nằm trên đỉnh.
                uiTabControl1 = new ATabControl
                {
                    Dock = DockStyle.Fill,
                    Margin = Padding.Empty
                };

                uiTabControl1Strip = new TopNavigation
                {
                    Dock = DockStyle.Top,
                    ShowIdentity = false,
                    Target = uiTabControl1
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

                var shell = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
                shell.Controls.Add(uiTabControl1);
                shell.Controls.Add(uiTabControl1Strip);
                Controls.Add(shell);
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private static TableLayoutPanel CreateInlineLayout(int columnCount)
        {
            var layout = new TableLayoutPanel
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

        private static APanel CreatePlainPanel()
        {
            // APanel đã sẵn Elevation.Flat = nền surface + viền hairline, đúng cặp
            // FillColor/RectColor mà UIPanel phải đặt tay. Text/TextAlignment bỏ vì
            // Panel thường không tự vẽ chữ — và ở đây Text vốn đã là null.
            return new APanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                Padding = Padding.Empty,
                MinimumSize = new Size(1, 1)
            };
        }

        private static AComboBox CreateComboBox(string name)
        {
            // DropDownStyle bỏ: AComboBox chỉ có danh sách chọn, không cho gõ tay —
            // đúng nghĩa DropDownList vốn đặt ở đây.
            return new AComboBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(3),
                MinimumSize = new Size(1, 1)
            };
        }

        private static Label CreateToolbarLabel(string text)
        {
            // Symbol/SymbolSize bỏ: đây là nhãn tĩnh, biểu tượng đồng hồ 61555 chỉ là
            // trang trí. Label thường vẽ chữ rẻ hơn một control tự vẽ.
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(70, 70, 70),
                MinimumSize = new Size(1, 1)
            };
        }

        private static Label CreatePlainLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiBoldFont,
                ForeColor = Color.FromArgb(70, 70, 70),
                MinimumSize = new Size(1, 1)
            };
        }

        private static Label CreateMetricText(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(70, 70, 70),
                MinimumSize = new Size(1, 1)
            };
        }

        private static DataGridView CreateGrid(string name)
        {
            // DataGridView gốc chứ không phải ADataGridView: StyleFullStackGrid đặt tay
            // một bảng màu TỐI cho mọi ô, mà ThemeHook của ADataGridView sẽ tô đè lại
            // bằng màu sáng của theme mỗi lần đổi theme.
            return new DataGridView
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
                RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
                // SelectedIndex/StripeOddColor/StripeEvenColor bỏ: cả ba là của SunnyUI.
                // SelectedIndex = -1 chạy lúc bảng chưa có dòng nào nên vốn đã vô tác dụng;
                // hai màu sọc đều là White = không sọc, mà StyleFullStackGrid đặt lại ngay
                // sau đó bằng AlternatingRowsDefaultCellStyle.
            };
        }
    }
}
