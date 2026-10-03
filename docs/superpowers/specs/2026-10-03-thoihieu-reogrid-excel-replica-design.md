# Tab Thời hiệu dựng lại bằng ReoGrid — bản sao bảng Excel "Ký nhận thời hiệu theo mốc time"

- Ngày: 2026-10-03
- Trạng thái: thiết kế đã được Owner duyệt trong chat 2026-10-03. Cập nhật cùng ngày theo file mẫu mới
  của Owner và kết quả bước 0 (mục 5).
- Giai đoạn: **layout**. Đổ dữ liệu thật là giai đoạn sau, có spec riêng.

## 1. Mục tiêu

Tab Thời hiệu của `FullStackOperation` hiện **giống 100%** sheet "Tổng" của file Excel thực tế. "Giống" ở đây gồm:
cấu trúc, kích thước cột/hàng, font, màu, viền, định dạng số và chữ — **kể cả lỗi chính tả**. Bảng nằm trong
khung giống cửa sổ Excel. Bước này dùng dữ liệu giả.

Giới hạn: nét chữ (khử răng cưa) và chỗ ngắt dòng của tiêu đề nhiều chữ có thể lệch Excel vài pixel vì Excel dùng
engine chữ riêng. Mọi hướng làm đều chịu giới hạn này, nên nó không được tính là lệch.

## 2. Quyết định của Owner

| Câu hỏi | Owner chọn |
|---|---|
| Hướng làm | Control spreadsheet bên thứ ba (không nâng renderer GDI+, không WebView2) |
| Control | ReoGrid Community — NuGet `unvell.ReoGrid.dll` 3.3.1, MIT, `net8.0-windows7.0` |
| Khung | Như cửa sổ Excel: chữ cột, số hàng, tab sheet, đường lưới |
| Lỗi của file gốc | Giữ y nguyên ("tỷ lên đạt", "Basline", "22H/23H/24H", dấu "." ở W34) |
| Phạm vi | "làm lại toàn bộ tabThoiHieu, xóa đi làm lại bằng ReoGid" — xoá hết code tab cũ |
| Thanh công cụ | Chỉ giữ **Xuất ảnh** + **Mở thư mục** (bỏ nút zoom, bỏ dòng trạng thái) |

## 3. Nguồn: file Excel gốc

File nguồn là `docs/layout/tabThoiHieu/Ký nhận thời hiệu  theo mốc time neww.xls` (BIFF8; tên có **hai** dấu
cách sau "hiệu"). File này chứa tên nhân viên thật nên **không bao giờ được commit**; nó chỉ là đầu vào của script
tạo file mẫu (mục 4.2). Bản trước của spec dựa trên file `… new (1).xls`; file `neww` khác nó ở layout hàng 1,
AutoFilter, độ rộng cột và viền (đã ghi bên dưới).

Workbook có hai sheet đang hiện là "Tổng" và "data", cùng vài sheet ẩn (các tháng cũ). Script xoá mọi sheet ẩn.

### 3.1 Sheet "Tổng"

- Zoom 70%, đường lưới bật. Font mặc định của workbook là Arial 10, nên độ rộng chữ số lớn nhất là 7 px.
- Độ rộng cột đổi ra pixel theo công thức `px = trunc((256·w + 18) / 256 · 7)`:

  | A | B | C | D | E | F | G | H | I | J | K…AC | AD |
  |---|---|---|---|---|---|---|---|---|---|---|---|
  | 46 | 101 | 71 | 93 | 217 | 69 | 86 | 80 | 52 | 76 | 57 mỗi cột | 70 (mặc định) |

  AE (217 px) và AG (197 px) có độ rộng riêng nhưng trống, nằm ngoài bảng.
- Chiều cao hàng:
  - hàng 1: 81 pt
  - hàng 2: 77.25 pt
  - hàng 3–33: 15 pt
  - hàng 34: 12.6 pt
  - hàng 35: ẩn
