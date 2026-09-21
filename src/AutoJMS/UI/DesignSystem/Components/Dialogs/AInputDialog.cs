using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Hỏi một giá trị (mã bưu cục, ghi chú, tên file...).
    /// Xem DesignReference/AutoJMS.DESIGN.md §T.
    /// </summary>
    public class AInputDialog : ADialog
    {
        private readonly ATextBox _input;

        public AInputDialog()
        {
            Width = ThemeMetrics.DialogWidthCompact;
            Text = "Nhập giá trị";

            _input = new ATextBox();
            Controls.Add(_input);

            AddButton("Huỷ", AButtonVariant.Secondary, DialogResult.Cancel);
            AddButton("OK", AButtonVariant.Primary, DialogResult.OK, isDefault: true);
        }

        public ATextBox Input => _input;

        public string Value
        {
            get => _input.Text;
            set => _input.Text = value;
        }

        public string PlaceholderText
        {
            get => _input.PlaceholderText;
            set => _input.PlaceholderText = value;
        }

        private int InputRowHeight => S(ThemeSpacing.Sm) + S(ThemeMetrics.ControlHeight);

        protected override int BodyHeight(int width) => base.BodyHeight(width) + InputRowHeight;

        protected override int MessageAreaHeight(Rectangle body)
            => Math.Max(0, body.Height - InputRowHeight);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_input == null) return;

            var body = BodyBounds;
            int h = S(ThemeMetrics.ControlHeight);
            _input.Bounds = new Rectangle(body.X, body.Bottom - h, body.Width, h);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _input.Inner.Focus();
            _input.SelectAll();
        }

        /// <returns>Giá trị người dùng nhập, hoặc null nếu bấm Huỷ.</returns>
        public static string Ask(IWin32Window owner, string message, string title = "Nhập giá trị",
            string defaultValue = "", string placeholder = "")
        {
            using (var dialog = new AInputDialog
            {
                Text = title,
                Message = message,
                Value = defaultValue,
                PlaceholderText = placeholder
            })
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Value : null;
            }
        }
    }
}
