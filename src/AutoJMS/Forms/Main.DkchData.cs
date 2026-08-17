using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// LƯU Ý: lớp Main nằm ở namespace "AutoJMS" (không phải "AutoJMS.Forms") dù file
// ở thư mục Forms/. Đặt sai namespace sẽ tạo ra một lớp Main THỨ HAI, không partial
// với lớp thật, và trình biên dịch báo "does not exist in the current context".
namespace AutoJMS
{
    /// <summary>
    /// Mục "DATA" của tab DKCH — vẽ hoàn toàn bằng code.
    ///
    /// Bốn lần sửa trước và vì sao đều hụt:
    ///   1. Kéo-thả (uiTableLayoutPanel8): bề rộng cột là pixel cứng, không liên quan chữ thật.
    ///   2. Tự đo + Sunny.UI: AppTheme gán Font = "Segoe UI" 10F cho MỌI control SAU khi
    ///      layout xong, nên số đo trước đó thành vô nghĩa.
    ///   3. AutoSize + Sunny.UI: nhãn hết cắt, nhưng UIComboBox tự vẽ text bên trong → vẫn cắt.
    ///   4. ComboBox/NumericUpDown hệ thống: hết cắt chữ, nhưng WinForms vẽ lại cả control
    ///      mỗi lần hover (kèm bước xoá nền) nên nháy, và giao diện thô.
    ///
    /// Bản này không mượn control hệ thống nào cho phần tương tác nữa. Mọi thứ là
    /// <see cref="Control"/> tự vẽ với UserPaint + OptimizedDoubleBuffer, nên:
    ///   • Không nháy: mỗi khung hình được dựng trọn trong bộ đệm rồi mới lên màn hình.
    ///   • Đẹp và nhất quán: bo góc, viền sáng khi hover, màu lấy thẳng từ AppTheme.
    ///   • Không ai cắt chữ hộ ta: bề rộng do chính ta đo và chính ta vẽ.
    /// Nhãn vẫn dùng Label AutoSize để WinForms tự đo — thứ duy nhất nó làm tốt hơn ta.
    /// </summary>
    public partial class Main
    {
        // Tên control giữ NGUYÊN để phần còn lại của Main.cs không phải sửa.
        private Panel tabDKCH_dataHost;
        private Label tabDKCH_lblMode;
        private Label tabDKCH_lblSheet;
        private Label tabDKCH_lblCol;
        private Label tabDKCH_lblUseSheet;
        private Label tabDKCH_countSum;
        private Label tabDKCH_countSave;
        private Panel tabDKCH_divider;
        private DkchDropDown tabDKCH_guideMode;
        private DkchDropDown tabDKCH_sheetName;
        private DkchSpin tabDKCH_numRow;
        private DkchToggle tabDKCH_useSheet;

        private Font _dkchLabelFont;
        private Font _dkchCountFont;
        private bool _dkchLayoutBusy;

        private const string DkchLabelFamily = "Segoe UI Semibold";
        private const float DkchLabelPtMax = 10.5f;
        private const float DkchLabelPtMin = 8f;
        private const int DkchGapLabel = 5;  // nhãn ↔ control của nó (để sát nhau)
        private const int DkchGapGroup = 12; // giữa cặp nhãn+control bên trái và bên phải
        private const int DkchRowGap = 8;    // khoảng cách dọc giữa các hàng
        private const int DkchSwitchW = 40;
        private const int DkchSwitchH = 20;
        // Nhãn đầy đủ; nếu hàng không đủ chỗ thì rút gọn theo yêu cầu của chủ dự án.
        private const string DkchUseSheetLong = "Dùng sheet";
        private const string DkchUseSheetShort = "U_Sheet";
        // Lòng trong của tabDKCH_dataSrc (272 - viền trái/phải); chỉ dùng khi control
        // chưa có handle nên ClientSize còn bằng 0.
        private const int DkchDesignWidth = 270;

        /// <summary>Dựng lại toàn bộ mục DATA. Gọi ngay sau InitializeComponent().</summary>
        private void BuildDkchDataSection()
        {
            if (tabDKCH_dataSrc == null || tabDKCH_dataSrc.IsDisposed) return;

            tabDKCH_dataSrc.SuspendLayout();

            // Dọn sạch mọi thứ designer từng đặt vào đây.
            for (int i = tabDKCH_dataSrc.Controls.Count - 1; i >= 0; i--)
            {
                Control stale = tabDKCH_dataSrc.Controls[i];
                tabDKCH_dataSrc.Controls.RemoveAt(i);
                stale.Dispose();
            }

            tabDKCH_dataHost = new Panel
            {
                Name = "tabDKCH_dataHost",
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0),
                TabStop = false
            };