- Vùng merge: A1:J1, V1:W1, Y1:AA1, AB1:AB2, AC1:AC2, AD1:AD2, B3:B30 (cột SPV), A31:E31, B32:C32, E35:L35.
- AutoFilter: **không có** (file cũ có C2:X31).
- Bố cục:
  - **Hàng 1:** tiêu đề A1:J1 "BẢNG KÝ NHẬN THỜI HIỆU THEO MỐC THỜI GIAN 214A02", rồi các cặp nhãn/giá trị — Hàng
    mới (K1/L1), Số TTTC quét gửi đến (M1/N1), Số đơn chưa đến (O1/P1), Số đơn chuyển tiếp (Q1/R1), Cần phát ra
    (S1/T1), Cần kí đạt KPI 91% (U1/V1:W1), Cần kí đạt KPI 90% (X1/Y1:AA1). Sau đó là AB1 "Còn thiếu mốc 91%",
    AC1 "Còn thiếu mốc Basline 90%" và AD1 (ô nền xanh).
  - **Hàng 2:** tiêu đề cột — STT, Người phụ trách (SPV), Quét mã ở Bưu cục, Mã nhân viên phát, Nhân viên phát,
    Đơn phát, Số đơn cần kí nhận thời hiệu 91%, Số đơn cần kí Basline 90%, Số vận đơn ký nhận, Tỉ lệ ký nhận,
    8h…21h, 22H, 23H, 24H.
  - **Hàng 3–30:** 28 nhân viên.
  - **Hàng 31:** dòng "Tổng".
  - **Hàng 32–34:** chú thích "Quy ước màu tỷ lệ" (B32:C32) — ô xanh "tỷ lên đạt 90% trở lên" (E32), ô trắng
    "Tỷ lệ 8x" (E33), ô đỏ "Tỷ lệ <80%" (E34).
- Kiểu ô chính (nguồn đầy đủ của từng kiểu ô là file mẫu, mục 4.2):

  | Vùng | Font | Nền | Định dạng số |
  |---|---|---|---|
  | Tiêu đề A1 | Arial 16, chữ trắng | 92D050 | — |
  | Nhãn tóm tắt K1, M1, O1, Q1, S1, U1, X1 | Calibri Light 12 đậm | accent3 tint 0.6 | — |
  | Giá trị tóm tắt L1, N1, P1, R1 | Calibri Light 12 đậm, chữ đỏ | FFFF00 | `0` |
  | Giá trị tóm tắt T1, V1, Y1 | Calibri Light 12 đậm, chữ đỏ | trắng | `0` |
  | AB1, AC1 | Arial 10 đậm, chữ đỏ | FFFF00 | — |
  | AD1 | Arial 10 | 00B0F0 | — |
  | Hàng 2: A, B | Arial 10 đậm | trắng | — |
  | Hàng 2: C–F | Times New Roman 12 đậm | trắng | — |
  | Hàng 2: G, H | Times New Roman 12 đậm, chữ đỏ | FFFF00 | — |
  | Hàng 2: I–AA | Times New Roman 12 đậm | accent6 tint 0.6 | — |
  | Thân: A–F | Arial 10 | — | — |
  | Thân: G, H | Calibri 11 | — | `0_ ` |
  | Thân: I | Arial 10 | trắng | kế toán; số 0 hiện "-" |
  | Thân: J | Arial 10 | — | `0.0%` |
  | Thân: K–AA | Times New Roman 11 | trắng | kế toán; số 0 hiện "-" |
  | Thân: AB, AC | Arial 10 | — | kế toán |
  | Tổng: A31:E31 | Times New Roman 12, chữ trắng | FF0000 | — |
  | Tổng: F, G, H | Times New Roman 12 | FFFF00 | `0` |
  | Tổng: J | Arial 10 | FFFF00 | `0.00%` |
  | Tổng: K–AA | Times New Roman 11, chữ trắng | FF0000 | kế toán |

  Mọi ô có viền trong bảng đều là viền `thin` bốn cạnh. Khác file cũ: thân các cột D, E, F, AB, AC, AD **có**
  viền.

- Định dạng có điều kiện:
  - **J3:J31 — data bar.** Mốc là `min` và `max` của vùng, màu 63C384, gradient, không viền. Vùng này **gồm cả
    dòng Tổng**.
  - **K3:AA30 — thang 3 màu.** `min` → F8696B, `percentile 50` → FCFCFF, `max` → 5A8AC6. Vùng này **không gồm**
    dòng Tổng.
