using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Tiến trình cho việc chạy lâu (tải lô đơn, in hàng loạt, xuất Excel).
    /// Xem DesignReference/AutoJMS.DESIGN.md §T, §V.
    ///
    /// Dùng Show() rồi cập nhật <see cref="Progress"/>, KHÔNG dùng ShowDialog:
    /// ShowDialog chặn luồng gọi nên chẳng còn ai chạy việc để mà báo tiến độ.
    /// Nút Huỷ chỉ bật cờ <see cref="Cancelled"/> - việc đang chạy tự dừng và
    /// tự đóng hộp thoại, vì đóng giữa chừng sẽ bỏ lại một vòng lặp đang ghi.
    /// </summary>
    public class AProgressDialog : ADialog
    {
        private readonly LoadingState _bar;
        private AButton _cancel;

        public AProgressDialog()
        {
            Width = ThemeMetrics.DialogWidthDefault;
            Text = "Đang xử lý";
            ControlBox = false;

            _bar = new LoadingState { Message = string.Empty, Progress = -1 };
            Controls.Add(_bar);
        }

        /// <summary>Người dùng đã bấm Huỷ. Vòng lặp đang chạy phải tự kiểm tra cờ này.</summary>
        public bool Cancelled { get; private set; }

        /// <summary>0-100, hoặc -1 khi chưa biết tổng số.</summary>
        public int Progress
        {
            get => _bar.Progress;
            set { _bar.Progress = value; _bar.Refresh(); }
        }

        /// <summary>Thêm nút Huỷ. Gọi trước khi Show().</summary>
        public void EnableCancel(string text = "Huỷ")
        {
            if (_cancel != null) return;

            _cancel = AddButton(text, AButtonVariant.Secondary, DialogResult.Cancel, closeOnClick: false);
            _cancel.Click += (s, e) =>
            {
                Cancelled = true;
                _cancel.Enabled = false;
                Progress = -1;
                SetStatus("Đang dừng...");
            };
        }

        /// <summary>Đổi câu mô tả và vẽ lại ngay - dùng trong vòng lặp trên luồng UI.</summary>
        public void SetStatus(string message)
        {
            Message = message;
            Refresh();
        }

        private int BarRowHeight => S(ThemeSpacing.Sm) + S(ThemeMetrics.LoadingBarHeight);

        protected override int BodyHeight(int width) => base.BodyHeight(width) + BarRowHeight;

        protected override int MessageAreaHeight(Rectangle body)
            => Math.Max(0, body.Height - BarRowHeight);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_bar == null) return;

            var body = BodyBounds;
            int h = S(ThemeMetrics.LoadingBarHeight);
            _bar.Bounds = new Rectangle(body.X, body.Bottom - h, body.Width, h);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            // Esc không được đóng: việc vẫn đang chạy.
            if (keyData == Keys.Escape)
            {
                if (_cancel != null && _cancel.Enabled) _cancel.PerformClick();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}
