using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Lớp nền cho mọi control A* tự vẽ.
    ///
    /// Gom đúng ba thứ mà cả 12 control đều phải có y hệt nhau:
    ///  1. Cờ vẽ (UserPaint + OptimizedDoubleBuffer) - thiếu là nháy khi cuộn.
    ///  2. Đăng ký/huỷ đăng ký ThemeManager.ThemeChanged qua ThemeHook.
    ///  3. Quy đổi DPI (hàm S) - mọi hằng số pixel đi qua đây.
    /// </summary>
    [ToolboxItem(false)]
    public abstract class AControl : Control
    {
        private ThemeHook _themeHook;

        protected AControl()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            Font = ThemeTypography.Body;
        }

        /// <summary>Bảng màu hiện hành. Đọc mỗi lần vẽ - không cache vào field.</summary>
        protected static ThemeColors Theme => ThemeManager.Current;

        /// <summary>Quy đổi hằng số 96-DPI sang DPI của control này.</summary>
        protected int S(int value) => DpiHelper.Scale(this, value);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _themeHook ??= new ThemeHook(this, OnThemeChanged);
        }

        /// <summary>
        /// Gọi khi người dùng đổi theme. Mặc định chỉ vẽ lại.
        /// Control có ruột WinForms phải ghi đè để gán lại màu cho ruột.
        /// </summary>
        protected virtual void OnThemeChanged() => Invalidate();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _themeHook?.Dispose();
                _themeHook = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Font mặc định là ThemeTypography.Body - dùng chung một handle GDI cho cả tiến trình.
        /// Không cấp phát Font mới trong control.
        /// </summary>
        [DefaultValue(null)]
        public override Font Font
        {
            get => base.Font;
            set => base.Font = value ?? ThemeTypography.Body;
        }

        /// <summary>Nền trong suốt theo control cha - tránh viền tối quanh góc bo.</summary>
        protected void PaintParentBackground(PaintEventArgs e)
        {
            var back = Parent?.BackColor ?? Theme.Surface;
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
}