            tabDKCH_lblMode = MakeDkchLabel("tabDKCH_lblMode", "Chế độ");
            tabDKCH_lblSheet = MakeDkchLabel("tabDKCH_lblSheet", "Sheet");
            tabDKCH_lblCol = MakeDkchLabel("tabDKCH_lblCol", "Cột");
            tabDKCH_lblUseSheet = MakeDkchLabel("tabDKCH_lblUseSheet", DkchUseSheetLong);
            tabDKCH_countSum = MakeDkchLabel("tabDKCH_countSum", "Tổng: 0");
            tabDKCH_countSave = MakeDkchLabel("tabDKCH_countSave", "OK: 0");

            tabDKCH_divider = new Panel
            {
                Name = "tabDKCH_divider",
                Height = 1,
                Margin = new Padding(0),
                TabStop = false
            };

            tabDKCH_guideMode = MakeDkchDropDown("tabDKCH_guideMode", 10, "Normal", "Newbie");
            tabDKCH_sheetName = MakeDkchDropDown("tabDKCH_sheetName", 11, "DKCH", "PHATLAI");

            tabDKCH_numRow = new DkchSpin
            {
                Name = "tabDKCH_numRow",
                Font = new Font(DkchLabelFamily, DkchLabelPtMax, FontStyle.Bold),
                Minimum = 1M,
                Maximum = 31M,
                Value = 1M,
                Margin = new Padding(0),
                TabIndex = 12
            };

            tabDKCH_useSheet = new DkchToggle
            {
                Name = "tabDKCH_useSheet",
                Size = new Size(DkchSwitchW, DkchSwitchH),
                Margin = new Padding(0),
                TabIndex = 13
            };
            // Giữ nguyên đấu nối cũ của designer.
            tabDKCH_useSheet.ActiveChanged += tabDKCH_btnStop_Click;

            tabDKCH_dataHost.Controls.AddRange(new Control[]
            {
                tabDKCH_lblMode, tabDKCH_guideMode, tabDKCH_lblUseSheet, tabDKCH_useSheet,
                tabDKCH_lblSheet, tabDKCH_sheetName, tabDKCH_lblCol, tabDKCH_numRow,
                tabDKCH_divider, tabDKCH_countSum, tabDKCH_countSave
            });

            tabDKCH_dataSrc.Controls.Add(tabDKCH_dataHost);
            tabDKCH_dataSrc.ResumeLayout(false);

            tabDKCH_dataHost.Resize += (s, e) => LayoutDkchDataSection();
            LayoutDkchDataSection();
        }

        private static Label MakeDkchLabel(string name, string text) => new Label
        {
            Name = name,
            Text = text,
            // AutoSize = true là điểm mấu chốt: chính WinForms đo chữ bằng bộ vẽ của nó,
            // nên nhãn không thể hẹp hơn chữ kể cả khi AppTheme đổi Font.
            AutoSize = true,
            Font = new Font(DkchLabelFamily, DkchLabelPtMax, FontStyle.Bold),
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,   // "&" trong dữ liệu không bị biến thành gạch chân
            TabStop = false
        };

        private static DkchDropDown MakeDkchDropDown(string name, int tabIndex, params string[] items)
        {
            var dd = new DkchDropDown
            {
                Name = name,
                Font = new Font(DkchLabelFamily, DkchLabelPtMax, FontStyle.Bold),
                Margin = new Padding(0),
                TabIndex = tabIndex
            };
            foreach (var it in items) dd.Items.Add(it);
            if (items.Length > 0) dd.SelectedIndex = 0;
            return dd;
        }

