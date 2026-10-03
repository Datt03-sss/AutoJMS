# Tab Thời hiệu dựng lại bằng ReoGrid — bản sao bảng Excel "Ký nhận thời hiệu theo mốc time"

- Ngày: 2026-10-03
- Trạng thái: thiết kế đã được Owner duyệt trong chat 2026-10-03
- Giai đoạn: **layout**. Đổ dữ liệu thật là giai đoạn sau, có spec riêng.

## 1. Mục tiêu

Tab Thời hiệu của `FullStackOperation` hiện **giống 100%** sheet "Tổng" của file Excel thực tế. "Giống" ở đây gồm:
cấu trúc, kích thước cột/hàng, font, màu, viền, định dạng số và chữ — **kể cả lỗi chính tả**. Bảng nằm trong
khung giống cửa sổ Excel. Bước này dùng dữ liệu giả.

Giới hạn: nét chữ (khử răng cưa) có thể lệch Excel vài pixel vì Excel dùng engine chữ riêng. Mọi hướng làm đều
chịu giới hạn này, nên nó không được tính là lệch.

## 2. Quyết định của Owner

| Câu hỏi | Owner chọn |
|---|---|
| Hướng làm | Control spreadsheet bên thứ ba (không nâng renderer GDI+, không WebView2) |
| Control | ReoGrid Community — NuGet `unvell.ReoGrid.dll` 3.3.1, MIT, `net8.0-windows7.0` |
| Khung | Như cửa sổ Excel: chữ cột, số hàng, tab sheet, đường lưới |
| Lỗi của file gốc | Giữ y nguyên ("tỷ lên đạt", "Basline", "22H/23H/24H", lọc C–X, dấu "." ở W34) |
| Phạm vi | "làm lại toàn bộ tabThoiHieu, xóa đi làm lại bằng ReoGid" — xoá hết code tab cũ |
| Thanh công cụ | Chỉ giữ **Xuất ảnh** + **Mở thư mục** (bỏ nút zoom, bỏ dòng trạng thái) |

## 3. Nguồn: file Excel gốc

File Owner gửi là `Ký nhận thời hiệu  theo mốc time new (1).xls` (BIFF8). Để đọc được, file được chuyển sang xlsx
bằng Excel COM (mở read-only, `SaveAs` định dạng 51). File gốc không bị sửa.

Workbook có 7 sheet. Hai sheet đang hiện là "Tổng" và "data". Năm sheet ẩn là "thời hiệu tháng 5", "thời hiệu
tháng 6", "thời hiệu tháng 7", "tháng 8" và "Sheet3".

### 3.1 Sheet "Tổng"

- Zoom 70%, đường lưới bật. Font mặc định của workbook là Arial 10, nên độ rộng chữ số lớn nhất là 7 px.
- Độ rộng cột đổi ra pixel theo công thức `px = trunc((256·w + 18) / 256 · 7)`:

  | A | B | C | D | E | F | G | H | I | J | K | L | M | N | O |
  |---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
  | 46 | 101 | 71 | 93 | 217 | 69 | 86 | 80 | 52 | 76 | 52 | 68 | 51 | 62 | 51 |

  | P | Q | R | S | T | U | V | W | X | Y | Z | AA | AB | AC | AD |
  |---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
  | 54 | 74 | 49 | 54 | 50 | 53 | 51 | 58 | 53 | 52 | 42 | 42 | 56 | 63 | 59 (mặc định) |

- Chiều cao hàng:
  - hàng 1: 81 pt
  - hàng 2: 77.25 pt
  - hàng 3–33: 15 pt
  - hàng 34: 12.6 pt
  - hàng 35: ẩn
- Vùng merge: A1:J1, X1:AA1, AB1:AB2, AC1:AC2, AD1:AD2, B3:B30 (cột SPV), A31:E31, B32:C32, E35:L35.
- AutoFilter: C2:X31.
- Bố cục:
  - **Hàng 1:** tiêu đề A1:J1, rồi các cặp nhãn/giá trị — Hàng mới (K1/L1), Số TTTC quét gửi đến (M1/N1),
    Số đơn chưa đến (O1/P1), Số đơn chuyển tiếp (Q1/R1), Cần phát ra (S1/T1), Cần kí đạt KPI 91% (U1/V1),
    Cần kí đạt KPI 90% (W1/X1:AA1). Sau đó là AB1 "Còn thiếu mốc 91%", AC1 "Còn thiếu mốc Basline 90%" và AD1
    (ô nền xanh).
  - **Hàng 2:** tiêu đề cột — STT, Người phụ trách (SPV), Quét mã ở Bưu cục, Mã nhân viên phát, Nhân viên phát,
    Đơn phát, Số đơn cần kí nhận thời hiệu 91%, Số đơn cần kí Basline 90%, Số vận đơn ký nhận, Tỉ lệ ký nhận,
    8h…21h, 22H, 23H, 24H.
  - **Hàng 3–30:** 28 nhân viên.
  - **Hàng 31:** dòng "Tổng".
  - **Hàng 32–34:** chú thích "Quy ước màu tỷ lệ" — ô xanh 92D050 "tỷ lên đạt 90% trở lên", ô trắng "Tỷ lệ 8x",
    ô đỏ FF0000 "Tỷ lệ <80%".