- Công thức:
  - G = F×91.1%, H = F×90%
  - I = SUM(K:AA), J = I/F
  - K..AA = `COUNTIFS(data!C:C, Tổng!E, data!A:A, ">h-1", data!A:A, "<=h")`; riêng cột K chỉ có điều kiện `"<=8"`
  - AB = G−I, AC = H−I
  - Hàng 31 = SUM từng cột
  - T1 = F31, V1 = T1×91%, Y1 = T1×90%

### 3.2 Sheet "data"

Hàng 2 là tiêu đề: "Thời gian | Mã Shipper | Tên nhân viên phát hàng | Thời gian ký nhận". Từ hàng 3 là dữ
liệu, trong đó cột A = `HOUR(D)`. Sheet này là nguồn của COUNTIFS. Nó liên quan tới giai đoạn đổ dữ liệu, nhưng ở
bước này chỉ giữ hàng tiêu đề.

## 4. Kiến trúc

### 4.1 Xoá

- Cả 6 file trong `src/AutoJMS/FullStack/UI/ThoiHieu/`:
  - `ThoiHieuKpiModels.cs`
  - `ThoiHieuKpiSampleData.cs`
  - `ThoiHieuKpiGridRenderer.cs`
  - `ThoiHieuKpiSheetControl.cs`
  - `ThoiHieuKpiImageExporter.cs`
  - `ThoiHieuKpiColorPalette.cs`
- Trong `FullStackOperation.cs`:
  - Mọi field của tab cũ, gồm cả cụm `DataGridView` chết (`_thoiHieuGrid`, timer, bộ lọc, footer, font ô).
  - `_uiReady` và `_pendingThoiHieuRows`. Sau khi xoá phần đọc, `_uiReady` chỉ còn được gán, nên giữ nó sẽ
    sinh warning CS0414.
  - Toàn bộ hàm từ `CreateThoiHieuKpiToolbar` tới `UpdateThoiHieuFooter`.
  - Nhánh `ThoiHieuRow` trong sort header và trong `uiDataGridView2_CellFormatting`.
  - Lời gọi `RefreshThoiHieuKpiSheet()` trong `RefreshFilteredGrid`.
  - Class `ThoiHieuRow`.
- Trong `FullStackOperation.Theme.cs`: các màu `ThoiHieu*`, `ThoiHieuPalette`, phần tô toolbar/sheet trong
  `ApplyFullStackContentTheme`, và class `ThemedFlatButton` (chỉ toolbar cũ dùng).
- Trong `FullStackOperation.Designer.cs`: lời gọi `DisposeThoiHieuFonts()`.

Phần dựng tab trong `FullStackOperation` vẫn giữ: chèn `_tabThoiHieu` vào vị trí 1 của `uiTabControl1` và bỏ
`tabPage4` khỏi `uiTabControl2`. Khác biệt duy nhất là nội dung tab giờ là `new ThoiHieuKpiView { Dock = Fill }`.

### 4.2 File mẫu `ThoiHieuKpi.template.xlsx`

Đường dẫn: `src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpi.template.xlsx`, khai báo `EmbeddedResource` với
`LogicalName="ThoiHieuKpi.template.xlsx"` trong `AutoJMS.csproj`.

File được tạo **một lần** từ file nguồn (mục 3) bằng Excel COM. Script tạo là
`eng/tmp/thoihieu/make-template.ps1`, nằm ở `eng/tmp/` và không commit. File nguồn mở read-only. Các bước:

1. Xoá mọi sheet ẩn.
2. Với sheet "Tổng":
   - Giữ nguyên mọi thứ ngoài dữ liệu thân bảng: tiêu đề, nhãn, kiểu ô, vùng merge, độ rộng cột, chiều cao hàng,
     zoom, chú thích, các lỗi chính tả, dấu "." ở W34, hàng 35 ẩn.
   - B3 (ô gộp B3:B30) thành "Giám sát 01"; E3:E30 thành "Nhân viên 01".."Nhân viên 28". C giữ "214A02", D vốn
     trống.
   - K..AA thành số giả tĩnh (`System.Random(42)`), thay cho COUNTIFS. F = tổng K..AA cộng một phần dư ngẫu nhiên.
   - L1 = 3000, N1 = 2950, P1 = 2, R1 = 15.
   - **G3:G30 viết lại thành `=F3*911/1000`** (cùng giá trị với `F3*91.1%`). Lý do: ReoGrid 3.3.1 tách số trong
     công thức theo `CurrentCulture`; trên máy vi-VN dấu thập phân là ",", nên `91.1%` là lỗi cú pháp và làm cả
     lệnh Load ném lỗi. Không đổi culture của app để né lỗi này.
   - Các công thức còn lại (H, I, J, AB, AC, hàng 31, T1/V1/Y1) giữ nguyên: bước 0 cho thấy ReoGrid tính đúng cả
     193 công thức.
