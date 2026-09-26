using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Bảng dữ liệu. Xem DesignReference/AutoJMS.DESIGN.md §Q.
    ///
    /// ĐÂY LÀ NGOẠI LỆ CÓ CHỦ Ý với hướng "A* kế thừa Control và tự vẽ":
    /// lớp này kế thừa DataGridView. Yêu cầu là chạy mượt 100k dòng, mà ảo hoá dòng,
    /// tính vùng cuộn, dò ô theo toạ độ, sắp xếp, chọn vùng và trợ năng đã nằm sẵn
    /// trong DataGridView. Viết lại từ Control sẽ vừa tốn hàng nghìn dòng vừa gần như
    /// chắc chắn CHẬM HƠN thứ nó thay thế - tức là phá đúng mục tiêu của yêu cầu.
    /// Ở đây chỉ nhận phần pixel và phần cấu hình an toàn về hiệu năng.
    /// </summary>
    [ToolboxItem(true)]
    public class ADataGridView : DataGridView
    {
        private ThemeHook _themeHook;
        private bool _showVerticalLines;

        public ADataGridView()
        {
            DoubleBuffered = true;

            // Hiệu năng - xem DESIGN.md §Q "ràng buộc cứng"
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            // KHÔNG dùng DisplayedCells: chế độ đó đo lại bề rộng của mọi ô đang hiện
            // sau mỗi lần cuộn, đúng thứ làm giật bảng 100k dòng trên máy cấu hình thấp.
            // Bề rộng lấy từ Column.Width (None) hoặc FillWeight (Fill) - đều không đo ô.
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            AllowUserToResizeRows = false;

            // Chrome
            EnableHeadersVisualStyles = false;
            BorderStyle = BorderStyle.None;
            RowHeadersVisible = false;
            AllowUserToAddRows = false;
            AllowUserToDeleteRows = false;
            ReadOnly = true;
            SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            MultiSelect = false;
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            ScrollBars = ScrollBars.Both;

            ApplyTheme();
        }

        protected static ThemeColors Theme => ThemeManager.Current;

        private int S(int value) => DpiHelper.Scale(this, value);

        /// <summary>
        /// Kẻ đường dọc. Mặc định tắt - chỉ bật khi bảng có nhiều hơn
        /// <see cref="ThemeMetrics.GridVerticalLineColumnThreshold"/> cột (DESIGN.md §Q).
        /// </summary>
        [DefaultValue(false)]
        public bool ShowVerticalLines
        {
            get => _showVerticalLines;
            set
            {
                if (_showVerticalLines == value) return;
                _showVerticalLines = value;
                CellBorderStyle = value
                    ? DataGridViewCellBorderStyle.Single
                    : DataGridViewCellBorderStyle.SingleHorizontal;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_themeHook == null)
            {
                _themeHook = new ThemeHook(this, ApplyTheme);
                // Bảng trên tab chưa mở chưa có handle nên lỡ tín hiệu đổi theme lúc khởi động:
                // mở app ở Dark là nền bảng kẹt màu Light. Chỉ bắt kịp MÀU — ApplyTheme còn đặt
                // lại font ô, đè mất cỡ chữ mà ApplyStandardGridSettings của Main đã chọn.
                ApplyColors();
            }
            ApplyMetrics();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _themeHook?.Dispose(); _themeHook = null; }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Bản sao nhánh "không visual style" của base cho viền Single. Base hỏi
        /// Application.RenderWithVisualStyles (IsAppThemed, ~0,5 ms/lần trên máy có hook
        /// điều khiển từ xa) TRƯỚC khi xét EnableHeadersVisualStyles, và hàm này chạy cho
        /// từng đầu cột mỗi lần vẽ/đo - đầu cột ở đây tự tô nên nhánh visual style không bao giờ dùng.
        /// </summary>
        public override DataGridViewAdvancedBorderStyle AdjustColumnHeaderBorderStyle(
            DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStyleInput,
            DataGridViewAdvancedBorderStyle dataGridViewAdvancedBorderStylePlaceholder,
            bool isFirstDisplayedColumn, bool isLastVisibleColumn)
        {
            if (EnableHeadersVisualStyles
                || dataGridViewAdvancedBorderStyleInput.All != DataGridViewAdvancedCellBorderStyle.Single)
                return base.AdjustColumnHeaderBorderStyle(dataGridViewAdvancedBorderStyleInput,
                    dataGridViewAdvancedBorderStylePlaceholder, isFirstDisplayedColumn, isLastVisibleColumn);

            if (isFirstDisplayedColumn && !RowHeadersVisible) return dataGridViewAdvancedBorderStyleInput;

            var p = dataGridViewAdvancedBorderStylePlaceholder;
            bool rtl = RightToLeft == RightToLeft.Yes;
            p.Left = rtl ? DataGridViewAdvancedCellBorderStyle.Single : DataGridViewAdvancedCellBorderStyle.None;
            p.Right = rtl ? DataGridViewAdvancedCellBorderStyle.None : DataGridViewAdvancedCellBorderStyle.Single;
            p.Top = DataGridViewAdvancedCellBorderStyle.Single;
            p.Bottom = DataGridViewAdvancedCellBorderStyle.Single;
            return p;
        }

        /// <summary>Chiều cao hàng CỐ ĐỊNH - grid tính vùng cuộn bằng phép nhân thay vì đo từng hàng.</summary>
        private void ApplyMetrics()
        {
            RowTemplate.Height = S(ThemeMetrics.GridRowHeight);
            ColumnHeadersHeight = S(ThemeMetrics.GridHeaderHeight);
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            ApplyMetrics();
        }

        public void ApplyTheme()
        {
            ApplyColors();

            DefaultCellStyle.Font = ThemeTypography.Grid;
            DefaultCellStyle.Padding = new Padding(
                S(ThemeSpacing.CellPadX), S(ThemeSpacing.CellPadY),
                S(ThemeSpacing.CellPadX), S(ThemeSpacing.CellPadY));

            ColumnHeadersDefaultCellStyle.Font = ThemeTypography.GridHeader;
            ColumnHeadersDefaultCellStyle.Padding = new Padding(S(ThemeSpacing.CellPadX), 0, S(ThemeSpacing.CellPadX), 0);

            Invalidate();
        }

        private void ApplyColors()
        {
            var c = Theme;

            BackgroundColor = c.Surface;
            GridColor = c.Border;
            ForeColor = c.Text;

            DefaultCellStyle.BackColor = c.SurfaceRaised;
            DefaultCellStyle.ForeColor = c.Text;
            DefaultCellStyle.SelectionBackColor = c.PrimaryTint;
            DefaultCellStyle.SelectionForeColor = c.Text;

            AlternatingRowsDefaultCellStyle.BackColor = c.SurfaceAlt;
            AlternatingRowsDefaultCellStyle.ForeColor = c.Text;
            AlternatingRowsDefaultCellStyle.SelectionBackColor = c.PrimaryTint;
            AlternatingRowsDefaultCellStyle.SelectionForeColor = c.Text;

            ColumnHeadersDefaultCellStyle.BackColor = c.SurfaceAlt;
            ColumnHeadersDefaultCellStyle.ForeColor = c.Text;
            ColumnHeadersDefaultCellStyle.SelectionBackColor = c.SurfaceAlt;
            ColumnHeadersDefaultCellStyle.SelectionForeColor = c.Text;
        }

        /// <summary>
        /// Định dạng một cột theo vai trò trong DESIGN.md §Q.
        /// Mã vận đơn/mã bưu cục dùng font đều để các dòng thẳng cột khi dò bằng mắt.
        /// </summary>
        public void StyleColumn(string columnName, AColumnRole role)
        {
            if (!Columns.Contains(columnName)) return;
            var col = Columns[columnName];

            switch (role)
            {
                case AColumnRole.Code:
                    col.DefaultCellStyle.Font = ThemeTypography.Mono;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                    break;
                case AColumnRole.Number:
                    col.DefaultCellStyle.Font = ThemeTypography.Mono;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    break;
                default:
                    col.DefaultCellStyle.Font = ThemeTypography.Grid;
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
                    break;
            }
        }
    }

    public enum AColumnRole
    {
        Text,
        /// <summary>Mã vận đơn, mã bưu cục - font đều, căn trái.</summary>
        Code,
        /// <summary>Số liệu - font đều, căn phải.</summary>
        Number
    }
}