        /// <summary>
        /// Xếp lại mục DATA theo bề rộng chữ đo được. An toàn khi gọi nhiều lần.
        /// </summary>
        private void LayoutDkchDataSection()
        {
            if (_dkchLayoutBusy) return;
            if (tabDKCH_dataSrc == null || tabDKCH_dataSrc.IsDisposed) return;
            if (tabDKCH_dataHost == null || tabDKCH_dataHost.IsDisposed) return;
            if (tabDKCH_sheetName == null || tabDKCH_guideMode == null || tabDKCH_numRow == null) return;

            _dkchLayoutBusy = true;
            try
            {
                int avail = tabDKCH_dataHost.ClientSize.Width;
                if (avail < 120) avail = DkchDesignWidth;   // chưa có handle

                // Ô số phải hiện đủ số chữ số của Maximum (tối thiểu 2 chữ số: "99").
                int digits = Math.Max(2, ((int)tabDKCH_numRow.Maximum).ToString().Length);
                string widestNum = new string('9', digits);

                // Lưới 2 cột: [nhãn | control]  [nhãn | control]
                //   hàng 1: Chế độ | dropdown      Dùng sheet | toggle
                //   hàng 2: Sheet  | dropdown      Cột        | ô số
                // Hai dropdown dùng CHUNG bề rộng để mép phải thẳng hàng.
                float pt = DkchLabelPtMax;
                string useSheetText = DkchUseSheetLong;
                int leftLblW = 0, ddW = 0, useSheetLblW = 0, colLblW = 0, spinW = 0, rightGroupW = 0;
                bool fits = false;

                // Ưu tiên giữ nhãn đầy đủ; chỉ khi cỡ chữ đã xuống thấp mà vẫn chật thì
                // mới rút gọn "Dùng sheet" → "U_Sheet" (đổi nhãn dễ đọc hơn là teo chữ).
                foreach (var attempt in new[]
                {
                    new { Text = DkchUseSheetLong, Floor = 9.5f },
                    new { Text = DkchUseSheetShort, Floor = DkchLabelPtMin }
                })
                {
                    for (pt = DkchLabelPtMax; pt >= attempt.Floor; pt -= 0.5f)
                    {
                        using (var probe = new Font(DkchLabelFamily, pt, FontStyle.Bold))
                        {
                            leftLblW = WidestOf(probe, tabDKCH_lblMode.Text, tabDKCH_lblSheet.Text);
                            ddW = Math.Max(DkchDropDown.WidthFor(tabDKCH_sheetName, probe),
                                           DkchDropDown.WidthFor(tabDKCH_guideMode, probe));
                            useSheetLblW = WidestOf(probe, attempt.Text);
                            colLblW = WidestOf(probe, tabDKCH_lblCol.Text);
                            spinW = DkchSpin.WidthFor(probe, widestNum);
                            // Trong nhóm phải, mỗi control bám sát nhãn CỦA NÓ. Nếu dóng cả
                            // hai vào một cột chung thì "Cột" (ngắn) sẽ bị đẩy xa khỏi ô số.
                            rightGroupW = Math.Max(useSheetLblW + DkchGapLabel + DkchSwitchW,
                                                   colLblW + DkchGapLabel + spinW);
                        }

                        int total = leftLblW + DkchGapLabel + ddW + DkchGapGroup + rightGroupW;
                        if (total <= avail) { useSheetText = attempt.Text; fits = true; break; }
                    }
                    if (fits) break;
                }

                if (!fits)
                {
                    // Hết cách thu gọn — dùng cấu hình nhỏ nhất, thà tràn vài pixel còn hơn cắt chữ.
                    pt = DkchLabelPtMin;
                    useSheetText = DkchUseSheetShort;
                }
                if (tabDKCH_lblUseSheet.Text != useSheetText) tabDKCH_lblUseSheet.Text = useSheetText;

                // Đổi font TRƯỚC, huỷ font cũ SAU để không huỷ font đang được vẽ.
                var newFont = new Font(DkchLabelFamily, pt, FontStyle.Bold);
                foreach (var lb in new Label[] { tabDKCH_lblMode, tabDKCH_lblSheet,
                                                 tabDKCH_lblCol, tabDKCH_lblUseSheet })
                {
                    if (lb != null && !lb.IsDisposed) lb.Font = newFont;
                }
                tabDKCH_sheetName.Font = newFont;
                tabDKCH_guideMode.Font = newFont;
                tabDKCH_numRow.Font = newFont;

                // Hai ô đếm là thông tin phụ, để nhỏ hơn nhãn một bậc rưỡi cho đỡ chiếm chỗ.
                var countFont = new Font(DkchLabelFamily, Math.Max(7f, pt - 1.5f), FontStyle.Bold);
                foreach (var lb in new Label[] { tabDKCH_countSum, tabDKCH_countSave })
                {
                    if (lb != null && !lb.IsDisposed) lb.Font = countFont;
                }

                _dkchLabelFont?.Dispose();
                _dkchLabelFont = newFont;
                _dkchCountFont?.Dispose();
                _dkchCountFont = countFont;

                // Nhãn đã tự co giãn theo font mới → lấy bề rộng THẬT do WinForms tính.
                leftLblW = Math.Max(leftLblW, ActualWidest(tabDKCH_lblMode, tabDKCH_lblSheet));
                useSheetLblW = Math.Max(useSheetLblW, ActualWidest(tabDKCH_lblUseSheet));
                colLblW = Math.Max(colLblW, ActualWidest(tabDKCH_lblCol));
                rightGroupW = Math.Max(useSheetLblW + DkchGapLabel + DkchSwitchW,
                                       colLblW + DkchGapLabel + spinW);

                ApplyDkchDataColors();

                int fieldH = Math.Max(24, newFont.Height + 8);
                tabDKCH_sheetName.ItemHeight = fieldH;
                tabDKCH_guideMode.ItemHeight = fieldH;

                int rowH = Math.Max(fieldH, Math.Max(newFont.Height + 4, DkchSwitchH));

                // Chốt chặn cuối: nếu vẫn quá khổ thì dropdown là thứ DUY NHẤT chịu co —
                // nó có sẵn "…" khi thiếu chỗ, còn nhãn thì không được phép cắt.
                int over = leftLblW + DkchGapLabel + ddW + DkchGapGroup + rightGroupW - avail;
                if (over > 0) ddW = Math.Max(48, ddW - over);

                int xCtrlLeft = leftLblW + DkchGapLabel;
                int xLblRight = xCtrlLeft + ddW + DkchGapGroup;
                int y = 0;

                // Hàng 1 — Chế độ | dropdown        Dùng sheet | toggle
                PlaceLabel(tabDKCH_lblMode, 0, y, rowH);
                Place(tabDKCH_guideMode, xCtrlLeft, y + (rowH - fieldH) / 2, ddW, fieldH);
                PlaceLabel(tabDKCH_lblUseSheet, xLblRight, y, rowH);
                Place(tabDKCH_useSheet, xLblRight + useSheetLblW + DkchGapLabel,
                      y + (rowH - DkchSwitchH) / 2, DkchSwitchW, DkchSwitchH);
                y += rowH + DkchRowGap;

                // Hàng 2 — Sheet | dropdown         Cột | ô số
                PlaceLabel(tabDKCH_lblSheet, 0, y, rowH);
                Place(tabDKCH_sheetName, xCtrlLeft, y + (rowH - fieldH) / 2, ddW, fieldH);
                PlaceLabel(tabDKCH_lblCol, xLblRight, y, rowH);
                Place(tabDKCH_numRow, xLblRight + colLblW + DkchGapLabel,
                      y + (rowH - fieldH) / 2, spinW, fieldH);
                y += rowH + 2;

                // Đường kẻ ngăn phần đếm — kéo sát lên trên, hàng đếm chỉ có chữ nên
                // không cần cao bằng hàng có dropdown (rowH ~27px là quá thừa).
                int lineW = Math.Max(40, Math.Min(avail, xLblRight + rightGroupW));
                Place(tabDKCH_divider, 0, y, lineW, 1);
                y += 4;

                // Hàng 3 — Tổng / OK, "OK" thẳng cột với nhóm bên phải.
                int countH = countFont.Height + 1;
                PlaceLabel(tabDKCH_countSum, 0, y, countH);
                PlaceLabel(tabDKCH_countSave, xLblRight, y, countH);
                y += countH;

                // Panel cao đúng nội dung (Dock=Top nên đổi Height là an toàn).
                int wanted = tabDKCH_dataSrc.Padding.Top + y + tabDKCH_dataSrc.Padding.Bottom + 2;
                if (tabDKCH_dataSrc.Height != wanted) tabDKCH_dataSrc.Height = wanted;

                AppLogger.Info($"[DKCH] bố cục DATA: {pt:0.#}pt, rộng {avail}px, nhãn trái {leftLblW}, " +
                               $"dropdown {ddW}, \"{useSheetText}\" {useSheetLblW}, Cột {colLblW}, ô số {spinW}, " +
                               $"chiếm {xLblRight + rightGroupW}px, cao {wanted}px");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[DKCH] không xếp được bố cục DATA: {ex.Message}");
            }
            finally
            {
                _dkchLayoutBusy = false;
            }
        }

