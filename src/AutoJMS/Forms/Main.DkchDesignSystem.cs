// LƯU Ý: lớp Main nằm ở namespace "AutoJMS" (không phải "AutoJMS.Forms") dù file
// ở thư mục Forms/. Xem đầu Main.DkchData.cs.
namespace AutoJMS
{
    /// <summary>
    /// Tab CHUYỂN HOÀN (DKCH) — màn hình đầu tiên chạy design system mới.
    /// Xem DesignReference/AutoJMS.DESIGN.md và DesignReference/AutoJMS.UI.AUDIT.md.
    ///
    /// Phase 4: Mọi control trên panel trái DKCH giờ là A* (AButton, APanel). Chúng
    /// tự lấy màu từ ThemeManager nên không còn hàm styling thủ công.
    ///
    /// Giữ lại BuildDkchDesignSystem() như hook trống để Main.cs không phải đổi.
    /// </summary>
    public partial class Main
    {
        /// <summary>Dựng lớp giao diện mới cho panel trái DKCH. Gọi sau BuildDkchNewbillSection().</summary>
        private void BuildDkchDesignSystem()
        {
            // Tất cả control giờ là A* và tự đọc ThemeManager — không còn gì để tô thủ công.
        }
    }
}