3. Với sheet "data": giữ hàng 1–2, xoá từ hàng 3 trở đi.
4. Xoá metadata tài liệu (`RemoveDocumentInformation`): người tạo, người sửa cuối.
5. Trước khi commit, chạy `eng/tmp/thoihieu/privacy-check.py <nguồn.xlsx> <mẫu.xlsx>`. Script lấy mọi tên ở
   `Tổng!B3:B30`, `Tổng!E3:E30`, `data!C` và người tạo/sửa trong `docProps/core.xml` của file nguồn, rồi tìm trong
   mọi XML của file mẫu. Phải in `LEAKS: none`.

Lý do làm sạch: repo **public**, mà file nguồn chứa tên khoảng 28 nhân viên thật cùng hàng chục nghìn dòng ký nhận.

Sau này Owner có thể mở file mẫu bằng Excel để chỉnh layout mà không cần sửa code. Nếu thêm công thức có số thập
phân thì phải viết lại không có dấu thập phân như G (test ở mục 6 sẽ báo).

### 4.3 `ThoiHieuKpiView` (UserControl)

- Gồm 2 phần: thanh công cụ ở trên (nút **Xuất ảnh**, nút **Mở thư mục** — nút sau tắt cho tới khi xuất ảnh lần
  đầu) và `ReoGridControl` lấp phần còn lại.
- `internal static Worksheet LoadTemplate(IWorkbook workbook)` — dùng chung cho màn hình, ảnh xuất và test:
  1. Nạp file mẫu từ manifest resource stream bằng `workbook.Load(stream, FileFormat.Excel2007)`.
  2. Bật `WorksheetSettings.Edit_Readonly` cho mọi sheet. Người dùng chọn ô được nhưng không sửa được.
  3. Gọi `Recalculate()` cho sheet "Tổng". **Bắt buộc**: ReoGrid không tính shared formula lúc Load, thiếu bước
     này các ô như I5, J5 ra 0.
  4. Gọi `ThoiHieuKpiConditionalFormat.Apply(sheet "Tổng")` và trả về sheet đó.
- Màn hình mở ở sheet "Tổng", zoom 70% (lấy từ file); zoom bằng Ctrl + lăn chuột của ReoGrid. Hiện chữ cột, số
  hàng, tab sheet "Tổng | data" và đường lưới.
- Theme:
  - Bảng luôn giữ kiểu Excel nền trắng, kể cả ở theme Dark.
  - Thanh công cụ theo theme app: nền `SurfaceAlt` ở Dark, `245,245,245` ở Light/Red như toolbar cũ; tô lại qua
    `ThemeHook` của chính view.
  - Nút là `AButton` biến thể Secondary, như nút "Xuất dữ liệu" của Dashboard. Nút tự theo theme.
  - `FullStackOperation` không đi qua `AppTheme.Apply`, nên không có gì kéo font hay màu của `ReoGridControl`.
- Xuất ảnh — `internal static string ExportPng(string directory)`:
  - Dựng một `ReoGridControl` **không có Form cha**, gọi `LoadTemplate`, tắt chữ cột/số hàng/đường lưới/tab sheet
    /thanh cuộn, đặt `ScaleFactor = 1` và `SelectionStyle = None` (không thì khung chọn ô A1 lọt vào ảnh).
  - Đặt kích thước control bằng `GetRangePhysicsBounds` của A1:AD(Tổng+3), tạo handle, rồi `DrawToBitmap`.
    Không có Form nên không cướp focus và Windows không kẹp kích thước theo màn hình. Bước 0 ra ảnh 2046×853.
  - Ghi `thoi-hieu-yyyyMMdd-HHmmss-fff.png` vào `AppPaths.UserDataDir\FullStack\Exports\ThoiHieu` (cùng thư mục
    với bản cũ).
  - Xong thì hiện toast "Đã xuất ảnh thời hiệu." và bật nút **Mở thư mục**. Lỗi thì ghi `AppLogger.Error` và hiện
    MessageBox như bản cũ.
