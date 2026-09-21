using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Quy đổi hằng số pixel ở 96 DPI sang DPI thật của màn hình.
    /// Xem DesignReference/AutoJMS.DESIGN.md §X.
    ///
    /// MỌI hằng số pixel trong DesignSystem phải đi qua đây. Không có ngoại lệ.
    ///
    /// Mẫu làm tròn lấy từ PremiumTabAccent.cs - chỗ duy nhất trong repo đang làm đúng
    /// trước khi có DesignSystem. MidpointRounding.AwayFromZero để 1px ở 125% ra 1px
    /// chứ không ra 0 (Math.Round mặc định làm tròn về số chẵn: 1.25 -> 1, nhưng
    /// 0.5 -> 0, làm mất hẳn đường kẻ mảnh).
    /// </summary>
    public static class DpiHelper
    {
        public const int BaseDpi = 96;

        public static double ScaleFactor(Control ctrl)
            => ctrl == null ? 1.0 : ctrl.DeviceDpi / (double)BaseDpi;

        /// <summary>Quy đổi giá trị 96-DPI theo DPI của control. Kết quả tối thiểu là 1 khi đầu vào &gt; 0.</summary>
        public static int Scale(Control ctrl, int value)
        {
            if (ctrl == null || value == 0) return value;
            int scaled = (int)Math.Round(value * ScaleFactor(ctrl), MidpointRounding.AwayFromZero);
            return value > 0 ? Math.Max(1, scaled) : Math.Min(-1, scaled);
        }

        public static float Scale(Control ctrl, float value)
            => ctrl == null ? value : (float)(value * ScaleFactor(ctrl));

        public static Size Scale(Control ctrl, Size value)
            => new Size(Scale(ctrl, value.Width), Scale(ctrl, value.Height));

        public static Padding Scale(Control ctrl, Padding value)
            => new Padding(
                Scale(ctrl, value.Left),
                Scale(ctrl, value.Top),
                Scale(ctrl, value.Right),
                Scale(ctrl, value.Bottom));

        /// <summary>Padding đều bốn phía từ một token của ThemeSpacing.</summary>
        public static Padding Uniform(Control ctrl, int spacing)
        {
            int s = Scale(ctrl, spacing);
            return new Padding(s);
        }
    }
}
