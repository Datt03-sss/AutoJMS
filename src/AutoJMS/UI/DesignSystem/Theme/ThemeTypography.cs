using System.Drawing;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Thang chữ của AutoJMS. Xem DesignReference/AutoJMS.DESIGN.md §D.
    ///
    /// Font là static readonly - cấp phát MỘT lần cho cả tiến trình.
    /// Không bao giờ tạo Font mới trong vòng lặp control hay trong OnPaint:
    /// mỗi Font là một GDI handle, và đó chính là lỗi đã làm hỏng 4 lần thử
    /// migration trước (AppTheme.cs:209 gán Font mới cho MỌI control).
    /// Người gọi KHÔNG được Dispose() các font này.
    ///
    /// Cỡ tính bằng POINT (WinForms mặc định), không phải pixel. Point tự nở theo DPI.
    /// </summary>
    public static class ThemeTypography
    {
        public const string Family = "Segoe UI";
        public const string FamilySemibold = "Segoe UI Semibold";
        public const string FamilyMono = "Consolas";

        /// <summary>Số KPI lớn, tiêu đề trang. 16pt Semibold.</summary>
        public static readonly Font Display = new Font(FamilySemibold, 16F, FontStyle.Regular);

        /// <summary>Tiêu đề section. 13pt Semibold.</summary>
        public static readonly Font H1 = new Font(FamilySemibold, 13F, FontStyle.Regular);

        /// <summary>Tiêu đề card/panel. 11pt Semibold.</summary>
        public static readonly Font H2 = new Font(FamilySemibold, 11F, FontStyle.Regular);

        /// <summary>Chữ mặc định toàn app. 9.75pt Regular.</summary>
        public static readonly Font Body = new Font(Family, 9.75F, FontStyle.Regular);

        /// <summary>Nhãn form, nhấn mạnh trong dòng. 9.75pt Semibold.</summary>
        public static readonly Font BodyStrong = new Font(FamilySemibold, 9.75F, FontStyle.Regular);

        /// <summary>Chữ trên nút. 9.75pt Semibold.</summary>
        public static readonly Font Button = new Font(FamilySemibold, 9.75F, FontStyle.Regular);

        /// <summary>Chữ trợ giúp, metadata. 8.25pt.</summary>
        public static readonly Font Small = new Font(Family, 8.25F, FontStyle.Regular);

        /// <summary>Nhỏ nhất - chỉ chú thích và badge. 8pt.</summary>
        public static readonly Font Caption = new Font(Family, 8F, FontStyle.Regular);

        /// <summary>Badge trạng thái. 8pt Semibold.</summary>
        public static readonly Font Badge = new Font(FamilySemibold, 8F, FontStyle.Regular);

        /// <summary>Ô dữ liệu trong bảng. 9pt.</summary>
        public static readonly Font Grid = new Font(Family, 9F, FontStyle.Regular);

        /// <summary>Đầu cột bảng. 9pt Semibold.</summary>
        public static readonly Font GridHeader = new Font(FamilySemibold, 9F, FontStyle.Regular);

        /// <summary>
        /// Mã vận đơn, mã bưu cục, số liệu xếp cột. 9pt đều.
        /// Font tỉ lệ làm 1/7 và 0/8 xô lệch giữa hai dòng khi người dùng dò mã bằng mắt.
        /// CHỈ dùng cho mã và số, không dùng cho chữ thường.
        /// </summary>
        public static readonly Font Mono = new Font(FamilyMono, 9F, FontStyle.Regular);

        /// <summary>Số KPI - đều, để nhiều card xếp cạnh nhau thẳng cột.</summary>
        public static readonly Font MonoDisplay = new Font(FamilyMono, 16F, FontStyle.Bold);

        /// <summary>
        /// Số đếm lớn đứng một mình giữa panel (ô "Tổng" của tab TRA HÀNH TRÌNH). 26pt.
        /// Có token riêng vì AppTheme tô lại ô này ở MỖI lần đổi theme: `new Font` tại đó
        /// rò một handle GDI mỗi lượt.
        /// </summary>
        public static readonly Font Metric = new Font(FamilySemibold, 26F, FontStyle.Bold);

        /// <summary>Chiều cao dòng cho chữ nhiều dòng. Nhãn một dòng dùng 1.0.</summary>
        public const float LineHeightMultiline = 1.35F;

        private static readonly Font[] Tokens =
        {
            Display, H1, H2, Body, BodyStrong, Button, Small, Caption, Badge, Grid, GridHeader, Mono, MonoDisplay,
            Metric
        };

        /// <summary>
        /// Đúng khi control đang giữ CHÍNH một instance trong bảng trên. AppTheme dựa vào
        /// đây để không kéo control đã chọn token về cỡ chữ mặc định của nó.
        /// So theo tham chiếu chứ không theo giá trị: font do Designer dựng luôn là
        /// instance mới, nên một font tình cờ cùng tên/cùng cỡ không bị nhận nhầm.
        /// </summary>
        public static bool IsToken(Font font)
        {
            foreach (var token in Tokens)
                if (ReferenceEquals(font, token)) return true;
            return false;
        }
    }
}