- Kiểu ô chính:

  | Vùng | Font | Nền | Định dạng số |
  |---|---|---|---|
  | Tiêu đề A1 | Arial 16, chữ trắng | 92D050 | — |
  | Nhãn tóm tắt K1, M1, O1, Q1, S1, U1, W1 | Calibri Light 12 đậm | accent3 (A5A5A5) tint 0.6 | — |
  | Giá trị tóm tắt L1, N1, P1, R1 | Calibri Light 12 đậm, chữ đỏ | FFFF00 | `0` |
  | Giá trị tóm tắt T1, V1, X1 | Calibri Light 12 đậm, chữ đỏ | trắng | `0` |
  | AB1, AC1 | Arial 10 đậm, chữ đỏ | FFFF00 | — |
  | AD1 | Arial 10 | 00B0F0 | — |
  | Hàng 2: A, B | Arial 10 đậm | trắng | — |
  | Hàng 2: C–F | Times New Roman 12 đậm | trắng | — |
  | Hàng 2: G, H | Times New Roman 12 đậm, chữ đỏ | FFFF00 | — |
  | Hàng 2: I–AA | Times New Roman 12 đậm | accent6 (70AD47) tint 0.6 | — |
  | Thân: A–F | Arial 10; E, F không viền (chỉ có đường lưới) | — | — |
  | Thân: G, H | Calibri 11 | — | `0_ ` |
  | Thân: I | Arial 10 | trắng | kế toán; số 0 hiện "-" |
  | Thân: J | Arial 10 | — | `0.0%` |
  | Thân: K–AA | Times New Roman 11 | trắng | kế toán; số 0 hiện "-" |
  | Thân: AB, AC | Arial 10, không viền | — | kế toán |
  | Tổng: A31:E31 | Times New Roman 12, chữ trắng | FF0000 | — |
  | Tổng: F, G, H | Times New Roman 12 | FFFF00 | `0` |
  | Tổng: J | Arial 10 | FFFF00 | `0.00%` |
  | Tổng: K–AA | Times New Roman 11, chữ trắng | FF0000 | kế toán |

  Mọi ô có viền trong bảng đều là viền `thin` bốn cạnh. Nguồn đầy đủ của từng kiểu ô là file mẫu (mục 4.2).

- Định dạng có điều kiện:
  - **J3:J31 — data bar.** Mốc là `min` và `max` của vùng, màu 63C384. Phần mở rộng x14 ghi
    `minLength=0`, `maxLength=100`, `axisPosition=none`, không có thuộc tính border hay gradient, nên dùng mặc
    định x14: thanh gradient, không viền. Lưu ý vùng này **gồm cả dòng Tổng**.
  - **K3:AA30 — thang 3 màu.** `min` → F8696B, `percentile 50` → FCFCFF, `max` → 5A8AC6. Vùng này **không gồm**
    dòng Tổng.
- Công thức:
  - G = F×91.1%, H = F×90%
  - I = SUM(K:AA), J = I/F
  - K..AA = `COUNTIFS(data!C:C, Tổng!E, data!A:A, ">h-1", data!A:A, "<=h")`; riêng cột K chỉ có điều kiện `"<=8"`
  - AB = G−I, AC = H−I
  - Hàng 31 = SUM từng cột
  - T1 = F31, V1 = T1×91%, X1 = T1×90%

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
- Trong `FullStackOperation*.cs`: mọi field, hàm và màu của tab cũ — toolbar, view mode, dòng trạng thái,
  `RefreshThoiHieuKpiSheet`, export, `ThoiHieuPalette`, cùng phần gán palette khi đổi theme.

Phần dựng tab trong `FullStackOperation` vẫn giữ: chèn `_tabThoiHieu` vào vị trí 1 của `uiTabControl1` và bỏ
`tabPage4` khỏi `uiTabControl2`. Khác biệt duy nhất là nội dung tab giờ là `new ThoiHieuKpiView { Dock = Fill }`.

### 4.2 File mẫu `ThoiHieuKpi.template.xlsx`

