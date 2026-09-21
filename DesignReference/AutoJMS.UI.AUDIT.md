# AutoJMS — UI Architecture Audit

> **Phase 1 của task redesign UI.** Tài liệu này CHỈ mô tả hiện trạng và rủi ro.
> Không có thay đổi business logic nào được thực hiện trong phase này.
>
> - Ngày audit: **2026-09-22**
> - Commit gốc: `10dab5f` (`main`)
> - Baseline build: `dotnet build ./AutoJMS.slnx -c Release` → **0 Warning, 0 Error** (24,5s)
> - Người thực hiện: Claude Code (`ui-redesign-designsystem`)

---

## 0. Bốn điều chỉnh so với đề bài

Đề bài mô tả một số thứ khác với hiện trạng repo. Ghi ở đây trước vì chúng
thay đổi phạm vi công việc, không phải chi tiết vụn.

| Đề bài nói | Thực tế trong repo | Ảnh hưởng |
|---|---|---|
| "C# .NET 10 WinForms" | `src/AutoJMS/AutoJMS.csproj` → `net8.0-windows`. .NET 10 chỉ dùng cho `AutoJMS.DataHub.Api` (backend). | Không dùng được API chỉ có ở .NET 9/10. Không phải lỗi — chỉ cần biết đúng target. |
| "Đọc `./DesignReference/Pinterest.DESIGN.md` trước" | **File không tồn tại** ở bất kỳ đâu trong repo (`git ls-files` + `find` đều rỗng). Thư mục `DesignReference/` cũng chưa có. | Xem §9. |
| "Login screen render qua WebView2" | Login của **AutoJMS** là `frmLogin` — WinForms + SunnyUI thuần (`Forms/frmLogin.cs`), thực chất là dialog **kích hoạt license**. WebView2 trong app trỏ tới `https://jms.jtexpress.vn` — cổng JMS của J&T. | Xem §9. Màn login mà đề bài mô tả (ngôn ngữ / nhớ tài khoản / quên mật khẩu) là trang của **bên thứ ba**, không thuộc sở hữu AutoJMS. |
| "CHUYỂN HOÀN có main table / DataGridView" | `tabDKCH` **không có DataGridView nào**. Bề mặt chính của nó là `tabDKCH_webView` (WebView2). DataGridView nằm ở `tabTracking` và `tabPrint`. | Xem §8 — CHUYỂN HOÀN không phải màn đại diện tốt cho yêu cầu grid. |

---

## 1. Cấu trúc UI hiện tại

### 1.1 Hai form gốc, hai hệ theme độc lập

```
Program.cs
  ├─ frmLogin (UIForm)              ← dialog kích hoạt license, SunnyUI thuần
  └─ Main (UIForm)                  ← shell chính
       └─ tabControl : UITabControl
            ├─ tabHome        HOME
            ├─ tabDKCH        CHUYỂN HOÀN
            ├─ tabTracking    TRA HÀNH TRÌNH
            ├─ tabPrint       IN ĐƠN
            │    └─ tabPrint_printFunc : UITabControl   ← tab con
            │         ├─ tabPrint_inCH / inCT / inLaiDon / inRV
            └─ tabAbout       ABOUT   ← luôn là tab cuối (Tab Boundary Rule)

FullStackOperation (UIForm)         ← form riêng, tier ULTRA, KHÔNG dùng AppTheme
```

`Main` là `partial` trải trên 7 file:

| File | Dòng | Vai trò |
|---|---:|---|
| `Forms/Main.cs` | 5.850 | Logic chính + điều phối theme + network + grid |
| `Forms/Main.Designer.cs` | 1.953 | Toàn bộ layout kéo-thả của 5 tab |
| `Forms/Main.TabPrintReverse.cs` | 1.720 | Cụm "In hàng hoàn" dựng bằng code |
| `Forms/Main.DkchNewbill.cs` | 1.617 | Panel NEWBILL của DKCH, tự vẽ |
| `Forms/Main.TabPrint.cs` | 1.369 | Tab IN ĐƠN |
| `Forms/Main.DkchData.cs` | 1.044 | Mục DATA của DKCH, tự vẽ hoàn toàn |
| `Forms/Main.DkchLeftPanel.cs` | 12 | **File rỗng có chủ đích** — xem §6.1 |

`FullStackOperation` là `partial` trải trên 12 file, tổng ~4.300 dòng chỉ riêng file chính.

### 1.2 Nền tảng control: SunnyUI 3.9.6, không phải WinForms thuần

`PackageReference Include="SunnyUI" Version="3.9.6"`. Đếm số lần xuất hiện trong `src/`:

