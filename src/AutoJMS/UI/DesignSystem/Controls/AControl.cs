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
            // CacheText: không có cờ này, mỗi lần vẽ/layout và mỗi lần đọc Text (GetPreferredSize
            // của AButton trong FlowLayoutPanel) là một GetWindowText = 2 message Win32. Phải bật
            // TRƯỚC khi có handle - bật sau thì Text trả về "". Xem AppTheme.CacheText.
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.CacheText, true);

            Font = ThemeTypography.Body;
        }

        /// <summary>Bảng màu hiện hành. Đọc mỗi lần vẽ - không cache vào field.</summary>
        protected static ThemeColors Theme => ThemeManager.Current;

        /// <summary>Quy đổi hằng số 96-DPI sang DPI của control này.</summary>
        protected int S(int value) => DpiHelper.Scale(this, value);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_themeHook != null) return;
            _themeHook = new ThemeHook(this, OnThemeChanged);
            // Control trên tab chưa mở chưa có handle nên lỡ tín hiệu đổi theme lúc khởi động;
            // ruột WinForms (TextBox của ATextBox) kẹt màu Light khi mở app ở Dark. Bắt kịp một lần.
            OnThemeChanged();
        }

        /// <summary>
        /// Gọi khi người dùng đổi theme. Mặc định chỉ vẽ lại.
        /// Control có ruột WinForms phải ghi đè để gán lại màu cho ruột.
        /// </summary>
        protected virtual void OnThemeChanged() => Invalidate();

        // Vòng focus chỉ vẽ khi ShowFocusCues (focus đến từ bàn phím) — bấm chuột không để lại
        // viền đen. Windows bật cờ này ở lần bấm Tab/Alt đầu tiên; vẽ lại để vòng hiện ngay.
        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            if (e.ChangeFocus) Invalidate();
        }

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
            // Tắt AA khi tô: AA + PixelOffsetMode.None chỉ phủ nửa hàng/cột pixel đầu, bộ đệm
            // trắng lộ ra thành vệt chữ L sáng ở mép trên-trái (ACheckBox, AStatusIndicator ở Dark).
            var g = e.Graphics;
            var mode = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            using (var brush = new SolidBrush(ControlStyler.SurfaceBehind(Parent)))
                g.FillRectangle(brush, ClientRectangle);
            g.SmoothingMode = mode;
        }
    }
}