        /// <summary>
        /// Tô màu theo theme. AppTheme chỉ nhận diện control Sunny.UI nên các control
        /// tự vẽ ở đây phải tự lấy màu, nếu không ở Dark sẽ trắng bệch.
        /// </summary>
        private void ApplyDkchDataColors()
        {
            var colors = UI.AppTheme.Colors;
            bool dark = UI.AppTheme.CurrentTheme == UI.ThemeMode.Dark;

            foreach (var lb in new Label[] { tabDKCH_lblMode, tabDKCH_lblSheet, tabDKCH_lblCol,
                                             tabDKCH_lblUseSheet, tabDKCH_countSum, tabDKCH_countSave })
            {
                if (lb == null || lb.IsDisposed) continue;
                lb.BackColor = Color.Transparent;
                lb.ForeColor = colors.TextPrimary;
            }

            if (tabDKCH_countSum != null && !tabDKCH_countSum.IsDisposed) tabDKCH_countSum.ForeColor = colors.TextSecondary;
            if (tabDKCH_countSave != null && !tabDKCH_countSave.IsDisposed) tabDKCH_countSave.ForeColor = colors.TextSecondary;
            if (tabDKCH_divider != null && !tabDKCH_divider.IsDisposed) tabDKCH_divider.BackColor = colors.SubtleBorder;

            foreach (var dd in new DkchDropDown[] { tabDKCH_sheetName, tabDKCH_guideMode })
            {
                if (dd == null || dd.IsDisposed) continue;
                dd.FieldBackColor = colors.InputBackground;
                dd.BorderColor = colors.InputBorder;
                dd.HoverBorderColor = colors.PrimaryAccent;
                dd.HighlightColor = colors.PrimaryAccent;
                dd.HighlightForeColor = colors.TextInverse;
                dd.HoverItemColor = dark ? colors.GridAlternating : colors.PrimaryHoverTint;
                dd.ForeColor = colors.TextPrimary;
                dd.Invalidate();
            }

            if (tabDKCH_numRow != null && !tabDKCH_numRow.IsDisposed)
            {
                tabDKCH_numRow.FieldBackColor = colors.InputBackground;
                tabDKCH_numRow.BorderColor = colors.InputBorder;
                tabDKCH_numRow.HoverBorderColor = colors.PrimaryAccent;
                tabDKCH_numRow.ButtonHoverColor = dark ? colors.GridAlternating : colors.PrimaryHoverTint;
                tabDKCH_numRow.ForeColor = colors.TextPrimary;
                tabDKCH_numRow.Invalidate();
            }

            if (tabDKCH_useSheet != null && !tabDKCH_useSheet.IsDisposed)
            {
                tabDKCH_useSheet.ActiveColor = colors.PrimaryAccent;
                tabDKCH_useSheet.InactiveColor = dark ? colors.InputBorder : Color.FromArgb(205, 205, 212);
                tabDKCH_useSheet.KnobColor = dark ? colors.TextPrimary : Color.White;
                tabDKCH_useSheet.Invalidate();
            }
        }

