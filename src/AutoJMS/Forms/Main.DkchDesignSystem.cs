using Sunny.UI;
using AutoJMS.UI.DesignSystem;

// LƯU Ý: lớp Main nằm ở namespace "AutoJMS" (không phải "AutoJMS.Forms") dù file
// ở thư mục Forms/. Xem đầu Main.DkchData.cs.
namespace AutoJMS
{
    /// <summary>
    /// Tab CHUYỂN HOÀN (DKCH) — màn hình đầu tiên chạy design system mới.
    /// Xem DesignReference/AutoJMS.DESIGN.md và DesignReference/AutoJMS.UI.AUDIT.md.
    ///
    /// Phase 2: bốn nút DKCH giờ do Designer tạo THẲNG bằng <see cref="AButton"/>
    /// (tabDKCH_Home, tabDKCH_btnDKCH1, tabDKCH_btnDKCH2, tabDKCH_btnStop) nên lớp
    /// bọc "ẩn nút SunnyUI rồi chồng nút A* lên" của phase trước đã bỏ. Tên trường và
    /// handler giữ nguyên, vì vậy UpdateDkchButtonsByState, AlignLeftPanelControls và
    /// InitializeAppCaptureUserActions trong Main.cs chạy y như cũ.
    ///
    /// Ba khung DATA / CONTROL / NEWBILL cũng đã là <see cref="APanel"/>; chúng tự lấy
    /// màu từ ThemeManager nên không còn gì để tô ở đây. Còn lại đúng một việc:
    /// tabHome_pnlLeft vẫn là UIPanel của SunnyUI (dọn ở Phase 4).
    ///
    /// Màu ba nút mang NGHĨA NGHIỆP VỤ, không phải màu trang trí (DESIGN.md §B):
    /// DKCH1 = Success, DKCH2 = Warning, Dừng = Danger. Không đổi sang Primary.
    /// </summary>
    public partial class Main
    {
        private ThemeHook _dsDkchThemeHook;

        /// <summary>Dựng lớp giao diện mới cho panel trái DKCH. Gọi sau BuildDkchNewbillSection().</summary>
        private void BuildDkchDesignSystem()
        {
            ApplyDkchDesignSystemChrome();
            _dsDkchThemeHook ??= new ThemeHook(this, ApplyDkchDesignSystemChrome);
        }

        private void ApplyDkchDesignSystemChrome()
        {
            if (tabHome_pnlLeft == null) return;

            var c = ThemeManager.Current;
            tabHome_pnlLeft.Style = UIStyle.Custom;
            tabHome_pnlLeft.StyleCustomMode = true;
            tabHome_pnlLeft.FillColor = c.Surface;
            tabHome_pnlLeft.RectColor = c.Surface;
        }
    }
}