```
41  UITableLayoutPanel      8  UIDataGridView      3  UIComboBox
36  UISymbolButton          8  UIButton            2  UISwitch
32  UILabel                 7  UIRichTextBox       2  UIProcessBar
27  UIPanel                 6  UITitlePanel        2  UIIntegerUpDown
                            6  UITextBox           2  UICheckBox
                            4  UITabControl
```

Đây là ràng buộc nền tảng quan trọng nhất của cả task:

- **`UITabControl` là `sealed`.** Không kế thừa được. Đã được ghi lại bằng thực nghiệm trong
  `UI/PremiumTabAccent.cs:11-20`: nó không gọi `base.OnDrawItem` nên event `DrawItem` không
  bao giờ bắn, và nó vẽ đầu tab *sau* khi `Paint` chạy xong nên nét vẽ trong `Paint` bị phủ.
  Giải pháp đang chạy là hook `WM_PAINT` qua `NativeWindow` — tức là đã phải đi đường vòng
  ở mức Win32 chỉ để vẽ một cái viền.
- Các control SunnyUI khác tự vẽ (`FillColor`/`RectColor`/`ForeColor`…), không dùng
  `BackColor`/`ForeColor` chuẩn WinForms. Một `ControlStyler` chung phải biết từng kiểu.

### 1.3 Control nào ở tab nào

| Tab | Control (Designer) | Ghi chú |
|---|---|---|
| **HOME** | 5 `UISymbolButton`, 2 `UIPanel`, 1 `UITextBox`, 1 `UITableLayoutPanel`, 1 **WebView2** (`tabHome_webView`) | Thanh điều hướng trình duyệt (back/forward/reload/home) + WebView chiếm phần lớn diện tích |
| **CHUYỂN HOÀN** | 3 `UIButton` (`btnDKCH1`/`btnDKCH2`/`btnStop`), 1 `UISymbolButton` (`Home`), 1 `UITitlePanel` (`dataSrc`), 1 **WebView2** (`tabDKCH_webView`) | Phần DATA và NEWBILL **không có trong Designer** — dựng bằng code, xem §1.4 |
| **TRA HÀNH TRÌNH** | 6 `UISymbolButton`, 1 `UIRichTextBox`, 1 `UIProcessBar`, 1 `UILabel` (`countSum`), 1 **`UIDataGridView`** (`tabTracking_dataView`) | Màn duy nhất có grid + input + counter + progress trong Designer |
| **IN ĐƠN** | 1 `UITabControl` + 4 `TabPage`, 3 `UILabel`, 2 `UISymbolButton`, 1 `UISwitch`, 1 `UIRichTextBox`, 1 `UIImageButton`, 1 `UICheckBox`, 1 **`UIDataGridView`**, 1 **WebView2** | Cụm "In hàng hoàn" (`Main.TabPrintReverse.cs`) dựng bằng code, không qua Designer |
| **ABOUT** | 1 `UISymbolButton`, 1 `UILabel`, 1 `UIButton` | Gọn nhất, rủi ro thấp nhất |

### 1.4 Ba lớp dựng UI cùng tồn tại

1. **Designer kéo-thả** (`Main.Designer.cs`) — vị trí pixel cứng.
2. **Code dựng SunnyUI** (`Main.TabPrintReverse.cs`, `FullStackOperation.*`).
3. **Control tự vẽ thuần WinForms** (`DkchDropDown`, `DkchSpin`, `DkchToggle` trong
   `Main.DkchData.cs`; `DkchSkin` trong `Main.DkchNewbill.cs`) — `UserPaint` +
   `OptimizedDoubleBuffer`, không đụng SunnyUI.

Design System được yêu cầu sẽ là **lớp thứ tư**. Xem §7.

---

## 2. Component tái dùng đang có