        private static void Place(Control c, int x, int y, int w, int h)
        {
            if (c == null || c.IsDisposed) return;
            var target = new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
            if (c.Bounds != target) c.Bounds = target;
        }

        /// <summary>
        /// Đặt nhãn AutoSize: CHỈ gán Location, để WinForms giữ bề rộng nó tự đo
        /// (gán Width cho control AutoSize sẽ bị bộ layout ghi đè ngay sau đó).
        /// </summary>
        private static void PlaceLabel(Label lb, int x, int y, int rowH)
        {
            if (lb == null || lb.IsDisposed) return;
            var target = new Point(x, y + Math.Max(0, (rowH - lb.Height) / 2));
            if (lb.Location != target) lb.Location = target;
        }

        /// <summary>Bề rộng lớn nhất mà WinForms ĐANG thực sự dùng cho các nhãn này.</summary>
        private static int ActualWidest(params Label[] labels)
        {
            int max = 0;
            if (labels == null) return 0;
            foreach (var lb in labels)
            {
                if (lb == null || lb.IsDisposed) continue;
                int w = Math.Max(lb.Width, lb.PreferredSize.Width);
                if (w > max) max = w;
            }
            return max;
        }

        /// <summary>Bề rộng pixel thật của chuỗi dài nhất trong danh sách, theo <paramref name="font"/>.</summary>
        private static int WidestOf(Font font, params string[] texts)
        {
            int max = 0;
            if (texts == null) return 0;
            foreach (var t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                // KHÔNG dùng NoPadding: Label vẽ chữ có đệm hai bên, đo không đệm sẽ
                // ra hẹp hơn lúc vẽ và chữ bị cắt ("Dùng sheet" → "Dùng").
                int w = TextRenderer.MeasureText(t, font, new Size(int.MaxValue, int.MaxValue),
                                                TextFormatFlags.SingleLine).Width;
                if (w > max) max = w;
            }
            return max;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Bộ control tự vẽ cho mục DATA.
    // Điểm chung: UserPaint + OptimizedDoubleBuffer + AllPaintingInWmPaint. Toàn bộ
    // khung hình được dựng trong bộ đệm rồi mới đưa lên màn hình, nên đổi trạng thái
    // hover không thể gây nháy — khác hẳn ComboBox/NumericUpDown hệ thống vốn xoá nền
    // rồi mới vẽ lại từng phần.
    // ─────────────────────────────────────────────────────────────────────────────

    internal static class DkchPaint
    {
        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Chữ nhật chỉ bo MỘT PHÍA — để hai ô ghép sát thành một khối, chỉ bo
        /// hai cạnh ngoài cùng còn hai cạnh giáp nhau thì vuông.
        /// </summary>
        public static GraphicsPath RoundSide(Rectangle r, int radius, bool left, bool right)
        {
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            if (left) path.AddArc(r.X, r.Y, d, d, 180, 90);
            else path.AddLine(r.X, r.Y, r.X, r.Y);

            if (right) { path.AddArc(r.Right - d, r.Y, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); }
            else { path.AddLine(r.Right, r.Y, r.Right, r.Bottom); }

            if (left) path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            else path.AddLine(r.X, r.Bottom, r.X, r.Y);

            path.CloseFigure();
            return path;
        }

        /// <summary>Vẽ dấu ˅ (mũi xổ) căn giữa trong ô cho trước.</summary>
        public static void Chevron(Graphics g, Rectangle box, Color color)
        {
            float w = 9f, h = 4.5f;
            float cx = box.X + (box.Width - w) / 2f;
            float cy = box.Y + (box.Height - h) / 2f;
            using (var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[] { new PointF(cx, cy), new PointF(cx + w / 2f, cy + h), new PointF(cx + w, cy) });
            }
        }

        public static void Glyph(Graphics g, Rectangle box, Color color, bool plus)
        {
            float len = 9f;
            float cx = box.X + box.Width / 2f;
            float cy = box.Y + box.Height / 2f;
            using (var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(pen, cx - len / 2f, cy, cx + len / 2f, cy);
                if (plus) g.DrawLine(pen, cx, cy - len / 2f, cx, cy + len / 2f);
            }
        }
    }

