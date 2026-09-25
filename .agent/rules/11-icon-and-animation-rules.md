# Icon and Animation Rules (Lucide & Morphicons)

> **Nguồn sự thật duy nhất về Icon và Icon Animation cho toàn bộ ứng dụng AutoJMS.**  
> Mọi Form, UserControl, Web Dashboard (WebView2), Dialog và Component bắt buộc phải tuân theo tài liệu này.  
> Áp dụng đồng bộ cho cả hai môi trường: **WinForms Native (.NET 8)** và **WebView2 (HTML/React)**.

---

## 1. Nguồn Thư viện Chuẩn (Single Sources of Truth)

1. **Bộ Icon duy nhất**: **[Lucide Icons](https://lucide.dev/icons/)**
   - Không dùng bộ icon nào khác (không FontAwesome, không Bootstrap Icons, không Material Icons, không tải ảnh PNG/JPG rời rạc bên ngoài).
   - Bộ font `Segoe MDL2 Assets` trước đây được thay thế hoàn toàn bằng font Lucide nhúng (`lucide.ttf`) để đảm bảo tính độc lập hệ điều hành và đồng bộ 100% với Web Dashboard.
   - Đặc tính thiết kế chuẩn: **Grid 24×24px**, **Stroke Width 2px**, `stroke-linecap="round"`, `stroke-linejoin="round"`, `fill="none"`.

2. **Bộ Animation Icon duy nhất**: **[Morphicons](https://www.morphicons.com/)**
   - Morphicons (`morphicons`) là thư viện mã nguồn mở chuyên biệt cho việc biến đổi (morphing) giữa các stroke-based SVG icons của Lucide bằng giải thuật 2D Procrustes Analysis và Spring Physics.
   - Cấm sử dụng các hiệu ứng CSS keyframe giật cục hoặc ảnh GIF/Lottie nặng nề gây tốn RAM/CPU trên máy bưu cục cấu hình thấp.

---

## 2. Thang Kích Thước Chuẩn (Icon Size Scale)

Mọi kích thước icon trong ứng dụng phải thuộc một trong các nấc kích thước sau (giá trị ở 96 DPI cơ sở, tự động co giãn qua `DpiHelper.Scale` trong WinForms):

| Hằng (`ThemeMetrics`) | Size | Dùng cho |
|---|---:|---|
| `IconSizeTag` | **12px** | Tag, badge trạng thái, inline metadata, ô chi tiết siêu dày đặc |
| `IconSizeDense` | **14px** | Toolbar nhỏ, nút phụ (Secondary/Ghost), dropdown arrow, ô tìm kiếm nhỏ |
| `IconSizeDefault` | **16px** | **Mặc định toàn app.** Nút chuẩn (`AButton`), ô nhập liệu (`ATextBox`), hành động trong bảng |
| `IconSizeNav` | **20px** | Thanh điều hướng chính (`TopNavigation`), tiêu đề Dialog, tiêu đề Card lớn |
| `IconSizeHero` | **24px** | Màn hình trống (`EmptyState`), cảnh báo lỗi (`ErrorState`), Hero KPI Card |

> **Cấm đặt kích thước tùy tiện** (như 13px, 17px, 19px, 22px...). Chỉ được sử dụng đúng 5 nấc kích thước trên.

Đây là tên hằng thật trong `src/AutoJMS/UI/DesignSystem/Theme/ThemeMetrics.cs` — viết thẳng
`ThemeMetrics.IconSizeDefault`, đừng viết `16`. Bên WebView2 không có hằng tương ứng vì CSP chặn
inline script: đặt số vào thuộc tính `size="16"` / `data-size="16"`, và số đó phải là một trong năm
giá trị trên.

---

## 3. Quy Chuẩn Màu Sắc (Color Tokens)

Icon phải luôn đọc màu từ semantic tokens của `ThemeColors`. **Tuyệt đối không hardcode mã hex `#...` cho icon.**

| Trạng thái / Ngữ cảnh | Token màu tương ứng |
|---|---|
| Icon thường trên bề mặt sáng/tối | `TextSecondary` (mặc định) |
| Icon của mục đang chọn (Active/Focus) | `Primary` |
| Icon nằm trên nền màu bão hòa (nút Primary) | `OnPrimary` (trắng) |
| Icon thành công / Đã hoàn thành | `Success` |
| Icon cảnh báo / Chờ xử lý | `Warning` |
| Icon lỗi / Dừng khẩn / Xóa | `Danger` |
| Icon phụ / Bị vô hiệu hóa (Disabled) | `TextMuted` |

---

## 4. Quy Chuẩn Animation & Tương Tác Chuyển Động (Morphicons)

### Triết lý: "Yên tĩnh & Phản hồi Tương tác (Micro-feedback Only)"
AutoJMS là công cụ vận hành bưu cục, không phải ứng dụng giải trí. Animation **chỉ phục vụ mục đích thông báo trạng thái thao tác cho người dùng**.

> **Phạm vi: mục §4 này chỉ áp cho WebView2 Dashboard.** WinForms vẽ icon bằng glyph của `lucide.ttf`
> — không có đường SVG để nội suy, và DESIGN.md §I đã cấm animation ở lớp native vì máy bưu cục.
> "Đồng bộ toàn ứng dụng" nghĩa là **cùng một bộ icon và cùng bảng cặp trạng thái dưới đây**: bên
> WinForms icon vẫn đổi hình theo trạng thái (gán hằng `ASymbols` khác rồi `Invalidate()`), chỉ là đổi
> tức thì thay vì morph. Đừng dựng `Timer` để giả lập chuyển động trong `OnPaint`.

1. **Chỉ dùng cho State Transitions (Chuyển đổi trạng thái)**:
   - Icon chỉ morph khi người dùng bấm nút hoặc khi một tiến trình ngầm đổi trạng thái.
2. **Cấu hình Spring Physics chuẩn**:
   - Damping: `20` (êm ái, không rung lắc thừa).
   - Stiffness: `200` (nhanh nhạy, phản hồi tức thì).
   - Thời lượng chuyển động: **250ms – 350ms**.
3. **Tuyệt đối cấm**:
   - Cấm animation lặp vô tận (infinite looping) làm xao nhãng mắt nhân viên (ngoại trừ icon quay tròn khi đang đồng bộ dữ liệu `sync`).
   - Cấm animation tự chạy khi mở trang nếu không có hành động của người dùng.

### Danh mục Cặp Icon Chuyển Trạng thái Chuẩn (State Transition Pairs)

| Hành động | Icon Bắt đầu (`from`) | Icon Kết thúc (`to`) | Cơ chế hoàn trả (Revert) |
|---|---|---|---|
| **Sao chép mã** | `copy` | `check` | Tự động hoàn trả về `copy` sau 1.5 giây |
| **Đồng bộ dữ liệu** | `refresh-cw` | `check` (thành công) / `triangle-alert` (lỗi) | Tự hoàn trả sau 2.0 giây |
| **Tìm kiếm / Xóa** | `search` | `x` | Giữ `x` khi có text; bấm `x` xóa text và morph về `search` |
| **Hiện / Ẩn mật khẩu**| `eye` | `eye-off` | Chuyển đổi qua lại theo toggle |
| **Đóng / Mở Panel** | `chevron-down` | `chevron-up` (hoặc `menu` ➔ `x`) | Chuyển đổi qua lại theo trạng thái mở/đóng |
| **Bật / Tắt Giám sát**| `play` | `pause` | Chuyển đổi theo trạng thái chạy/dừng |

Bảng này là danh sách đóng: 13 icon trong bảng `LUCIDE` của `src/AutoJMS/Web/aj-icons.js` đúng
bằng các tên ở trên. Thêm một cặp mới thì chép dữ liệu icon từ gói `lucide` vào bảng đó và thêm tên
vào mảng `EXPECTED` của `eng/harness/check-dashboard-icons.mjs` — **đừng vẽ tay đường `d`**.

> Lucide đã đổi tên một số icon: `alert-triangle` → `triangle-alert`, `alert-circle` → `circle-alert`,
> `more-horizontal` → `ellipsis`. Dùng tên mới; tên cũ vẫn tra được trên lucide.dev nên rất dễ chép lại.

---

## 5. Hướng Dẫn Kỹ Thuật Theo Môi Trường

### A. Trong Lớp WinForms Native (.NET 8)

`lucide.ttf` là embedded resource (`AutoJMS.csproj`), nạp một lần trong static constructor của
`ASymbols` qua `PrivateFontCollection`. Không có file font nào cạnh `.exe` để ai đó xoá mất.

```csharp
// Vẽ icon trong nút bấm AButton
btnSync.Symbol = ASymbols.Refresh;                   // codepoint Lucide
btnSync.SymbolSize = ThemeMetrics.IconSizeDefault;   // 16px

// Vẽ trực tiếp trong custom OnPaint — ASymbols.Draw tự căn giữa trong bounds
ASymbols.Draw(g, ASymbols.Search, DpiHelper.Scale(ThemeMetrics.IconSizeDefault),
              Theme.TextSecondary, glyphRect);
```

Thêm icon mới:

1. Tra icon trên [lucide.dev/icons](https://lucide.dev/icons/).
2. Lấy codepoint **từ cmap của `lucide.ttf`** (gói `lucide-static`), không tự đoán và không lấy từ
   bảng MDL2/FontAwesome. Số sai không báo lỗi — nó vẽ ra một glyph khác.
3. Thêm hằng vào `src/AutoJMS/UI/DesignSystem/Helpers/ASymbols.cs`.

Tên hằng đặt theo **vai trò trong AutoJMS**, tên Lucide ghi ở comment cuối dòng khi hai tên lệch
nhau — đó là quy ước đang dùng trong file (`Warning = 0xE193; // triangle-alert`,
`View = 0xE0BA; // eye`). Trùng tên Lucide thì để trần, không cần comment.

### B. Trong Lớp WebView2 Dashboard (HTML / React)

Điểm vào duy nhất là [`src/AutoJMS/Web/aj-icons.js`](../../src/AutoJMS/Web/aj-icons.js), nạp bằng
**`<script type="module">`**. Dashboard chạy ở origin https thật (`https://autojms.local`, map qua
`SetVirtualHostNameToFolderMapping`) nên ESM cùng origin hợp lệ với CSP `script-src 'self'`; không có
bundler và không cần UMD. Thư viện morphicons được chép nguyên vào `Web/morphicons/` — xem
[README](../../src/AutoJMS/Web/morphicons/README.md), **không sửa file `.js` trong đó**.

**Icon tĩnh** — inline SVG Lucide thẳng trong markup (24×24, `stroke-width="2"`, `fill="none"`), đúng
như ~180 icon đang có trong `index.html`. Đừng bọc thêm lớp trừu tượng cho thứ không đổi trạng thái.

**Icon morph** — custom element `<aj-morph-icon>`; nó đã mang sẵn spring 200/20 ở §4:

```html
<aj-morph-icon icon="{{ copyIconD }}" size="12"></aj-morph-icon>
```

`icon` nhận **một chuỗi `d`**, không nhận tên icon: React 16.8.6 chỉ truyền được thuộc tính dạng chuỗi
cho custom element. Lấy chuỗi đó trong khối `data-dc-script` bằng helper `AJ_ICON(name)`, rồi phát ra
qua `renderVals`:

```javascript
// renderVals — đổi state thì element tự morph, không gọi hàm nào
copyIconD: AJ_ICON(this.state.copied ? 'check' : 'copy'),
```

Ba chỗ cấm:

- **Cấm cặp `sc-if` hai SVG** để "chuyển" icon — đó là cắt cảnh, không phải morph, và là thứ
  `<aj-morph-icon>` thay thế.
- **Cấm `new Morphicon(...)`, `morph.to()`, `morph.from()`** — API đó không tồn tại. Chuyển động được
  điều khiển bằng state, không bằng lệnh gọi thủ tục.
- **Cấm `<morph-icon>` trần** — thuộc tính `spring=` chỉ nhận tên preset dựng sẵn (`smooth`, `snappy`,
  `bouncy`), và 200/20 không phải preset nào trong số đó.

Sửa xong chạy `node eng/harness/check-dashboard-icons.mjs` (đã nằm trong gate NodeTests của
`verify.ps1`). `dotnet build` **không** bắt được lỗi ở lớp này vì `Web/` không có bước build.

---

## 6. Quy Tắc Accessibility & Usability

1. **Icon đứng một mình BẮT BUỘC phải có Tooltip**:
   - Nếu nút bấm chỉ có icon mà không có chữ kèm theo, bắt buộc phải có `ToolTip` (WinForms) hoặc `title` / `aria-label` (WebView2).
2. **Không phân biệt trạng thái CHỈ bằng màu icon**:
   - Icon trạng thái phải thay đổi cả hình dáng (glyph) lẫn màu sắc (ví dụ: Thành công = icon `check` màu `Success`, Lỗi = icon `circle-alert` màu `Danger`).
3. **Đổi theme xong phải tô lại icon tự vẽ**:
   - `AppTheme.Apply` bỏ qua control không dùng token, nên icon vẽ trong `OnPaint` bằng màu lấy một
     lần lúc khởi tạo sẽ giữ màu của theme cũ. Đọc `Theme.*` ngay trong `OnPaint`, đừng cache `Color`.
