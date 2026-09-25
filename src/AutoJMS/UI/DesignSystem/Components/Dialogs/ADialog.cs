using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Khung hộp thoại dùng chung. Xem DesignReference/AutoJMS.DESIGN.md §T.
    ///
    ///  - FormBorderStyle.None + tự vẽ: thanh tiêu đề hệ thống không theo theme được.
    ///  - Bề rộng chỉ lấy từ ThemeMetrics.DialogWidth*, chiều cao tự tính theo nội dung.
    ///  - Nút chính NGOÀI CÙNG BÊN PHẢI. Gọi AddButton theo thứ tự trái sang phải.
    ///  - Esc = huỷ, Enter = nút mặc định.
    ///  - Không đổ bóng: làm mờ nền (scrim) để tách lớp thay cho bóng.
    /// </summary>
    public class ADialog : Form
    {
        private readonly List<AButton> _buttons = new List<AButton>();
        private ThemeHook _themeHook;
        private AButton _defaultButton;
        private Form _scrim;
        private string _message = string.Empty;

        public ADialog()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Font = ThemeTypography.Body;
            Width = ThemeMetrics.DialogWidthDefault;
            Height = 160;
        }

        protected static ThemeColors Theme => ThemeManager.Current;

        protected int S(int value) => DpiHelper.Scale(this, value);

        /// <summary>Câu mô tả dưới tiêu đề. Tiếng Việt, giữ nguyên cách gọi nghiệp vụ.</summary>
        public string Message
        {
            get => _message;
            set { _message = value ?? string.Empty; RecalcHeight(); Invalidate(); }
        }

        /// <summary>Đệm quanh toàn bộ nội dung.</summary>
        protected int Pad => S(ThemeSpacing.Xl);

        protected int TitleHeight => ThemeTypography.H2.Height;

        /// <summary>Vùng giữa tiêu đề và hàng nút - nơi lớp con đặt control.</summary>
        protected Rectangle BodyBounds
        {
            get
            {
                int top = Pad + TitleHeight + S(ThemeSpacing.Sm);
                int bottom = Height - Pad - S(ThemeMetrics.ControlHeight) - S(ThemeSpacing.Xl);
                return new Rectangle(Pad, top, Math.Max(1, Width - Pad * 2), Math.Max(0, bottom - top));
            }
        }

        protected AButton AddButton(string text, AButtonVariant variant, DialogResult result,
            bool isDefault = false, bool closeOnClick = true)
        {
            var button = new AButton { Text = text, Variant = variant, AutoSize = false };
            if (closeOnClick)
                button.Click += (s, e) => { DialogResult = result; Close(); };

            _buttons.Add(button);
            Controls.Add(button);
            if (isDefault) _defaultButton = button;

            PerformLayout();
            return button;
        }

        /// <summary>Chiều cao phần thân. Mặc định là chiều cao câu mô tả đã ngắt dòng.</summary>
        protected virtual int BodyHeight(int width)
            => string.IsNullOrEmpty(_message)
                ? 0
                : TextRenderer.MeasureText(_message, ThemeTypography.Body,
                    new Size(width, int.MaxValue), MessageFlags).Height;

        private static TextFormatFlags MessageFlags
            => TextFormatFlags.Left | TextFormatFlags.Top |
               TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;

        protected void RecalcHeight()
        {
            int width = Math.Max(1, Width - Pad * 2);
            Height = Pad + TitleHeight + S(ThemeSpacing.Sm)
                   + BodyHeight(width)
                   + S(ThemeSpacing.Xl) + S(ThemeMetrics.ControlHeight) + Pad;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_buttons.Count == 0) return;

            int h = S(ThemeMetrics.ControlHeight);
            int y = Height - Pad - h;
            int x = Width - Pad;

            // Ngược từ phải sang: phần tử THÊM CUỐI CÙNG nằm ngoài cùng bên phải.
            for (int i = _buttons.Count - 1; i >= 0; i--)
            {
                var b = _buttons[i];
                int w = Math.Max(S(88), b.GetPreferredSize(Size.Empty).Width);
                x -= w;
                b.Bounds = new Rectangle(x, y, w, h);
                x -= S(ThemeSpacing.Sm);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 0 || Height <= 0) return;

            var old = Region;
            using (var path = ControlStyler.RoundedRect(new Rectangle(0, 0, Width, Height), S(ThemeRadius.Lg)))
                Region = new Region(path);
            old?.Dispose();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _themeHook ??= new ThemeHook(this, Invalidate);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ShowScrim();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BringToFront();
            _defaultButton?.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_scrim != null) { _scrim.Close(); _scrim.Dispose(); _scrim = null; }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _themeHook?.Dispose(); _themeHook = null; }
            base.Dispose(disposing);
        }

        private void ShowScrim()
        {
            var area = Owner != null ? Owner.Bounds : Screen.FromControl(this).Bounds;
            var scrim = Theme.Scrim;
            _scrim = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = area,
                // BackColor của Form PHẢI đục: Control.set_BackColor ném ArgumentException
                // khi alpha < 255. Độ mờ đi qua Opacity, và alpha của token là nguồn DUY NHẤT
                // quyết định độ đậm (Light 0x66, Dark 0x99) - số 0.35 cứng trước đây nuốt mất
                // sự khác nhau giữa hai theme.
                BackColor = Color.FromArgb(255, scrim),
                Opacity = scrim.A / 255.0,
                ShowInTaskbar = false
            };

            // Show TRƯỚC rồi mới tắt Enabled: Form.Show(owner) từ chối form đang disabled
            // ("Forms that are not enabled cannot be displayed as a modal dialog box").
            // Tắt sau vẫn giữ nguyên ý định cũ - scrim không nuốt chuột nếu hộp thoại đóng lỗi.
            _scrim.Show(Owner);
            _scrim.Enabled = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var c = Theme;
            var g = e.Graphics;
            int radius = S(ThemeRadius.Lg);

            ControlStyler.Prepare(g, radius);
            ControlStyler.FillSurface(g, ClientRectangle, c.SurfaceRaised, radius);
            ControlStyler.DrawBorder(g, ClientRectangle, c.BorderStrong, S(ThemeBorders.Control), radius);

            TextRenderer.DrawText(g, Text, ThemeTypography.H2,
                new Rectangle(Pad, Pad, Math.Max(1, Width - Pad * 2), TitleHeight),
                c.Text, ControlStyler.TextLeft);

            if (string.IsNullOrEmpty(_message)) return;

            var body = BodyBounds;
            TextRenderer.DrawText(g, _message, ThemeTypography.Body,
                new Rectangle(body.X, body.Y, body.Width, MessageAreaHeight(body)),
                c.TextSecondary, MessageFlags);
        }

        /// <summary>Lớp con đặt control trong thân sẽ giới hạn lại vùng chữ.</summary>
        protected virtual int MessageAreaHeight(Rectangle body) => body.Height;

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyData == Keys.Enter && _defaultButton != null && !(ActiveControl is AButton))
            {
                _defaultButton.PerformClick();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;

            base.WndProc(ref m);

            // Không có thanh tiêu đề hệ thống -> báo cho Windows biết dải tiêu đề
            // là chỗ kéo, để được kéo/snap y như form thường.
            if (m.Msg != WM_NCHITTEST || m.Result.ToInt32() != HTCLIENT) return;

            var p = PointToClient(new Point(unchecked((int)(long)m.LParam)));
            if (p.Y < Pad + TitleHeight) m.Result = (IntPtr)HTCAPTION;
        }
    }
}