    /// <summary>
    /// Dropdown tự vẽ thay cho ComboBox. Giữ API <c>Items</c> / <c>SelectedIndex</c> /
    /// <c>SelectedItem</c> / <c>Text</c> / <c>SelectedIndexChanged</c> / <c>TextChanged</c>
    /// nên phần còn lại của Main.cs không phải sửa.
    /// </summary>
    internal sealed class DkchDropDown : Control
    {
        private readonly List<object> _items = new List<object>();
        private int _selectedIndex = -1;
        private bool _hot;
        private bool _open;
        private ToolStripDropDown _popup;
        private DateTime _closedAt = DateTime.MinValue;

        public event EventHandler SelectedIndexChanged;

        public DkchDropDown()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            ItemHeight = 26;
            Size = new Size(120, 26);
        }

        public List<object> Items { get { return _items; } }
        public int ItemHeight { get; set; }
        public int Radius { get; set; } = 6;

        public Color FieldBackColor { get; set; } = Color.White;
        public Color BorderColor { get; set; } = Color.FromArgb(209, 213, 219);
        public Color HoverBorderColor { get; set; } = Color.FromArgb(99, 102, 241);
        public Color HighlightColor { get; set; } = Color.FromArgb(99, 102, 241);
        public Color HighlightForeColor { get; set; } = Color.White;
        public Color HoverItemColor { get; set; } = Color.FromArgb(238, 242, 255);

