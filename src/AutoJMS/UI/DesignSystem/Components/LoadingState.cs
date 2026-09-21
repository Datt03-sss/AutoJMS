using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Trạng thái đang tải. Xem DesignReference/AutoJMS.DESIGN.md §V.
    ///
    /// Thanh mảnh 2px ở ĐỈNH vùng đang tải, không spinner giữa màn, không skeleton.
    ///
    /// Không có Timer và không có khung hình nào chạy: thanh xác định vẽ theo
    /// <see cref="Progress"/>, thanh không xác định vẽ nền PrimaryTint tĩnh và để
    /// CHỮ mang thông tin ("Đang tải 1.248 đơn..."). Một thanh chạy qua chạy lại
    /// cần vài chục lần vẽ mỗi giây cho thứ không nói thêm được điều gì.
    /// </summary>
    [ToolboxItem(true)]
    public class LoadingState : APanel
    {
        private string _message = "Đang tải...";
        private int _progress = -1;

        public LoadingState()
        {
            Radius = ThemeRadius.None;
            Elevation = ThemeShadows.Elevation.Background;
        }

        [DefaultValue("Đang tải...")]
        public string Message
        {
            get => _message;
            set { _message = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>0-100 để vẽ thanh theo tỉ lệ; -1 là không xác định.</summary>
        [DefaultValue(-1)]
        public int Progress
        {
            get => _progress;
            set
            {
                int v = value < 0 ? -1 : Math.Min(100, value);
                if (_progress == v) return;
                _progress = v;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var c = Theme;
            var g = e.Graphics;
            int barH = S(ThemeMetrics.LoadingBarHeight);

            using (var track = new SolidBrush(c.PrimaryTint))
                g.FillRectangle(track, 0, 0, Width, barH);

            if (_progress >= 0)
            {
                using (var fill = new SolidBrush(c.Primary))
                    g.FillRectangle(fill, 0, 0, (int)(Width * (_progress / 100.0)), barH);
            }

            if (string.IsNullOrEmpty(_message)) return;

            TextRenderer.DrawText(g, _message, ThemeTypography.Body,
                new Rectangle(0, barH, Width, Height - barH), c.TextSecondary,
                ControlStyler.TextCenter);
        }
    }
}