| Thành phần | Vị trí | Trạng thái |
|---|---|---|
| `AppTheme` | `UI/AppTheme.cs` (813 dòng) | **Đang chạy.** 3 theme (Light/Red/**Dark đã có sẵn**), token ngữ nghĩa, walker đệ quy |
| `AppPalette` | `UI/AppPalette.cs` (18 màu) | **CODE CHẾT — 0 call site.** Xác nhận bằng `grep -rn 'AppPalette\.'` → 0 |
| `PremiumTabAccent` | `UI/PremiumTabAccent.cs` | Đang chạy, viền vàng tab con IN ĐƠN |
| `UiThread`, `WebViewHost`, `uiControlService` | `UI/` | Helper nhỏ, không phải UI component |
| `KpiCardControl` | `FullStack/UI/OperationCenter/` | Chỉ FullStackOperation dùng |
| `GridFilterToolbarControl` | `FullStack/UI/OperationCenter/` | Chỉ FullStackOperation dùng |
| `QueueSidebarControl`, `StatusFooterControl`, `WaybillDetailPanel` | `FullStack/UI/OperationCenter/` | Chỉ FullStackOperation dùng |
| `ThoiHieuKpiGridRenderer` + `ThoiHieuKpiColorPalette` | `FullStack/UI/ThoiHieu/` | Renderer riêng, bảng màu riêng |
| `DkchDropDown` / `DkchSpin` / `DkchToggle` | `Forms/Main.DkchData.cs` | Custom control tự vẽ, chỉ DKCH dùng |
| `DkchSkin` | `Forms/Main.DkchNewbill.cs` | Bảng màu riêng cho NEWBILL, chọn theo `AppTheme.CurrentTheme` |
| `TermsDialog`, `UpdateChannelDialog` | `Forms/` | Dialog tự dựng, style ad-hoc |

**Kết luận:** đã có component tái dùng, nhưng **không cái nào dùng chung giữa `Main` và
`FullStackOperation`**. Hai form là hai thế giới tách biệt.

---

## 3. Điểm không nhất quán về mặt thị giác

### 3.1 Hai hệ màu song song, không liên quan gì nhau

`FullStackOperation.Theme.cs` khai báo bảng màu **riêng**, không hề đọc `AppTheme`:

| Vai trò | `AppTheme` (Light) | `FullStackOperation.Theme.cs` |
|---|---|---|
| Accent chính | `#3B82F6` | `#2F6FED` (`AccentBlue`) |
| Nền app | `#F5F7FA` | `#F4F6F9` (`FullStackBackColor`) |
| Viền | `#E5E7EB` | `#E4E8EF` (`BorderColor`) |
| Text phụ | `#6B7280` | `#6B7588` (`TextSecondary`) |
| Header | — | `#11243F` (`HeaderDark`) — navy, không có ở AppTheme |

Đây là các cặp màu **gần giống nhưng khác nhau** — loại lệch khó phát hiện bằng mắt nhất và
chắc chắn sẽ lệch tiếp mỗi lần sửa một bên.

`FullStackOperation.Theme.cs` cũng **không có biến thể Dark**, trong khi `Main` có.
→ Bật Dark theme, mở FULLSTACK_OPERATION thì form đó vẫn sáng trắng.

### 3.2 Bốn họ font trong cùng một app

```
94  new Font("Segoe UI Semibold", ...)
86  new Font("Segoe UI", ...)
47  new Font("Microsoft Sans Serif", ...)   ← chủ yếu trong Main.Designer.cs
 1  new Font("Tahoma", ...)
```

`Microsoft Sans Serif` là font mặc định WinForms từ .NET Framework — nó ở đây vì Designer
sinh ra, không phải vì ai chọn.

### 3.3 Màu cứng rải khắp nơi

| File | Số lần hard-code màu |
|---|---:|
| `Forms/Main.Designer.cs` | 149 |
| `UI/AppTheme.cs` | 142 (hợp lệ — đây là nguồn token) |
| `Forms/FullStackOperation.cs` | 137 |
| `FullStack/UI/OperationCenter/WaybillDetailPanel.cs` | 45 |
| `Forms/FullStackOperation.WaybillWorkspace.cs` | 39 |
| `Forms/UpdateChannelDialog.cs` | 33 |
| `FullStack/UI/OperationCenter/GridFilterToolbarControl.cs` | 21 |
| `Forms/Main.cs` | 16 |
| `Forms/TermsDialog.cs` | 15 |

Ví dụ điển hình — `Main.cs:1150-1174` (`UpdateNetworkUI`) rẽ nhánh theo theme rồi gán màu
literal, thay vì đọc `Success`/`Warning`/`Danger` từ token:

```csharp
if (isRed) lblNetworkStatus.ForeColor = Color.White;
else if (isDark) lblNetworkStatus.ForeColor = Color.LimeGreen;
else lblNetworkStatus.ForeColor = Color.FromArgb(0, 240, 100);
```

### 3.4 Style theo **tên control** — coupling nguy hiểm nhất

`AppTheme.ApplyStyleToControl` phân nhánh bằng chuỗi tên:

```csharp
if (sbtn.Name == "tabDKCH_Home")                    // AppTheme.cs:217
if (btn.Name == "tabDKCH_btnDKCH1" || ... )         // AppTheme.cs:267
if (lbl.Name == "tabTracking_countSum")             // AppTheme.cs:295
if (tab.Name == "tabPrint_printFunc")               // AppTheme.cs:326
if (rtxt.Name == "tabDKCH_inputNewBill" || ...)     // AppTheme.cs:405
if (cb.Name == "tabDKCH_sheetName" || ...)          // AppTheme.cs:488
if (pnl.Name == "tabHome_pnlLeft")                  // AppTheme.cs:462
if (pnl.Name == "uiPanel19" || pnl.Name == "uiPanel20")  // AppTheme.cs:468
```

Hệ quả: **đổi tên một control trong Designer là mất style của nó, im lặng, không lỗi biên dịch.**
`uiPanel19`/`uiPanel20` còn tệ hơn — đó là tên Designer tự sinh, không mang nghĩa gì.

---

## 4. Rủi ro hiệu năng

### 4.1 `AppTheme` cấp phát một `Font` cho **mỗi** control, **mỗi** lần apply

`UI/AppTheme.cs:209`:

```csharp
ctrl.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
```

Dòng này nằm trong walker đệ quy, chạy cho **mọi** control trong cây. `Font` là đối tượng
ôm GDI handle và **không bao giờ được `Dispose`**. Font cũ cũng không được thu hồi.

`AppTheme.Apply(this)` được gọi **ít nhất 3 lần**: `Main.cs:209`, `Main.cs:613`,
`Main.cs:1290` (mỗi lần đổi theme). Với cây control của `Main`, đây là hàng trăm GDI object
rò mỗi lần đổi theme. Trên máy cấu hình thấp — đúng đối tượng người dùng của AutoJMS — đây
là rủi ro thật, không phải lý thuyết.

`ApplyStandardGridSettings` (`Main.cs:1007-1008`) cũng cấp phát 2 `Font` mới mỗi lần gọi.

### 4.2 `Panel_ControlAdded` — handler tự nhân bản

`UI/AppTheme.cs:460-461`:

```csharp
pnl.ControlAdded -= Panel_ControlAdded;
pnl.ControlAdded += Panel_ControlAdded;
```

Cách `-=` trước `+=` là đúng và chặn được nhân đôi. Nhưng hệ quả thiết kế thì vẫn còn:
**mỗi control được thêm vào bất kỳ `UIPanel` nào, ở bất kỳ lúc nào, đều kéo theo một lượt
style đệ quy** — kể cả khi đang nạp dữ liệu. Với panel nạp động nhiều control thì đây là
O(n²) ẩn.

### 4.3 DataGridView — chưa sẵn sàng cho 100k dòng

| Điểm | Vị trí | Đánh giá |
|---|---|---|
| `VirtualMode = false` (đặt tường minh) | `Tracking/WaybillTrackingService.cs:68` | Mọi dòng là một `DataGridViewRow` thật. 100k dòng ≈ 100k object + cell object. |
| `AutoSizeColumnsMode = DisplayedCells` | `Main.cs:1002`, `FullStackOperation.cs:458` | Chấp nhận được (chỉ đo dòng hiển thị). |
| `AutoSizeColumnsMode = ColumnHeader` | `Main.Designer.cs:1208` | Bị `Main.cs:1000-1002` ghi đè lúc runtime — designer value là code chết. |
| **`AutoResizeColumns(AllCells)`** | `Printing/PrintService.cs:156` | **Rủi ro cao.** `AllCells` đo **mọi** dòng. Trên tập lớn đây là đóng băng UI thấy rõ. |
| `RowTemplate.Height = 27` cố định | `Main.cs:1004` | Tốt cho hiệu năng (chiều cao ổn định). Nhưng là pixel cứng → không scale theo DPI. |
| Font grid `7.5F` / `8.5F` | `Main.cs:1006-1008` | Rất nhỏ. Ở 100% DPI trên màn laptop là khó đọc. Xem §5.2. |
| `StripeOddColor/StripeEvenColor = White` | `Main.cs:1013-1014` | **Nghi vấn bug Dark theme** — xem §4.4. |

### 4.4 Nghi vấn: grid trắng trong Dark theme *(cần Owner smoke test xác nhận)*

Thứ tự gọi trong `Main.cs`:

```
613:  UI.AppTheme.Apply(this);              → dgv.StripeEvenColor = colors.GridAlternating (#131316 ở Dark)
708:  ApplyStandardGridSettings(tabTracking_dataView);   → uiGrid.StripeOddColor  = Color.White
709:  ApplyStandardGridSettings(tabPrint_dataView);      → uiGrid.StripeEvenColor = Color.White
```

`ApplyStandardGridSettings` chạy **sau** và ghi đè stripe color về trắng vô điều kiện.
`UIDataGridView` của SunnyUI dùng stripe color trong hàm vẽ của nó. Nếu đúng như đọc code,
hai grid này sẽ ra **nền trắng, chữ sáng** ở Dark theme — gần như không đọc được.

Đây là suy luận từ code, **chưa chạy app để xác nhận**. Ghi lại như một mục cần kiểm chứng,
không phải một khẳng định.

### 4.5 `PremiumTabAccent` vẽ trên mỗi `WM_PAINT`

`UI/PremiumTabAccent.cs:79-85` — mỗi `WM_PAINT` tạo `Graphics.FromHwnd`, `LinearGradientBrush`,
2 `Pen`. Có `using` đầy đủ nên không rò, nhưng đây là cấp phát trên đường vẽ nóng. Chấp nhận
được với 1 control; **không được nhân rộng mô hình này** cho control mới.

---

## 5. DPI và độ phân giải

### 5.1 Vấn đề nghiêm trọng nhất: `AutoScaleMode.None`

| Vị trí | Giá trị |
|---|---|
| `AutoJMS.csproj` | `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` |
| `Program.cs:88` | `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` |
| **`Main.Designer.cs:1789`** | **`AutoScaleMode = AutoScaleMode.None;`** |
| **`frmLogin.Designer.cs:157`** | **`AutoScaleMode = AutoScaleMode.None;`** |
| `FullStackOperation.Layout.cs:16` | `AutoScaleMode = AutoScaleMode.Dpi;` ← form duy nhất scale đúng |

Tổ hợp này là trường hợp xấu nhất có thể:

- `PerMonitorV2` nói với Windows: *"app này tự lo DPI, đừng kéo giãn bitmap hộ."*
- `AutoScaleMode.None` nói với WinForms: *"đừng scale layout."*

→ **Không ai scale cả.** Ở 150% DPI, `Main` vẫn vẽ ở toạ độ 96 DPI trên một màn hình
vật lý lớn hơn 1,5 lần. Chữ và control nhỏ lại bằng 2/3 kích thước đáng có.

Đây là lý do gốc khiến grid font phải để `7.5F` — cả layout đang bị nén.

### 5.2 Chỗ nào đã xử lý DPI đúng

Chỉ một chỗ duy nhất trong toàn bộ UI — `UI/PremiumTabAccent.cs:92`:

```csharp
double dpiScale = _tab.DeviceDpi / 96.0;
int thickness = Math.Max(BaseBorderWidth, (int)Math.Round(BaseBorderWidth * dpiScale, ...));
```

Đây là mẫu đúng và là thứ `DpiHelper` nên chuẩn hoá lại.

### 5.3 Toạ độ pixel cứng trong code runtime

`Main.cs:1181-1205` (`AlignLeftPanelControls`):

```csharp
tabDKCH_Home.Size = new Size(258, 32);
tabDKCH_btnDKCH1.Height = 32;
uiTableLayoutPanel9.RowStyles[0] = new RowStyle(SizeType.Absolute, 38F);
```

`Main.DkchData.cs:53-63` — `DkchGapLabel = 5`, `DkchGapGroup = 12`, `DkchSwitchW = 40`,
`DkchDesignWidth = 270`… tất cả là hằng số pixel ở 96 DPI.

---

## 6. Rủi ro migration

### 6.1 ⚠️ Migration này ĐÃ TỪNG THẤT BẠI BỐN LẦN — có tài liệu

Đây là phát hiện quan trọng nhất của toàn bộ audit.

`Forms/Main.DkchLeftPanel.cs` hiện là **file rỗng có chủ đích**:

> ```
> // Đã gỡ bỏ.
> // File này từng chứa DkchGroupPanel/DkchButton dùng để thay Sunny.UI cho cả panel trái.
> // Chủ dự án yêu cầu trả DATA và CONTROL về đúng bản trước đó (vẫn dùng Sunny.UI
> // UITitlePanel/UIButton do Designer tạo) và chỉ tiếp tục chỉnh phần NEWBILL.
> ```

Tức là: **chính xác cái việc "thay control SunnyUI bằng control của Design System trong tab
DKCH" đã được làm rồi, và Owner đã yêu cầu rollback.**

`Forms/Main.DkchData.cs:15-28` ghi lại bốn lần thử và lý do hụt:

> 1. Kéo-thả (`uiTableLayoutPanel8`): bề rộng cột là pixel cứng, không liên quan chữ thật.
> 2. Tự đo + Sunny.UI: **AppTheme gán `Font = "Segoe UI" 10F` cho MỌI control SAU khi layout
>    xong, nên số đo trước đó thành vô nghĩa.**
> 3. AutoSize + Sunny.UI: nhãn hết cắt, nhưng `UIComboBox` tự vẽ text bên trong → vẫn cắt.
> 4. ComboBox/NumericUpDown hệ thống: hết cắt chữ, nhưng WinForms vẽ lại cả control mỗi lần
>    hover (kèm bước xoá nền) nên nháy.

**Bất kỳ kế hoạch redesign nào không giải quyết điểm 2 đều sẽ thất bại lần thứ năm.**

### 6.2 Vòng đời "apply rồi vá lại"

Vì `AppTheme.Apply` ghi đè font và màu của mọi thứ, mỗi vùng dựng-bằng-code phải tự sửa lại
sau đó. Trích `Main.cs:209-215` — kèm comment của chính tác giả:

```csharp
UI.AppTheme.Apply(this);
ApplyWaybillInputBoldFonts();
// AppTheme gán Font = "Segoe UI" 10F cho MỌI control, nên phải đo và xếp lại
// mục DATA SAU khi theme chạy, nếu không chữ sẽ bị cắt.
LayoutDkchDataSection();
LayoutDkchNewbill();
```

Và `Main.cs:611-618` lặp lại y hệt, cộng thêm `ApplyReverseTheme()`.

`Main.TabPrintReverse.cs:543` xác nhận cùng một mô hình:

> *"…ngay sau mỗi `AppTheme.Apply`: theme chỉ nhận ra control SunnyUI nên không tô…"*

**Mỗi vùng UI mới sinh ra một lời gọi vá mới.** Đây là nợ kiến trúc lớn nhất, và nó sẽ áp
nguyên vẹn lên mọi control Design System mới trừ khi `AppTheme` được sửa trước.

### 6.3 Protected Files chặn hầu hết Phase 6–7

Theo `CLAUDE.md` § Protected Files:

- `src/AutoJMS/Forms/Main.cs` — **Protected**
- `src/AutoJMS/Forms/Main.Designer.cs` — **Protected**
- `src/AutoJMS/Program.cs` — **Protected**

Nhưng:

- **App shell** (Phase 6) *là* `Main` + `Main.Designer.cs`.
- **CHUYỂN HOÀN** (Phase 7, màn đại diện) sống hoàn toàn trong `Main.Designer.cs`
  (3 nút + WebView) và `Main.cs` / `Main.DkchData.cs` / `Main.DkchNewbill.cs`.
- Sửa `AutoScaleMode.None` (§5.1) là sửa `Main.Designer.cs:1789`.

→ Phase 3–5 (theme + control + component) làm được hoàn toàn bằng **file mới**, không đụng
file nào bị bảo vệ. Phase 6–7 thì **không**.

### 6.4 WebView2 và automation bằng selector

`Automation/WebViewAutomation.cs` (1.647 dòng) điều khiển `tabDKCH_webView` bằng selector DOM
trên trang `jms.jtexpress.vn`. Bất kỳ thay đổi nào tới DOM/CSS của WebView đều có nguy cơ
làm hỏng automation. `modules/selectors.json` chứa selector và được cập nhật từ xa.

**Control không được redesign vì lý do chức năng:**

| Control | Lý do |
|---|---|
| `tabDKCH_webView`, `tabHome_webView` | Bề mặt automation. Không đổi kích thước/DPI behavior mà chưa kiểm thử. |
| `tabDKCH_btnDKCH1` / `btnDKCH2` / `btnStop` | Trạng thái enabled/disabled phản ánh trạng thái workflow đang chạy. Màu xanh/vàng/đỏ ở `AppTheme.cs:706-738` **mang nghĩa nghiệp vụ**, không phải trang trí. |
| `lblNetworkStatus` | `AppTheme.cs:303-306` cố tình bỏ qua — do `Main.cs` quản. |
| `tabTracking_dataView` | Grid nóng nhất. Mọi thay đổi vẽ đều ảnh hưởng tập lớn. |
| `frmLogin` `txt_key` / `txt_hwid` | Watermark được cài bằng tay (`frmLogin.cs:48-64`) và so sánh **bằng chuỗi** (`key == watermarkText`). Đổi watermark là đổi validation. |

### 6.5 Va chạm tên: `AppTheme` và `ThemeColors`

Kiến trúc đề bài yêu cầu `UI/DesignSystem/Theme/AppTheme.cs` và `ThemeColors.cs`.
Repo **đã có** `AutoJMS.UI.AppTheme` với lớp lồng `AppTheme.ThemeColors`.

Tạo `AutoJMS.UI.DesignSystem.AppTheme` là hợp lệ về mặt C#, nhưng sẽ có **hai kiểu tên
`AppTheme` trong hai namespace lồng nhau** — chính là loại nhập nhằng mà 7 file `partial`
của `Main` không chịu nổi. Cần quyết định tên trước khi viết dòng code đầu tiên.

---

## 7. Rủi ro kiến trúc

**Design System được yêu cầu sẽ là lớp UI thứ tư chồng lên ba lớp đang có.**

```
WinForms native  →  SunnyUI 3.9.6  →  Dkch* tự vẽ  →  A* (Design System)
```

Với `AButton`, `ATextBox`… bọc control SunnyUI, chuỗi trở thành: `AButton` → `UIButton` →
`Control`. Mỗi tầng có mô hình vẽ riêng, mô hình màu riêng (`FillColor` vs `BackColor`),
mô hình font riêng. Debug một vấn đề hiển thị sẽ phải đi qua cả bốn tầng.

Ba cách xử lý:

| Hướng | Mô tả | Đánh đổi |
|---|---|---|
| **A. Token-only** | Không tạo `A*` control. Chỉ tạo tầng token + `ControlStyler` đọc token, rồi **sửa `AppTheme` để đọc từ token** thay vì hằng số. | Diff nhỏ nhất, rủi ro thấp nhất, giải quyết §6.2 tận gốc. Không có `A*` control như đề bài. |
| **B. Wrapper** | `A*` bọc SunnyUI, consume token. | Đúng đề bài. Thêm một tầng. `ATabControl` **không làm được** (`UITabControl` `sealed`). |
| **C. Thay thế** | `A*` kế thừa `Control`, tự vẽ, bỏ SunnyUI dần. | Sạch nhất về lâu dài. Chính là cái **đã bị rollback ở §6.1**. |

---

## 8. CHUYỂN HOÀN có phải màn đại diện tốt không?

Đề bài chọn CHUYỂN HOÀN vì nó *"chứa navigation, search/control area, buttons, counters,
operational workflow, DataGridView, status information"*.

Đối chiếu thực tế:

| Đề bài kỳ vọng | Có trong `tabDKCH`? |
|---|---|
| navigation | ✅ (`tabDKCH_Home`) |
| search/control area | ✅ (panel trái: DATA + CONTROL + NEWBILL) |
| buttons | ✅ (`btnDKCH1`/`btnDKCH2`/`btnStop`) |
| counters | ✅ (`tabDKCH_countSum`, `tabDKCH_countSave`) |
| operational workflow | ✅ |
| **DataGridView** | ❌ **Không có.** Bề mặt chính là WebView2. |
| status information | ✅ |

**Đề xuất:** giữ CHUYỂN HOÀN làm màn pilot (nó đúng là nơi hội tụ nhiều mẫu UI nhất), nhưng
**không thể dùng nó để kiểm chứng `ADataGridView`**. `TRA HÀNH TRÌNH` (`tabTracking`) là màn
duy nhất có đủ grid + input + counter + progress bar trong một chỗ — đó mới là nơi kiểm
chứng yêu cầu grid.

---

## 9. Hai thứ trong đề bài không thực hiện được như mô tả

### 9.1 `DesignReference/Pinterest.DESIGN.md` không tồn tại

Đã tìm bằng `git ls-files`, `find . -iname "*pinterest*"`, `find . -iname "*DESIGN*"` → không
có. Thư mục `DesignReference/` cũng chưa tồn tại trước task này.

Mức độ ảnh hưởng: **thấp**. Đề bài đã liệt kê sẵn 15 nguyên tắc cần rút ra từ file đó, và
nhấn mạnh *"phải giống AutoJMS, không phải Pinterest"* + *"đừng tối ưu cho độ giống Pinterest"*.
`AutoJMS.DESIGN.md` viết được từ danh sách nguyên tắc đó cộng với hiện trạng trong tài liệu này.
Nếu Owner có file thật thì đưa vào, sẽ đối chiếu lại.

### 9.2 Màn login mà đề bài mô tả là trang của J&T, không phải của AutoJMS

Đề bài yêu cầu redesign phần nhìn của một màn login WebView2 có: *nền, panel giữa, chọn ngôn
ngữ, username, password, nhớ tài khoản, quên mật khẩu, nút login, thông tin hỗ trợ*.

Trong repo:

- **`frmLogin`** (`Forms/frmLogin.cs`) là WinForms + SunnyUI thuần. Nó không có username /
  password / ngôn ngữ / nhớ tài khoản. Nó có `txt_hwid` (mã máy) + `txt_key` (mã kích hoạt) +
  `btn_activate`. Đây là **dialog kích hoạt license**.
- Màn login có đủ các trường trên là **trang login của cổng JMS J&T**, tải từ
  `https://jms.jtexpress.vn` (`Config/AppConfig.cs:24`) vào `tabDKCH_webView` / `tabHome_webView`.

Restyle trang đó nghĩa là **tiêm CSS vào website của bên thứ ba**. Không nên làm:

1. AutoJMS không sở hữu markup đó — J&T deploy lại là CSS vỡ.
2. `WebViewAutomation.cs` lái chính trang đó bằng selector DOM. Đổi cách trình bày là đánh
   cược vào automation của toàn bộ luồng CHUYỂN HOÀN.
3. Đây là hệ thống nội bộ của J&T Express. Sửa giao diện nó từ một app bên thứ ba là quyết
   định của Owner, không phải mặc định kỹ thuật.

**`frmLogin` thì hoàn toàn restyle được** và đằng nào cũng nằm trong danh sách dialog.

---

## 10. Thứ tự migration đề xuất

Sắp theo **rủi ro tăng dần**, không theo thứ tự trong đề bài.

### Bước 0 — Sửa gốc trước *(bắt buộc, chặn mọi thứ sau)*

| # | Việc | File | Protected? |
|---|---|---|---|
| 0.1 | Tầng token (`ThemeColors`/`Typography`/`Spacing`/`Radius`/`Metrics`) | file mới | Không |
| 0.2 | **Bỏ dòng `ctrl.Font = new Font(...)` vô điều kiện** ở `AppTheme.cs:209`; chỉ gán khi control chưa có font riêng, và dùng `Font` cache chung | `UI/AppTheme.cs` | Không |
| 0.3 | `AppTheme.LightColors/RedColors/DarkColors` đọc từ token thay vì hằng số literal | `UI/AppTheme.cs` | Không |
| 0.4 | Xoá `UI/AppPalette.cs` (0 call site) | `UI/AppPalette.cs` | Không |

> 0.2 là điều kiện cần để §6.1 không lặp lại lần thứ năm. Làm xong bước này thì
> `LayoutDkchDataSection()` / `LayoutDkchNewbill()` gọi-sau-Apply mới có thể gỡ dần.

### Bước 1 — Component mới, chưa nối vào đâu *(rủi ro ~0)*

`KpiCard`, `EmptyState`, `LoadingState`, `ErrorState`, `StatusLegend`, `ResultSummary`,
`SearchPanel`, `FilterBar`, `ToolbarGroup`, `Timeline`, `DpiHelper`, `FontManager`,
`ControlStyler`. Tất cả là file mới. Build phải pass. Chưa màn nào dùng.

### Bước 2 — Dialog *(rủi ro thấp — độc lập, dễ rollback)*

`TermsDialog` → `UpdateChannelDialog` → `frmLogin` → 75 chỗ `MessageBox.Show`/`UIMessageTip`
thay bằng `AMessageDialog`/`AConfirmDialog`. Không file nào bị bảo vệ.

### Bước 3 — ABOUT *(màn nhỏ nhất, 3 control)*

Màn thật đầu tiên. Cần quyền sửa `Main.Designer.cs`. Nếu hỏng, ảnh hưởng bằng 0.

### Bước 4 — DPI *(cần quyết định riêng của Owner)*

Đổi `Main.Designer.cs:1789` từ `AutoScaleMode.None` sang `Dpi`. **Việc này sẽ làm xê dịch
mọi toạ độ pixel cứng trong `Main.Designer.cs`.** Không gộp chung với bất kỳ bước nào khác —
phải là một commit riêng, một lượt smoke test riêng ở 100%/125%/150%.

### Bước 5 — TRA HÀNH TRÌNH *(nơi kiểm chứng `ADataGridView`)*

Màn duy nhất có grid + input + counter + progress. Đây mới là chỗ chứng minh yêu cầu
100k dòng, không phải CHUYỂN HOÀN.

### Bước 6 — CHUYỂN HOÀN *(màn pilot theo đề bài)*

Để sau Bước 0 và Bước 5 vì §6.1: migration này đã từng bị rollback một lần.

### Bước 7 — IN ĐƠN, HOME, FULLSTACK_OPERATION

`FullStackOperation` để cuối cùng: 12 file, ~4.300 dòng riêng file chính, bảng màu riêng
chưa có Dark, và là tính năng chỉ tier ULTRA có.

---

## 11. Tóm tắt — 6 rủi ro cao nhất

| # | Rủi ro | Mức | Chặn cái gì |
|---|---|---|---|
| 1 | `AppTheme.cs:209` ghi đè font mọi control sau khi layout xong | 🔴 Cao | **Toàn bộ `ThemeTypography`.** Đã làm hỏng 4 lần thử trước (§6.1) |
| 2 | `Main.Designer.cs:1789` `AutoScaleMode.None` + `PerMonitorV2` → app không scale DPI | 🔴 Cao | Toàn bộ yêu cầu 100/125/150% |
| 3 | `Main.cs` / `Main.Designer.cs` là Protected File, chứa app shell **và** CHUYỂN HOÀN | 🔴 Cao | Phase 6 và Phase 7 |
| 4 | Style phân nhánh theo `ctrl.Name` (8 chỗ) | 🟠 Vừa | Đổi tên control = mất style, không lỗi biên dịch |
| 5 | `FullStackOperation` có hệ màu riêng, không Dark | 🟠 Vừa | Tính nhất quán toàn app |
| 6 | `Font` rò GDI handle mỗi lượt apply; `PrintService.cs:156` `AutoResizeColumns(AllCells)` | 🟠 Vừa | Yêu cầu "máy cấu hình thấp" và "100k dòng" |

---

*Hết Phase 1. Không có file nguồn nào bị sửa trong phase này.*