        public bool IsOpen { get { return _open; } }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set { SetSelectedIndex(value, true); }
        }

        public object SelectedItem
        {
            get { return (_selectedIndex >= 0 && _selectedIndex < _items.Count) ? _items[_selectedIndex] : null; }
            set
            {
                string target = value == null ? null : value.ToString();
                for (int i = 0; i < _items.Count; i++)
                {
                    string cur = _items[i] == null ? null : _items[i].ToString();
                    if (string.Equals(cur, target, StringComparison.Ordinal)) { SetSelectedIndex(i, true); return; }
                }
            }
        }

        private void SetSelectedIndex(int index, bool raise)
        {
            if (index < -1 || index >= _items.Count) return;
            if (_selectedIndex == index) return;
            _selectedIndex = index;
            // Gán base.Text sẽ tự phát TextChanged — Main.cs đang lắng nghe sự kiện đó.
            base.Text = (index >= 0 && _items[index] != null) ? _items[index].ToString() : "";
            Invalidate();
            if (raise)
            {
                var handler = SelectedIndexChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        /// <summary>Bề rộng cần để hiện trọn giá trị dài nhất, gồm cả chỗ cho mũi xổ.</summary>
        public static int WidthFor(DkchDropDown dd, Font font)
        {
            if (dd == null) return 0;
            int text = 0;
            foreach (var item in dd._items)
            {
                string t = item == null ? "" : item.ToString();
                if (string.IsNullOrEmpty(t)) continue;
                int w = TextRenderer.MeasureText(t, font, new Size(int.MaxValue, int.MaxValue),
                                                TextFormatFlags.SingleLine).Width;
                if (w > text) text = w;
            }
            return text + 8 /*lề trái*/ + 17 /*mũi xổ*/ + 5 /*lề phải*/;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            // Bấm ra ngoài đã tự đóng popup rồi; nếu không chặn thì cú bấm đó lại mở ngay lại.
            if (_open || (DateTime.UtcNow - _closedAt).TotalMilliseconds < 250) return;
            OpenPopup();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_items.Count == 0) return;
            if (e.KeyCode == Keys.Down) { SetSelectedIndex(Math.Min(_items.Count - 1, _selectedIndex + 1), true); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { SetSelectedIndex(Math.Max(0, _selectedIndex - 1), true); e.Handled = true; }
            else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OpenPopup(); e.Handled = true; }
        }

        private void OpenPopup()
        {
            if (_items.Count == 0 || _open || !IsHandleCreated) return;

            var list = new DkchDropDownList(this);
            var host = new ToolStripControlHost(list)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = list.Size
            };
            _popup = new ToolStripDropDown
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                DropShadowEnabled = true,
                BackColor = FieldBackColor
            };
            _popup.Items.Add(host);
            _popup.Size = list.Size;
            _popup.Closed += (s, e) =>
            {
                _open = false;
                _popup = null;
                _closedAt = DateTime.UtcNow;
                Invalidate();
            };

            _open = true;
            Invalidate();
            _popup.Show(this, new Point(0, Height + 2));
        }

        internal void CommitFromPopup(int index)
        {
            SetSelectedIndex(index, true);
            var p = _popup;
            if (p != null) p.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            bool lit = _hot || _open || Focused;

            using (var path = DkchPaint.RoundRect(box, Radius))
            {
                using (var brush = new SolidBrush(FieldBackColor)) g.FillPath(brush, path);
                using (var pen = new Pen(lit ? HoverBorderColor : BorderColor, lit ? 1.4f : 1f)) g.DrawPath(pen, path);
            }

            var chevronBox = new Rectangle(box.Right - 19, box.Y, 17, box.Height);
            var textBox = new Rectangle(box.X + 8, box.Y, Math.Max(1, chevronBox.X - box.X - 10), box.Height);
            TextRenderer.DrawText(g, Text, Font, textBox, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

            DkchPaint.Chevron(g, chevronBox, lit ? HoverBorderColor : ForeColor);
        }
    }

    /// <summary>Danh sách xổ ra của <see cref="DkchDropDown"/> — cũng tự vẽ, cũng có bộ đệm.</summary>
    internal sealed class DkchDropDownList : Control
    {
        private readonly DkchDropDown _owner;
        private int _hotIndex = -1;

        public DkchDropDownList(DkchDropDown owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = owner.Font;
            ForeColor = owner.ForeColor;
            BackColor = owner.FieldBackColor;
            Cursor = Cursors.Hand;

            int itemH = Math.Max(18, owner.ItemHeight);
            Size = new Size(Math.Max(40, owner.Width), itemH * Math.Max(1, owner.Items.Count) + 8);
        }

        private int ItemH { get { return Math.Max(18, _owner.ItemHeight); } }

        private int IndexAt(int y)
        {
            int i = (y - 4) / ItemH;
            return (i >= 0 && i < _owner.Items.Count) ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Y);
            if (i != _hotIndex) { _hotIndex = i; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hotIndex != -1) { _hotIndex = -1; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.Y);
            if (i >= 0) _owner.CommitFromPopup(i);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (var path = DkchPaint.RoundRect(box, 8))
            {
                using (var brush = new SolidBrush(BackColor)) g.FillPath(brush, path);
                using (var pen = new Pen(_owner.BorderColor, 1f)) g.DrawPath(pen, path);
            }

            int itemH = ItemH;
            for (int i = 0; i < _owner.Items.Count; i++)
            {
                var r = new Rectangle(4, 4 + i * itemH, Math.Max(1, Width - 8), itemH);
                Color fore = ForeColor;

                if (i == _owner.SelectedIndex)
                {
                    using (var path = DkchPaint.RoundRect(r, 5))
                    using (var brush = new SolidBrush(_owner.HighlightColor))
                    {
                        g.FillPath(brush, path);
                    }
                    fore = _owner.HighlightForeColor;
                }
                else if (i == _hotIndex)
                {
                    using (var path = DkchPaint.RoundRect(r, 5))
                    using (var brush = new SolidBrush(_owner.HoverItemColor))
                    {
                        g.FillPath(brush, path);
                    }
                }

                string text = _owner.Items[i] == null ? "" : _owner.Items[i].ToString();
                var textBox = new Rectangle(r.X + 7, r.Y, Math.Max(1, r.Width - 12), r.Height);
                TextRenderer.DrawText(g, text, Font, textBox, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>
    /// Ô số tự vẽ thay cho NumericUpDown, dạng "−  n  +".
    /// <c>Value</c> được KẸP vào [Minimum, Maximum] thay vì ném ArgumentOutOfRangeException
    /// như NumericUpDown chuẩn — giữ đúng hành vi của UIIntegerUpDown cũ, để một file
    /// cấu hình có defaultRowCount ngoài khoảng không làm app văng lúc khởi động.
    /// </summary>
    internal sealed class DkchSpin : Control
    {
        private decimal _value = 1M;
        private decimal _min = 1M;
        private decimal _max = 31M;
        private int _hotButton;   // 0 = không, 1 = giảm, 2 = tăng
        private bool _hot;

        public event EventHandler ValueChanged;

        public DkchSpin()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(84, 26);
        }

        public int Radius { get; set; } = 6;
        public int ButtonWidth { get; set; } = DkchButtonWidth;

        /// <summary>Bề rộng nút −/+. Dùng chung với <see cref="WidthFor"/> nên đổi một chỗ là đủ.</summary>
        private const int DkchButtonWidth = 18;

        public Color FieldBackColor { get; set; } = Color.White;
        public Color BorderColor { get; set; } = Color.FromArgb(209, 213, 219);
        public Color HoverBorderColor { get; set; } = Color.FromArgb(99, 102, 241);
        public Color ButtonHoverColor { get; set; } = Color.FromArgb(238, 242, 255);

        public decimal Minimum
        {
            get { return _min; }
            set { _min = value; if (_value < _min) Value = _min; else Invalidate(); }
        }

        public decimal Maximum
        {
            get { return _max; }
            set { _max = value; if (_value > _max) Value = _max; else Invalidate(); }
        }

        public decimal Value
        {
            get { return _value; }
            set
            {
                decimal v = Math.Min(_max, Math.Max(_min, value));
                if (_value == v) return;
                _value = v;
                Invalidate();
                var handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        /// <summary>Bề rộng cần để hiện trọn <paramref name="widestNumber"/> giữa hai nút.</summary>
        public static int WidthFor(Font font, string widestNumber)
        {
            int text = TextRenderer.MeasureText(widestNumber ?? "99", font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine).Width;
            return DkchButtonWidth * 2 + Math.Max(18, text + 8);
        }

        private Rectangle MinusBox { get { return new Rectangle(1, 1, ButtonWidth, Math.Max(1, Height - 2)); } }
        private Rectangle PlusBox { get { return new Rectangle(Math.Max(1, Width - ButtonWidth - 1), 1, ButtonWidth, Math.Max(1, Height - 2)); } }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hot = false;
            if (_hotButton != 0) _hotButton = 0;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hot = MinusBox.Contains(e.Location) ? 1 : (PlusBox.Contains(e.Location) ? 2 : 0);
            Cursor = hot == 0 ? Cursors.Default : Cursors.Hand;
            if (hot != _hotButton) { _hotButton = hot; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            if (MinusBox.Contains(e.Location)) Value = _value - 1M;
            else if (PlusBox.Contains(e.Location)) Value = _value + 1M;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up) { Value = _value + 1M; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Value = _value - 1M; e.Handled = true; }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta != 0) Value = _value + (e.Delta > 0 ? 1M : -1M);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var box = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            bool lit = _hot || Focused;

            using (var path = DkchPaint.RoundRect(box, Radius))
            {
                using (var brush = new SolidBrush(FieldBackColor)) g.FillPath(brush, path);
                using (var pen = new Pen(lit ? HoverBorderColor : BorderColor, lit ? 1.4f : 1f)) g.DrawPath(pen, path);
            }

            if (_hotButton != 0)
            {
                var hotBox = _hotButton == 1 ? MinusBox : PlusBox;
                using (var path = DkchPaint.RoundRect(hotBox, Radius))
                using (var brush = new SolidBrush(ButtonHoverColor))
                {
                    g.FillPath(brush, path);
                }
            }

            bool canDown = _value > _min;
            bool canUp = _value < _max;
            DkchPaint.Glyph(g, MinusBox, canDown ? ForeColor : Blend(ForeColor, FieldBackColor), false);
            DkchPaint.Glyph(g, PlusBox, canUp ? ForeColor : Blend(ForeColor, FieldBackColor), true);

            var textBox = new Rectangle(MinusBox.Right, box.Y, Math.Max(1, PlusBox.X - MinusBox.Right), box.Height);
            TextRenderer.DrawText(g, ((int)_value).ToString(), Font, textBox, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private static Color Blend(Color a, Color b)
        {
            return Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);
        }
    }

    /// <summary>
    /// Toggle vẽ tay thay cho Sunny.UI UISwitch. Giữ nguyên API <c>Active</c> /
    /// <c>ActiveChanged</c> nên phần còn lại của Main.cs không phải sửa.
    /// ActiveChanged chỉ phát khi NGƯỜI DÙNG bật/tắt, không phát khi gán bằng code —
    /// tránh gọi nhầm handler lúc khởi động.
    /// </summary>
    internal sealed class DkchToggle : Control
    {
        private bool _active;
        private bool _hot;

        public event EventHandler ActiveChanged;

        public DkchToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw | ControlStyles.UserPaint
                   | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(40, 20);
        }

        public bool Active
        {
            get { return _active; }
            set
            {
                if (_active == value) return;
                _active = value;
                Invalidate();
            }
        }

        public Color ActiveColor { get; set; } = Color.FromArgb(99, 102, 241);
        public Color InactiveColor { get; set; } = Color.FromArgb(205, 205, 212);
        public Color KnobColor { get; set; } = Color.White;

        private void ToggleByUser()
        {
            _active = !_active;
            Invalidate();
            var handler = ActiveChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) ToggleByUser();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                ToggleByUser();
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int h = Math.Max(10, Height - 1);
            int w = Math.Max(h + 4, Width - 1);
            var track = new Rectangle(0, 0, w, h);

            using (var path = DkchPaint.RoundRect(track, h / 2))
            using (var brush = new SolidBrush(_active ? ActiveColor : InactiveColor))
            {
                g.FillPath(brush, path);
            }

            if (_hot || Focused)
            {
                using (var path = DkchPaint.RoundRect(track, h / 2))
                using (var pen = new Pen(ActiveColor, 1.4f))
                {
                    g.DrawPath(pen, path);
                }
            }

            int d = Math.Max(6, h - 4);
            int x = _active ? w - d - 2 : 2;
            using (var brush = new SolidBrush(KnobColor))
            {
                g.FillEllipse(brush, x, 2, d, d);
            }
        }
    }
}