Đường dẫn: `src/AutoJMS/FullStack/UI/ThoiHieu/ThoiHieuKpi.template.xlsx`, khai báo `EmbeddedResource` trong
`AutoJMS.csproj`.

File được tạo **một lần** từ file gốc bằng Excel COM. Script tạo nằm ở `eng/tmp/` và không commit. Các bước:

1. Xoá 5 sheet ẩn.
2. Với sheet "Tổng":
   - Giữ nguyên mọi thứ ngoài dữ liệu thân bảng: tiêu đề, nhãn, kiểu ô, vùng merge, độ rộng cột, chiều cao hàng,
     zoom, AutoFilter, chú thích, các lỗi chính tả, dấu "." ở W34, hàng 35 ẩn.
   - Thay dữ liệu thật bằng dữ liệu giả cho 28 hàng: B dùng tên SPV giả, C dùng mã bưu cục giả "214A02",
     D dùng mã nhân viên giả, E dùng "Nhân viên 01".."Nhân viên 28".
   - F và K..AA dùng số giả tĩnh; các ô COUNTIFS bị thay bằng giá trị.
   - L1, N1, P1, R1 dùng số giả.
3. Giữ các công thức G, H, I, J, AB, AC, hàng 31 và T1/V1/X1 **nếu** bước 0 cho thấy ReoGrid tính đúng. Nếu không,
   ghi giá trị đã tính sẵn.
4. Với sheet "data": giữ hàng 1–2, xoá từ hàng 3 trở đi.
5. Trước khi commit, kiểm quyền riêng tư: không còn tên nhân viên thật nào. Cách kiểm là so danh sách tên ở cột E
   và `data!C` của file gốc với `xl/sharedStrings.xml` của file mẫu.

Lý do làm sạch: repo **public**, mà file gốc chứa tên khoảng 28 nhân viên thật cùng khoảng 33.000 dòng ký nhận.

Sau này Owner có thể mở file mẫu bằng Excel để chỉnh layout mà không cần sửa code.

### 4.3 `ThoiHieuKpiView` (UserControl)

- Gồm 2 phần: thanh công cụ ở trên (nút **Xuất ảnh**, nút **Mở thư mục** — nút sau tắt cho tới khi xuất ảnh lần
  đầu) và `ReoGridControl` lấp phần còn lại.
- Nạp file mẫu từ manifest resource stream bằng `Workbook.Load(stream, FileFormat.Excel2007)`. Mở ở sheet "Tổng",
  zoom 70%; zoom bằng Ctrl + lăn chuột của ReoGrid.
- Hiện chữ cột, số hàng, tab sheet "Tổng | data" và đường lưới.
- Chỉ đọc: bật `WorksheetSettings.Edit_Readonly` cho cả hai sheet. Người dùng chọn ô được nhưng không sửa được.
- Sau khi nạp, gọi `ThoiHieuKpiConditionalFormat.Apply(worksheet "Tổng")`.
- Theme:
  - Bảng luôn giữ kiểu Excel nền trắng, kể cả ở theme Dark.
  - Thanh công cụ theo theme app, dùng cùng kiểu nút với các nút khác của FullStack.
- Xuất ảnh:
  - Vẽ vùng đã dùng của "Tổng" ở 100%, không kèm chữ cột, số hàng hay tab, ra file PNG trong
    `AppPaths.UserDataDir\FullStack\Exports\ThoiHieu` (cùng thư mục với bản cũ).
  - Xong thì hiện toast "Đã xuất ảnh thời hiệu." và bật nút **Mở thư mục**.
  - Lỗi thì ghi `AppLogger.Error` và hiện MessageBox như bản cũ.
  - Cách vẽ: dựng một `ReoGridControl` ẩn, tắt header/tab, có kích thước bằng toàn vùng đã dùng, rồi gọi
    `DrawToBitmap`.
- Kích thước tạo bằng code (chiều cao toolbar, padding) phải qua `S()`. Lý do: `AutoScaleMode.Dpi` chỉ nhân
  control do `InitializeComponent` tạo.

### 4.4 `ThoiHieuKpiConditionalFormat` (static, logic thuần)

- `Color[] ColorScale3(IReadOnlyList<double?> values)` — thang 3 màu đúng cách Excel làm:
  - Mốc giữa là percentile 50 theo kiểu PERCENTILE.INC (nội suy tuyến tính).
  - Giá trị ≤ mốc giữa thì nội suy RGB từ F8696B sang FCFCFF; giá trị ≥ mốc giữa thì nội suy từ FCFCFF sang
    5A8AC6.
  - Ô rỗng hoặc không phải số: không tô.
  - Mọi giá trị bằng nhau: tô màu giữa FCFCFF.
