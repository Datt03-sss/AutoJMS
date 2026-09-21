using System;
using System.Drawing;
using System.Windows.Forms;
using AutoJMS.UI.DesignSystem;
using Sunny.UI;

// LƯU Ý: lớp Main nằm ở namespace "AutoJMS" (không phải "AutoJMS.Forms") dù file
// ở thư mục Forms/. Xem đầu Main.DkchData.cs.
namespace AutoJMS
{
    /// <summary>
    /// Tab CHUYỂN HOÀN (DKCH) — màn hình đầu tiên chạy design system mới.
    /// Xem DesignReference/AutoJMS.DESIGN.md và DesignReference/AutoJMS.UI.AUDIT.md.
    ///
    /// CÁCH DI TRÚ Ở ĐÂY (đọc trước khi sửa):
    ///
    ///  - KHÔNG xoá control SunnyUI nào. Bốn nút cũ vẫn do Designer tạo, vẫn giữ
    ///    nguyên handler, chỉ bị ẩn. Tắt cờ <see cref="DkchDesignSystemEnabled"/>
    ///    là quay về đúng giao diện cũ, không cần revert commit.
    ///  - KHÔNG viết lại logic nghiệp vụ. Nút A* nối THẲNG vào chính các handler cũ
    ///    (tabDKCH_btnDKCH1_Click, ...) nên luồng chạy DKCH không đổi một dòng nào.
    ///  - Lần thay panel trái trước đây đã bị Chủ dự án yêu cầu trả lại
    ///    (xem Main.DkchLeftPanel.cs). Lần này khác ở chỗ bản cũ vẫn nằm nguyên
    ///    trong file và bật lại được bằng một hằng số.
    ///
    /// Màu ba nút mang NGHĨA NGHIỆP VỤ, không phải màu trang trí (DESIGN.md §B):
    /// DKCH1 = Success, DKCH2 = Warning, Dừng = Danger. Không đổi sang Primary.
    /// </summary>
    public partial class Main
    {
        /// <summary>Công tắc DUY NHẤT của bản giao diện mới cho tab DKCH.</summary>
        private static readonly bool DkchDesignSystemEnabled = true;

        private AButton _dsDkchHome;
        private AButton _dsDkch1;
        private AButton _dsDkch2;
        private AButton _dsDkchStop;
        private ThemeHook _dsDkchThemeHook;

        /// <summary>Dựng lớp giao diện mới cho panel trái DKCH. Gọi sau BuildDkchNewbillSection().</summary>
        private void BuildDkchDesignSystem()
        {
            if (!DkchDesignSystemEnabled) return;
            if (uiTableLayoutPanel9 == null || uiTableLayoutPanel10 == null || uiPanel1 == null) return;

            _dsDkchHome = NewDkchButton("Home", AButtonVariant.Ghost, btn_Refresh_Click);
            _dsDkchHome.Symbol = 61461;   // fa-refresh, giữ đúng icon cũ của tabDKCH_Home

            _dsDkch1 = NewDkchButton("DKCH1", AButtonVariant.Success, tabDKCH_btnDKCH1_Click);
            _dsDkch2 = NewDkchButton("DKCH2", AButtonVariant.Warning, tabDKCH_btnDKCH2_Click);
            _dsDkchStop = NewDkchButton("Dừng", AButtonVariant.Danger, tabDKCH_btnStop_Click);

            // Bản SunnyUI: ẩn, KHÔNG xoá. Handler vẫn gắn trên chúng cho trường hợp tắt cờ.
            SetVisible(tabDKCH_Home, false);
            SetVisible(tabDKCH_btnDKCH1, false);
            SetVisible(tabDKCH_btnDKCH2, false);
            SetVisible(tabDKCH_btnStop, false);

            // Vào đúng ô cũ để bố cục TableLayoutPanel không phải tính lại.
            uiTableLayoutPanel9.Controls.Add(_dsDkchHome, 0, 0);
            uiTableLayoutPanel10.Controls.Add(_dsDkch1, 0, 0);
            uiTableLayoutPanel10.Controls.Add(_dsDkch2, 0, 1);

            // Nút Dừng phủ kín uiPanel1 y như bản cũ và chỉ hiện khi đang chạy.
            _dsDkchStop.Dock = DockStyle.Fill;
            _dsDkchStop.Visible = false;
            uiPanel1.Controls.Add(_dsDkchStop);

            ApplyDkchDesignSystemChrome();
            _dsDkchThemeHook ??= new ThemeHook(this, ApplyDkchDesignSystemChrome);
        }