- Nạp file mẫu lỗi (chỉ xảy ra khi resource hỏng): ghi `AppLogger.Error`, tắt nút Xuất ảnh, form FullStack vẫn mở.
- Kích thước tạo bằng code (chiều cao toolbar, padding, nút) đi qua `DpiHelper.Scale`.

### 4.4 `ThoiHieuKpiConditionalFormat` (static, logic thuần)

Mọi quy tắc biên dưới đây đã đối chiếu với Excel thật bằng COM ở bước 0.

- `double? Num(object value)` — hằng trong ô là `decimal`, kết quả công thức là `double`; mọi thứ khác ra `null`.
- `Color[] ColorScale3(IReadOnlyList<double?> values)` — thang 3 màu đúng cách Excel làm:
  - Mốc giữa là percentile 50 theo kiểu PERCENTILE.INC (nội suy tuyến tính trên dãy đã sắp).
  - Thứ tự xét: `v ≥ max` → 5A8AC6; ngược lại `v ≤ min` → F8696B; ngược lại `v ≤ giữa` → nội suy F8696B→FCFCFF với
    `t = (v−min)/(giữa−min)`; còn lại nội suy FCFCFF→5A8AC6 với `t = (v−giữa)/(max−giữa)`.
  - Mỗi kênh màu = `a + (int)((b − a)·t)` (cắt phần lẻ, không làm tròn).
  - Hệ quả: mọi giá trị bằng nhau ra màu **max** 5A8AC6.
  - Ô `null`: `Color.Empty`, không tô.
- `double DataBarFraction(double value, double min, double max)` — `(value − min) / (max − min)`, kẹp trong
  [0, 1]; khi `max == min` thì trả **0.5**.
- `int FindTotalRow(Worksheet)` — hàng (0-based) có chữ "Tổng" ở cột A, `-1` nếu không có. Với file mẫu là 30.
- `Apply(Worksheet)`:
  - Tô thang màu cho K3:AA(Tổng−1) bằng `SetRangeStyles` từng ô (cờ `PlainStyleFlag.BackColor`).
  - Gắn data bar cho J3:J(Tổng) bằng `CellBody` tự vẽ: nền, rồi thanh gradient 63C384→F3FAF5 tại
    `(x+1, y+2, (rộng−3)·tỉ lệ, cao−4)`, rồi chữ.
  - Gắn `CellBody` kế toán cho I và K..AC, hàng 3 tới dòng Tổng. ReoGrid nạp định dạng kế toán thành số thường,
    nên body tự vẽ: số 0 ra "-" dồn phải cách mép 4 px; số âm có "-" dồn trái cách mép 3 px; số dương dồn phải,
    dạng `#,##0` theo `CurrentCulture` ("6.105" trên máy vi-VN, giống Excel trên cùng máy).
  - Body tô nền bằng `Graphics.FillRectangle(Bounds, …)`. **Không** dùng `dc.DrawCellBackground()`: ở zoom khác
    100% hàm này nhân tỉ lệ lần hai.
  - Vì chỉ đọc giá trị đang có trong ô, giai đoạn đổ dữ liệu thật gọi lại được nguyên hàm này.

## 5. Bước 0 — đã chạy (trong `eng/tmp/thoihieu/`, không commit)

Kết quả, ngày 2026-10-03:

- ReoGrid 3.3.1 nạp file mẫu, vẽ đúng merge, font, màu theme có tint, viền thin, chiều cao hàng, xuống dòng ở
  tiêu đề, hàng 35 ẩn, zoom 70% — so ảnh với `CopyPicture` của Excel ở 100% và 70%.
- Cả 193 công thức có `FormulaStatus.Normal` sau `Recalculate()`, kể cả `%`, SUM và COUNTIFS. COUNTIFS dùng được
  cho giai đoạn dữ liệu.