- `double DataBarFraction(double value, double min, double max)` — trả `(value − min) / (max − min)`, kẹp trong
  [0, 1]; khi `max == min` thì trả 1.
- `Apply(Worksheet)`:
  - Tìm dòng Tổng: ô cột A có chữ "Tổng".
  - Tô thang màu cho K3:AA(Tổng−1) bằng cách đặt màu nền từng ô.
  - Gắn data bar cho J3:J(Tổng) bằng một `CellBody` tự vẽ: vẽ nền, rồi thanh gradient từ 63C384 sang gần trắng,
    rồi chữ.
  - Vì chỉ đọc giá trị đang có trong ô, giai đoạn đổ dữ liệu thật gọi lại được nguyên hàm này.

Hai hành vi biên ("mọi giá trị bằng nhau" và `max == min`) được đối chiếu với Excel ở bước 0. Nếu Excel làm khác
thì sửa theo Excel.

## 5. Bước 0 — chạy thử để kiểm chứng (trong `eng/tmp/`, không commit)

1. Dựng một app WinForms nhỏ dùng `unvell.ReoGrid.dll` 3.3.1, nạp file mẫu, chụp sheet "Tổng" ở 100%.
2. Lấy ảnh tham chiếu bằng Excel COM: `Range("A1:AD34").CopyPicture` từ chính file mẫu.
3. So hai ảnh theo các tiêu chí:
   - vùng merge;
   - font, cỡ, đậm;
   - màu nền, kể cả màu theme có tint;
   - viền thin; ô E, F không viền nhưng có đường lưới;
   - chiều cao hàng 81 / 77.25 pt;
   - xuống dòng ở tiêu đề;
   - định dạng số: "-" cho số 0, "71.2%", "90.24%", `0_ `;
   - công thức có hậu tố `%` (`F3*91.1%`) và SUM;
   - hàng 35 ẩn.
4. Ghi thêm: ReoGrid 3.3.1 có COUNTIFS không. Kết quả này dùng cho giai đoạn dữ liệu.

Kết quả:

- Đạt → tiếp tục.
- Lệch nhưng sửa được trong file mẫu (ví dụ đổi màu theme thành RGB cố định, công thức thành giá trị) → sửa file
  mẫu, không vá bằng code.
- Lệch mà không sửa được (merge, font hay chiều cao sai và API không chỉnh được) → **dừng, báo Owner**.

## 6. Kiểm thử

- xUnit `tests/AutoJMS.Tests/ThoiHieuKpiConditionalFormatTests.cs`:
  - `ColorScale3`: min ra F8696B, trung vị ra FCFCFF, max ra 5A8AC6, điểm ở giữa được nội suy, ô rỗng bị bỏ qua,
    mọi giá trị bằng nhau ra FCFCFF.
  - `DataBarFraction`: min ra 0, max ra 1, điểm giữa, `max == min`.
- Build Release đạt 0 warning / 0 error; `eng/harness/verify.ps1` đạt cả 5 gate.
- Checklist cho Owner — mở FullStack (ULTRA), vào tab Thời hiệu:
  1. So với Excel bằng mắt ở zoom 70%.
  2. Ctrl + lăn chuột để zoom.
  3. Chuyển sang tab sheet "data".
  4. Thử sửa một ô — không sửa được.
  5. Bấm Xuất ảnh — PNG đúng bảng, không có khung Excel.
  6. Bấm Mở thư mục.
  7. Đổi sang theme Dark — thanh công cụ đổi màu, bảng vẫn trắng.
  8. Đóng FullStack rồi mở lại.

## 7. Ngoài phạm vi

- Đổ dữ liệu thật. Cột giờ sẽ lấy bằng COUNTIFS trên sheet "data" hay do C# tính thì quyết ở giai đoạn sau, dựa
  vào kết quả bước 0.
- Sửa ô, in, thanh công thức.

## 8. Rủi ro

- ReoGrid V3 Community do cộng đồng bảo trì và ít cập nhật. Lỗi vẽ thì ưu tiên né bằng cách sửa file mẫu.
- Tài liệu của ReoGrid ghi rằng định dạng có điều kiện không được nạp từ xlsx. Vì vậy data bar và thang màu do
  code tự vẽ (mục 4.4). Nếu sau này file mẫu thêm luật màu mới thì phải viết thêm code.
- `AppTheme.Apply` duyệt cây control và kéo mọi font không phải token về 10F Regular. Nếu nó đổi Font hoặc
  BackColor của `ReoGridControl` thì phải loại control này khỏi lượt duyệt.
- Bản cài nặng thêm do có `unvell.ReoGrid.dll`.
