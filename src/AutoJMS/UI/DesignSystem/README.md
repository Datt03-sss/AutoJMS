# AutoJMS Design System

Bộ control và token dùng chung cho toàn bộ giao diện AutoJMS.
Namespace: `AutoJMS.UI.DesignSystem`. Tiền tố `A` = control của design system.

Quy tắc và lý do nằm ở **[DesignReference/AutoJMS.DESIGN.md](../../../../DesignReference/AutoJMS.DESIGN.md)**.
Hiện trạng giao diện cũ và thứ tự di trú nằm ở **[DesignReference/AutoJMS.UI.AUDIT.md](../../../../DesignReference/AutoJMS.UI.AUDIT.md)**.
File này chỉ nói *dùng thế nào*.

---

## Bắt đầu

```csharp
using AutoJMS.UI.DesignSystem;

var card = new ACard { Title = "Nguồn dữ liệu", Dock = DockStyle.Top };
var search = new SearchPanel { Dock = DockStyle.Top };
search.Search += (s, e) => Load(search.Query);

var run = new AButton { Text = "Chạy", Variant = AButtonVariant.Primary };
```

Không gán màu và font bằng tay. Mọi giá trị lấy từ token:

```csharp
// ĐÚNG
label.ForeColor = ThemeManager.Current.TextSecondary;
label.Font = ThemeTypography.Small;

// SAI - màu chết, không đổi theo theme, không ai tìm ra để sửa
label.ForeColor = Color.FromArgb(107, 114, 128);
label.Font = new Font("Microsoft Sans Serif", 8.25F);
```

---

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `Theme/` | Token: `ThemeColors`, `ThemeTypography`, `ThemeSpacing`, `ThemeRadius`, `ThemeBorders`, `ThemeShadows`, `ThemeMetrics`, và `ThemeManager` |
| `Controls/` | `AButton` `ATextBox` `AComboBox` `ACheckBox` `ARadioButton` `AToggleSwitch` `ACard` `ABadge` `AToolbar` `APanel` `ADataGridView` `AStatusIndicator` |
| `Components/` | `SearchPanel` `KpiCard` `EmptyState` `LoadingState` `ErrorState` |
| `Components/Dialogs/` | `ADialog` (khung) + `AMessageDialog` `AConfirmDialog` `AInputDialog` `AProgressDialog` |
| `Layout/` | `PageHeader` |
| `Helpers/` | `DpiHelper` `ControlStyler` `ThemeHook` |

---

## Theme

`ThemeManager` **không giữ trạng thái**. `ThemeManager.Mode` đọc/ghi thẳng
`AutoJMS.UI.AppTheme.CurrentTheme` — nơi màn hình Cài đặt vẫn đang đọc và ghi.
Giữ một bản sao thứ hai thì ngay lần đổi theme đầu tiên từ màn hình cũ là hai bên lệch nhau.

Đổi theme bằng đường nào cũng được:

```csharp
ThemeManager.Mode = ThemeMode.Dark;   // đường mới
UI.AppTheme.Apply(this);              // đường cũ - cuối hàm đã gọi ThemeManager.NotifyChanged()
```

`AppTheme.ApplyToControls` **bỏ qua cả cây con** của mọi control trong namespace
`AutoJMS.UI.DesignSystem`: nó đè `Font` và màu SunnyUI lên control, làm token thành vô nghĩa.
Control A* tự vẽ lại qua `ThemeHook`.

Control A* tự nối `ThemeHook` — không cần làm gì. Control **không** phải A* mà cần
đổi màu theo theme thì tự giữ một `ThemeHook` và **phải `Dispose()`**:
`ThemeChanged` là event tĩnh, quên gỡ là giữ sống cả cây control của form đã đóng.

---

## DPI

Mọi số đo trong token là pixel ở 96 DPI. Nhân trước khi dùng:

```csharp
int pad = DpiHelper.Scale(this, ThemeSpacing.Md);   // trong control A*: S(ThemeSpacing.Md)
```

Đặt control bằng `Dock` / `Anchor` / `TableLayoutPanel`, không bằng `Location` cứng.

---

## ADataGridView

Kế thừa thẳng `DataGridView` — ngoại lệ có chủ ý của quy tắc "A* kế thừa Control".
Lưới phải chịu 100k dòng; viết lại ảo hoá dòng, cuộn, hit-test, sắp xếp là hàng nghìn
dòng code và gần như chắc chắn chậm hơn.

Ba điều không được phá (DESIGN.md §Q):

- `AutoSizeRowsMode = None`. **Không bao giờ** `AllCells` — nó đo lại mọi ô ở mọi lần vẽ.
- Trên `ThemeMetrics.GridVirtualModeThreshold` (5000) dòng thì bật `VirtualMode`.
- Không xử lý `CellPainting` / `RowPrePaint` trên đường vẽ nóng, không ảnh trong ô.

Màu lấy từ token qua `ApplyTheme()`, gọi sẵn lúc tạo handle và khi đổi DPI.

---

## Hộp thoại

```csharp
if (AConfirmDialog.Confirm(this, "Xoá 12 đơn đã chọn?", "Xác nhận",
        confirmText: "Xoá", destructive: true))
{
    ...
}
```

`destructive: true` → nút Xoá màu Danger và **không** phải nút mặc định: Enter rơi vào "Huỷ".

`AProgressDialog` dùng `Show()`, không dùng `ShowDialog()` — `ShowDialog` chặn luồng gọi
nên không còn ai chạy việc để báo tiến độ. Nút Huỷ chỉ bật cờ `Cancelled`; vòng lặp tự
kiểm tra rồi tự đóng.

---

## Cố ý không có

| Thứ | Vì sao |
|---|---|
| Đổ bóng | WinForms phải tự composite alpha mỗi `WM_PAINT`; máy bưu cục không gánh nổi (DESIGN.md §I). `ThemeShadows` chỉ là thang `Elevation` đổi nền/viền. |
| Animation, gradient, blur | Cùng lý do. `AToggleSwitch` nhảy, không trượt. `LoadingState` không có `Timer`. |
| `FontManager` / `IconManager` | `ThemeTypography` đã là kho font cache sẵn; icon FontAwesome chỉ là số `int`. Thêm lớp bọc là thêm chỗ để sai. |
| `Theme/AppTheme.cs` | Trùng tên `AutoJMS.UI.AppTheme` → CS0104 ở mọi file dùng cả hai namespace. `ThemeColors` đã mang `Mode`. |

Chưa viết vì chưa có chỗ dùng thật: `ATabControl`, `ADatePicker`, `FilterBar`,
`ToolbarGroup`, `Timeline`, `StatusLegend`, `ResultSummary`, `AppShell`,
`TopNavigation`, `SplitLayout`, `ResponsiveLayout`.

---

## Màn hình đã di trú

| Màn hình | File | Trạng thái |
|---|---|---|
| CHUYỂN HOÀN (DKCH) — panel trái | `Forms/Main.DkchDesignSystem.cs` | Nút CONTROL dùng `AButton`; khung mục vẫn là `UITitlePanel` nhưng ăn token. Tắt bằng `DkchDesignSystemEnabled`. |

Control SunnyUI cũ **vẫn còn nguyên và vẫn giữ handler** — tắt cờ là về đúng bản cũ.
Nút A* nối thẳng vào chính handler cũ, không có lớp trung gian, không có logic nghiệp vụ nào
được chép lại.