        private AButton NewDkchButton(string text, AButtonVariant variant, EventHandler onClick)
        {
            var button = new AButton
            {
                Text = text,
                Variant = variant,
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 4, 3, 4),
                Radius = ThemeRadius.Sm
            };
            button.Click += onClick;   // CHÍNH handler cũ — không có lớp trung gian nào
            return button;
        }

        private static void SetVisible(Control control, bool visible)
        {
            if (control != null) control.Visible = visible;
        }

        /// <summary>
        /// Ba khung "DATA" / "CONTROL" / "NEWBILL" vẫn là UITitlePanel của SunnyUI —
        /// chỉ đổi màu sang token. Thay hẳn bằng ACard sẽ phải dựng lại toàn bộ nội dung
        /// bên trong, đúng thứ lần trước bị trả lại.
        /// </summary>
        private void ApplyDkchDesignSystemChrome()
        {
            if (!DkchDesignSystemEnabled) return;

            var c = ThemeManager.Current;
            StyleDkchSection(tabDKCH_dataSrc, c);
            StyleDkchSection(uiTitlePanel1, c);
            StyleDkchSection(uiTitlePanel2, c);

            if (tabHome_pnlLeft != null)
            {
                tabHome_pnlLeft.Style = UIStyle.Custom;
                tabHome_pnlLeft.StyleCustomMode = true;
                tabHome_pnlLeft.FillColor = c.Surface;
                tabHome_pnlLeft.RectColor = c.Surface;
            }
        }

        private static void StyleDkchSection(UITitlePanel panel, ThemeColors c)
        {
            if (panel == null) return;

            panel.Style = UIStyle.Custom;
            panel.StyleCustomMode = true;
            panel.FillColor = c.Surface;
            panel.RectColor = c.Border;
            panel.ForeColor = c.Text;
            panel.TitleColor = c.SurfaceAlt;     // tiêu đề mục KHÔNG tô accent (DESIGN.md §Z)
            panel.TitleForeColor = c.TextSecondary;
        }

        /// <summary>
        /// Gọi ở CUỐI UpdateDkchButtonsByState: chạy sau phần gán cho bản SunnyUI nên
        /// giữ được chúng ẩn, đồng thời cho nút A* đúng trạng thái đang chạy / đang rảnh.
        /// </summary>
        private void SyncDkchDesignSystem(bool isRunning)
        {
            if (!DkchDesignSystemEnabled || _dsDkch1 == null) return;

            SetVisible(tabDKCH_Home, false);
            SetVisible(tabDKCH_btnDKCH1, false);
            SetVisible(tabDKCH_btnDKCH2, false);
            SetVisible(tabDKCH_btnStop, false);

            _dsDkch1.Visible = !isRunning;
            _dsDkch2.Visible = !isRunning;
            _dsDkch1.Enabled = !isRunning;
            _dsDkch2.Enabled = !isRunning;

            _dsDkchStop.Visible = isRunning;
            _dsDkchStop.Enabled = isRunning;
            if (isRunning) _dsDkchStop.BringToFront();
        }

        /// <summary>
        /// Handler cũ giờ chạy từ nút A*, nên AppCapture phải bám vào nút A*.
        /// Gọi trong InitializeAppCaptureUserActions().
        /// </summary>
        private void CaptureDkchDesignSystemButtons()
        {
            if (!DkchDesignSystemEnabled || _dsDkch1 == null) return;

            _appUserActionCapture.CaptureButton(_dsDkch1, "tabDKCH", "DKCH1.Click");
            _appUserActionCapture.CaptureButton(_dsDkch2, "tabDKCH", "DKCH2.Click");
            _appUserActionCapture.CaptureButton(_dsDkchStop, "tabDKCH", "Stop.Click");
        }
    }
}