- Phát hiện và đã xử lý:
  - Lỗi culture của công thức (mục 4.2) → viết lại G.
  - Shared formula ra 0 nếu thiếu `Recalculate()` (mục 4.3).
  - Định dạng kế toán bị nạp thành số thường → body tự vẽ (mục 4.4).
  - `DrawCellBackground` nhân tỉ lệ hai lần → `FillRectangle` (mục 4.4).
  - Khung chọn ô lọt vào ảnh xuất → `SelectionStyle = None` (mục 4.3).
  - Quy tắc biên của thang màu và data bar trong bản trước của spec sai so với Excel → đã sửa (mục 4.4).
- Workbook bộ nhớ (`ReoGridControl.CreateMemoryWorkbook()`) nạp và tính được trên thread MTA dưới culture vi-VN,
  nên test xUnit dùng được.
- Lệch còn lại: chỗ ngắt dòng của vài tiêu đề nhiều chữ (giới hạn ở mục 1).

## 6. Kiểm thử

- xUnit `tests/AutoJMS.Tests/ThoiHieuKpiConditionalFormatTests.cs` — các bộ số đã đo trên Excel:

  | Giá trị | Màu |
  |---|---|
  | 0, 0, 0, 5 | 0 → F8696B, 5 → 5A8AC6 |
  | 3, 3, 3 | đều 5A8AC6 |
  | 0, 5, 10 | F8696B, FCFCFF, 5A8AC6 |
  | 0, 5, 5, 5 | 0 → F8696B, 5 → 5A8AC6 |
  | 0, 2, 10, 20 | 2 → F99A9C, 10 → CEDCEF |
  | 1, 4, 7, 30, 31 | 4 → FAB2B5, 7 → FCFCFF, 30 → 618FC9 |

  Cộng thêm: ô `null` không tô; `DataBarFraction` ra 0 ở min, 1 ở max, 0.5 ở giữa, 0.5 khi `max == min`, kẹp khi
  ngoài khoảng; `Num` nhận `decimal`/`double`, trả `null` cho chuỗi.
- xUnit `tests/AutoJMS.Tests/ThoiHieuKpiTemplateTests.cs` — nạp file mẫu thật qua `LoadTemplate` vào workbook bộ
  nhớ, dưới culture vi-VN:
  - không ném lỗi; có đúng hai sheet "Tổng", "data"; mọi sheet chỉ đọc;
  - mọi công thức `FormulaStatus.Normal` (bắt lỗi culture nếu ai sửa file mẫu);
  - I5 = tổng K5:AA5 (bắt thiếu `Recalculate`); G3 = F3 × 0.911;
  - `FindTotalRow` = 30; ô nhỏ nhất của K3:AA30 có nền F8696B; dòng Tổng giữ nền FF0000.
- Build Release đạt 0 warning / 0 error; `eng/harness/verify.ps1` đạt cả 5 gate.
- Checklist cho Owner — mở FullStack (ULTRA), vào tab Thời hiệu:
  1. So với Excel bằng mắt ở zoom 70%.
  2. Ctrl + lăn chuột để zoom.
  3. Chuyển sang tab sheet "data".
  4. Thử sửa một ô — không sửa được.
  5. Bấm Xuất ảnh — PNG đúng bảng, không có khung Excel, không có khung chọn ô.
  6. Bấm Mở thư mục.
  7. Đổi sang theme Dark — thanh công cụ đổi màu, bảng vẫn trắng.
  8. Đóng FullStack rồi mở lại.

## 7. Ngoài phạm vi

- Đổ dữ liệu thật. Cột giờ sẽ lấy bằng COUNTIFS trên sheet "data" hay do C# tính thì quyết ở giai đoạn sau; bước 0
  đã xác nhận COUNTIFS chạy được.
- Sửa ô, in, thanh công thức.

## 8. Rủi ro

- ReoGrid V3 Community do cộng đồng bảo trì và ít cập nhật. Lỗi vẽ thì ưu tiên né bằng cách sửa file mẫu.
- ReoGrid không nạp định dạng có điều kiện và định dạng kế toán từ xlsx. Vì vậy chúng do code tự vẽ (mục 4.4).
  Nếu sau này file mẫu thêm luật màu mới thì phải viết thêm code.
- Công thức có số thập phân trong file mẫu làm Load ném lỗi trên máy vi-VN (mục 4.2). Test file mẫu chặn trường
  hợp này.
- Bản cài nặng thêm do có `unvell.ReoGrid.dll`.
