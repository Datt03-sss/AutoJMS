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

| Token | Size | Dùng cho |
|---|---:|---|
| `Xs` | **12px** | Tag, badge trạng thái, inline metadata, ô chi tiết siêu dày đặc |
| `Sm` | **14px** | Toolbar nhỏ, nút phụ (Secondary/Ghost), dropdown arrow, ô tìm kiếm nhỏ |
| `Md` | **16px** | **Mặc định toàn app.** Nút chuẩn (`AButton`), ô nhập liệu (`ATextBox`), hành động trong bảng |
| `Lg` | **20px** | Thanh điều hướng chính (`TopNavigation`), tiêu đề Dialog, tiêu đề Card lớn |
| `Xl` | **24px** | Màn hình trống (`EmptyState`), cảnh báo lỗi (`ErrorState`), Hero KPI Card |

> **Cấm đặt kích thước tùy tiện** (như 13px, 17px, 19px, 22px...). Chỉ được sử dụng đúng 5 nấc kích thước trên.

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
| **Đồng bộ dữ liệu** | `refresh-cw` | `check` (thành công) / `alert-triangle` (lỗi) | Tự hoàn trả sau 2.0 giây |
| **Tìm kiếm / Xóa** | `search` | `x` | Giữ `x` khi có text; bấm `x` xóa text và morph về `search` |
| **Hiện / Ẩn mật khẩu**| `eye` | `eye-off` | Chuyển đổi qua lại theo toggle |
| **Đóng / Mở Panel** | `chevron-down` | `chevron-up` (hoặc `menu` ➔ `x`) | Chuyển đổi qua lại theo trạng thái mở/đóng |
| **Bật / Tắt Giám sát**| `play` | `pause` | Chuyển đổi theo trạng thái chạy/dừng |

---

## 5. Hướng Dẫn Kỹ Thuật Theo Môi Trường

### A. Trong Lớp WinForms Native (.NET 8)
- Sử dụng font nhúng `lucide.ttf` qua `ASymbols`:
  ```csharp
  // Vẽ icon trong nút bấm AButton
  btnSync.Symbol = ASymbols.Refresh; // Mã Lucide Unicode
  btnSync.SymbolSize = ThemeMetrics.IconMd; // 16px
  
  // Vẽ trực tiếp trong custom OnPaint
  ASymbols.Draw(g, ASymbols.Search, S(16), Theme.TextSecondary, glyphRect);
  ```
- Khi thêm icon mới vào WinForms:
  1. Tra cứu icon trên [lucide.dev/icons](https://lucide.dev/icons/).
  2. Bổ sung hằng số Unicode vào `src/AutoJMS/UI/DesignSystem/Helpers/ASymbols.cs`.
  3. Sử dụng tên hằng số theo PascalCase khớp với tên Lucide (ví dụ `Package`, `Printer`, `Download`, `RefreshCw`).

### B. Trong Lớp WebView2 Dashboard (HTML / React)
- Sử dụng SVG chuẩn của Lucide từ `lucide-icons.js`:
  ```html
  <!-- Khai báo icon tĩnh -->
  <span class="aj-icon" data-icon="printer" data-size="16"></span>
  ```
- Sử dụng Morphicons cho hiệu ứng chuyển động:
  ```javascript
  // Khởi tạo morphing icon
  const iconEl = document.getElementById("copy-btn-icon");
  const morph = new Morphicon(iconEl, {
    from: LucideIcons.copy,
    to: LucideIcons.check,
    spring: { stiffness: 200, damping: 20 }
  });
  
  // Kích hoạt morph khi click
  function onCopyClicked() {
    morph.to();
    setTimeout(() => morph.from(), 1500);
  }
  ```

---

## 6. Quy Tắc Accessibility & Usability

1. **Icon đứng một mình BẮT BUỘC phải có Tooltip**:
   - Nếu nút bấm chỉ có icon mà không có chữ kèm theo, bắt buộc phải có `ToolTip` (WinForms) hoặc `title` / `aria-label` (WebView2).
2. **Không phân biệt trạng thái CHỈ bằng màu icon**:
   - Icon trạng thái phải thay đổi cả hình dáng (glyph) lẫn màu sắc (ví dụ: Thành công = icon `check` màu `Success`, Lỗi = icon `alert-circle` màu `Danger`).
