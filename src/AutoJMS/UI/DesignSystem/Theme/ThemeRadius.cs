namespace AutoJMS.UI.DesignSystem
{
    /// <summary>
    /// Bo góc. Xem DesignReference/AutoJMS.DESIGN.md §G.
    /// Năm giá trị, không có giá trị thứ sáu.
    ///
    /// Cố ý ngược Pinterest (16/32, cấm góc vuông): mật độ desktop không chịu được
    /// góc bo lớn, và bo góc bề mặt cấu trúc tạo khe hở nhìn thấy được giữa các panel kề nhau.
    /// </summary>
    public static class ThemeRadius
    {
        /// <summary>0 - MẶC ĐỊNH. Bề mặt cấu trúc: trang, nav, bảng, splitter, toolbar.</summary>
        public const int None = 0;

        /// <summary>4 - ô nhập, nút, combo, checkbox.</summary>
        public const int Sm = 4;

        /// <summary>6 - card, panel, popover.</summary>
        public const int Md = 6;

        /// <summary>8 - dialog.</summary>
        public const int Lg = 8;

        /// <summary>Sentinel: bo nửa chiều cao. CHỈ badge trạng thái.</summary>
        public const int Pill = -1;

        /// <summary>Quy ra bán kính thật cho một control cao <paramref name="height"/> px.</summary>
        public static int Resolve(int radius, int height)
            => radius == Pill ? height / 2 : radius;
    }
}
