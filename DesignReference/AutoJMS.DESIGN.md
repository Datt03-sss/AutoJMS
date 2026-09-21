# AutoJMS Design System

> **Nguồn sự thật về mọi quyết định thị giác của AutoJMS.**
> Mọi control, component và màn hình phải tuân theo tài liệu này.
> Cảm hứng nguyên tắc lấy từ [`Pinterest.DESIGN.md`](./Pinterest.DESIGN.md) — **không** lấy hình thức.
>
> - Phiên bản: `1.0` · 2026-09-22
> - Hiện trạng đối chiếu: [`AutoJMS.UI.AUDIT.md`](./AutoJMS.UI.AUDIT.md)
> - Nền tảng: WinForms, `net8.0-windows`, máy cấu hình thấp, 100–150% DPI

---

## A. Triết lý thị giác

AutoJMS là **công cụ vận hành**, không phải sản phẩm nội dung. Nhân viên bưu cục mở nó
suốt ca làm, quét bảng dữ liệu, bấm nút, đọc trạng thái. Giao diện thành công khi người
dùng **không chú ý tới nó**.

Năm nguyên tắc, xếp theo thứ tự ưu tiên khi xung đột:

1. **Dữ liệu là nhân vật chính.** Chrome (nav, panel, nút) lùi lại; bảng và số liệu nổi lên.
2. **Quét được trong 1–3 giây.** Mắt phải tìm ra con số/trạng thái cần thiết mà không phải đọc.
3. **Dày đặc nhưng không chật.** Mật độ thông tin cao đạt bằng nhịp khoảng cách đều đặn,
   không phải bằng cách bóp nhỏ mọi thứ.
4. **Yên tĩnh.** Không animation, không gradient, không đổ bóng, không hiệu ứng. Mỗi pixel
   có màu đều phải giải thích được vì sao nó có màu.
5. **Nhanh trên máy yếu.** Thị giác không bao giờ được đánh đổi bằng hiệu năng.

### Ba thứ mượn từ Pinterest

| Nguyên tắc Pinterest | Áp vào AutoJMS |
|---|---|
| *"Get out of the photograph's way"* — chrome lùi để ảnh nổi | Chrome lùi để **bảng dữ liệu** nổi. Grid là "bức ảnh" của AutoJMS. |
| Một màu accent bão hoà duy nhất, **không bao giờ dùng để trang trí** | `Primary` chỉ dành cho hành động chính, tab đang chọn, và focus của phần tử chính. Không tô tiêu đề, không tô viền panel. |
| *"Two tools sharing the same chrome"* — magazine thoáng, search engine chật | **Chrome thở (12–24px), bề mặt dữ liệu chật (4–8px).** Đây là nguyên tắc nhịp điệu quan trọng nhất của AutoJMS. |

### Ba thứ cố tình làm ngược Pinterest

| Pinterest | AutoJMS | Vì sao |
|---|---|---|
| Radius 16px/32px, **cấm góc vuông** | Radius 0/4/6/8, **góc vuông cho mọi bề mặt cấu trúc** | Góc bo 16px ở mật độ desktop ăn mất pixel đọc được và trông như web app. Đề bài: *"No excessive rounded cards"*. |
| Hero 70px, nhịp section 64px | Chữ lớn nhất 16pt, nhịp section 24px | Màn 1366×768 của laptop bưu cục. 64px nhịp dọc là mất một hàng dữ liệu. |
| Font riêng (Pin Sans), thay bằng Inter | **Segoe UI, không bàn** | Có sẵn trong Windows — không phải nhúng, không phải cài, không có FOUT. Được hint cho cỡ nhỏ ở DPI thấp. Nhúng Inter là thêm file vào bộ cài để đổi lấy một khác biệt không ai thấy ở 9pt. |

---

## B. Hệ màu

### Nguyên tắc nền

1. **Một accent.** Toàn app có đúng **một** màu bão hoà. Đổi `Primary` là đổi cả app.
2. **Không control nào được hard-code màu.** Mọi màu đọc từ `ThemeColors`.
3. **Xám mang cấu trúc, màu mang ý nghĩa.** Nếu một pixel có màu, nó đang nói một điều gì đó
   về trạng thái nghiệp vụ.
4. **Phân cấp bằng độ đậm chữ và khoảng cách, không bằng sắc độ màu.** (Mượn thẳng Pinterest.)

### Ba preset

Giữ nguyên ba theme đang chạy — người dùng đã có `_settings.Theme` lưu sẵn, đổi tên là mất
cấu hình của họ.

| Preset | `Primary` | Tính cách |
|---|---|---|
| **Light** (mặc định) | `#3B82F6` xanh | Trung tính, không thiên thương hiệu |
| **Red** | `#E53935` đỏ | Theo nhận diện J&T |
| **Dark** | `#E53935` đỏ | Ca đêm, màn hình sáng thấp |

### Token ngữ nghĩa

Đây là **toàn bộ** từ vựng màu. Không có màu nào ngoài bảng này.

| Token | Light | Red | Dark | Dùng cho |
|---|---|---|---|---|
| `Primary` | `#3B82F6` | `#E53935` | `#E53935` | Nút chính, tab đang chọn, link |
| `PrimaryHover` | `#60A5FA` | `#EF5350` | `#EF5350` | Hover của nút chính |
| `PrimaryPressed` | `#2563EB` | `#C62828` | `#B71C1C` | Đang nhấn |
| `PrimaryDisabled` | `#BFDBFE` | `#F5B7B5` | `#5A2422` | Nút chính bị khoá |
| `PrimaryTint` | `#EFF6FF` | `#FFEBEE` | `#2A1414` | Nền nhạt: dòng chọn, hover trong list |
| `OnPrimary` | `#FFFFFF` | `#FFFFFF` | `#FFFFFF` | Chữ/icon nằm trên `Primary` |
| `Surface` | `#F5F7FA` | `#F5F7FA` | `#101012` | Nền trang |
| `SurfaceAlt` | `#F9FAFB` | `#F9FAFB` | `#131316` | Sọc grid, nền header grid, toolbar |
| `SurfaceRaised` | `#FFFFFF` | `#FFFFFF` | `#18181B` | Card, panel, ô nhập, dialog |
| `Border` | `#E5E7EB` | `#E5E7EB` | `#27272A` | Viền card, đường kẻ, divider |
| `BorderStrong` | `#D1D5DB` | `#D1D5DB` | `#3F3F46` | Viền ô nhập, viền control tương tác |
| `Text` | `#1F2937` | `#1F2937` | `#E4E4E7` | Chữ chính |
| `TextSecondary` | `#6B7280` | `#6B7280` | `#A1A1AA` | Nhãn, mô tả, chữ phụ |
| `TextMuted` | `#9CA3AF` | `#9CA3AF` | `#71717A` | Placeholder, chữ bị khoá, metadata |
| `Success` | `#16A34A` | `#16A34A` | `#22C55E` | Thành công, đã xong |
| `Warning` | `#D97706` | `#D97706` | `#F59E0B` | Cảnh báo, đang chờ |
| `Danger` | `#DC2626` | `#DC2626` | `#EF4444` | Lỗi, huỷ, dừng |
| `Info` | `#2563EB` | `#2563EB` | `#3B82F6` | Thông tin trung tính |
| `Focus` | `#111827` | `#111827` | `#FAFAFA` | Vòng focus bàn phím — xem §S |
| `Scrim` | `#66000000` | `#66000000` | `#99000000` | Lớp phủ sau modal (ARGB) |

> **`Warning` đổi từ `#F59E0B` sang `#D97706` ở Light/Red.**
> `#F59E0B` trên nền trắng cho tương phản 2,2:1 — dưới ngưỡng WCAG AA (4,5:1) cho chữ.
> `#D97706` đạt 4,6:1. Giá trị cũ giữ lại cho Dark vì nền tối làm nó đủ tương phản.

### Màu mang nghĩa nghiệp vụ *(không được đổi)*

Ba nút workflow của CHUYỂN HOÀN dùng màu như **mã trạng thái**, không phải trang trí:

| Control | Token | Nghĩa |
|---|---|---|
| `tabDKCH_btnDKCH1` | `Success` | Chạy luồng 1 — an toàn |
| `tabDKCH_btnDKCH2` | `Warning` | Chạy luồng 2 — cần chú ý |
| `tabDKCH_btnStop` | `Danger` | Dừng khẩn |

Chúng **không** dùng `Primary`. Người dùng nhận ra nút bằng màu trước khi đọc chữ.

---

## C. Vai trò màu ngữ nghĩa

Bốn màu trạng thái luôn xuất hiện theo **cặp nền/chữ**, không bao giờ chỉ đổi màu chữ:

| Trạng thái | Nền badge | Chữ badge | Chữ trên nền thường |
|---|---|---|---|
| Success | `Success` @ 12% | `Success` | `Success` |
| Warning | `Warning` @ 12% | `Warning` | `Warning` |
| Danger | `Danger` @ 12% | `Danger` | `Danger` |
| Info | `Info` @ 12% | `Info` | `Info` |
| Neutral | `SurfaceAlt` | `TextSecondary` | `TextSecondary` |

**Không bao giờ truyền đạt trạng thái CHỈ bằng màu.** Mỗi badge trạng thái phải có chữ.
~8% nam giới Việt Nam mù màu đỏ–lục; đỏ và lục là hai màu trạng thái quan trọng nhất của AutoJMS.

---

## D. Thang chữ

**Đơn vị là point (pt)** — WinForms `Font` mặc định theo point, không phải pixel.
Cột px là quy đổi ở 96 DPI để tham khảo.

| Token | pt | ≈px | Weight | Dùng cho |
|---|---:|---:|---|---|
| `Display` | 16 | 21 | Semibold | Số KPI lớn, tiêu đề trang |
| `H1` | 13 | 17 | Semibold | Tiêu đề section |
| `H2` | 11 | 15 | Semibold | Tiêu đề card, tiêu đề panel |
| `Body` | 9.75 | 13 | Regular | Chữ mặc định toàn app |
| `BodyStrong` | 9.75 | 13 | Semibold | Nhãn form, nhấn mạnh trong dòng |
| `Button` | 9.75 | 13 | Semibold | Chữ trên nút |
| `Small` | 8.25 | 11 | Regular | Chữ trợ giúp, metadata |
| `Caption` | 8 | 10.7 | Regular | Nhỏ nhất — chỉ dùng cho chú thích |
| `Grid` | 9 | 12 | Regular | Ô dữ liệu trong bảng |
| `GridHeader` | 9 | 12 | Semibold | Đầu cột bảng |
| `Mono` | 9 | 12 | Regular | Mã vận đơn, mã bưu cục — xem dưới |

**Chỉ 3 trọng lượng:** Regular (400) · Semibold (600) · Bold (700, hiếm — chỉ số KPI âm/khẩn).

### Họ chữ

| Vai trò | Họ | Fallback |
|---|---|---|
| Toàn bộ UI | **Segoe UI** | `Microsoft Sans Serif` |
| Nhấn mạnh | **Segoe UI Semibold** | `Segoe UI` Bold |
| Mã vận đơn / số | **Consolas** | `Courier New` |

> **`Microsoft Sans Serif` bị loại.** Nó xuất hiện 47 lần trong repo vì Designer sinh ra mặc
> định, không phải vì ai chọn. `Tahoma` (1 lần) cũng vậy.

> **Vì sao có `Mono`:** mã vận đơn là chuỗi số dài người dùng phải đối chiếu bằng mắt.
> Font tỉ lệ làm `1`/`7` và `0`/`8` xô lệch nhau giữa hai dòng. Font đều giữ cột thẳng hàng.
> Chỉ dùng cho **mã**, không dùng cho chữ thường.

### Nguyên tắc

- **Phân cấp bằng weight và size, không bằng màu.** Chữ body giữ `Text` ở mọi ngữ cảnh.
- **Chiều cao dòng 1.35** cho chữ nhiều dòng; 1.0 cho nhãn một dòng và chữ trên nút.
- **Không letter-spacing âm.** Thủ thuật của Pinterest hợp cho 70px, phá vỡ khả năng đọc ở 9pt.
- **Không chữ IN HOA toàn bộ** ngoài đầu cột bảng. Tiếng Việt có dấu — viết hoa hết làm
  dấu chồng lên nhau và mất khả năng đọc lướt.

---

## E. Font family — xem §D

---

## F. Thang khoảng cách

**Đơn vị cơ sở: 4px.** Mọi khoảng cách là bội số của 4.

| Token | px | Dùng cho |
|---|---:|---|
| `Xs` | 4 | Nhãn ↔ control của nó; padding dọc trong ô bảng |
| `Sm` | 8 | Giữa control cùng nhóm; padding ngang trong ô bảng |
| `Md` | 12 | Padding trong card; giữa các nhóm control |
| `Lg` | 16 | Padding panel; lề nội dung trang |
| `Xl` | 24 | Giữa các section lớn |
| `Xxl` | 32 | Padding dialog |

### Nhịp hai tốc độ *(nguyên tắc Pinterest được áp dụng đúng chỗ)*

| Vùng | Nhịp | Lý do |
|---|---|---|
| **Chrome** — nav, header, toolbar, card, dialog | `Md`–`Xl` (12–24) | Thở. Đây là nơi mắt nghỉ. |
| **Bề mặt dữ liệu** — ô bảng, hàng danh sách, chip | `Xs`–`Sm` (4–8) | Chật. Mỗi pixel là một hàng dữ liệu nữa lên màn. |

Đây là bản dịch trực tiếp của *"magazine ↔ search engine"*: một chrome, hai mật độ.

### Chiều cao chuẩn

| Phần tử | px @96DPI |
|---|---:|
| Thanh nav chính | 40 |
| Toolbar | 32 |
| Nút, ô nhập, combo | 28 |
| Hàng bảng | 26 |
| Đầu cột bảng | 30 |
| Badge trạng thái | 18 |

Mọi giá trị trên đi qua `DpiHelper.Scale()` — xem §X.

---

## G. Bo góc

| Token | px | Dùng cho |
|---|---:|---|
| `None` | 0 | **Mặc định.** Bề mặt cấu trúc: trang, nav bar, bảng, splitter, toolbar |
| `Sm` | 4 | Ô nhập, nút, combo, checkbox |
| `Md` | 6 | Card, panel, popover |
| `Lg` | 8 | Dialog |
| `Pill` | ½ chiều cao | **Chỉ** badge trạng thái |

**Cố tình ngược Pinterest.** Pinterest cấm `0px` và nhảy từ 16 lên 32. AutoJMS lấy `0` làm
mặc định và không bao giờ vượt quá `8`. Mật độ desktop không chịu được góc bo lớn: radius
16px ăn mất ~10px chiều ngang mỗi ô ở mật độ AutoJMS, và bo góc bề mặt cấu trúc tạo ra
những khe hở nhìn thấy được giữa các panel kề nhau.

**Không có giá trị nào ngoài 5 giá trị trên.**

---

## H. Viền

| Token | Dày | Màu | Dùng cho |
|---|---:|---|---|
| `Hairline` | 1 | `Border` | Viền card, divider, đường kẻ bảng |
| `Control` | 1 | `BorderStrong` | Viền ô nhập, combo, nút phụ |
| `Focus` | 2 | `Focus` | Vòng focus bàn phím |
| `Accent` | 1 | `Primary` | Card đang chọn, tab đang chọn |

**Viền là công cụ phân tầng chính của AutoJMS** (thay cho đổ bóng). Không có độ dày nào
khác 1 và 2. Viền 3px+ trông như lỗi hiển thị ở 100% DPI.

---

## I. Độ nổi

AutoJMS **không dùng đổ bóng**. Phân tầng bằng nền + viền.

| Mức | Cách thể hiện | Dùng cho |
|---|---|---|
| **0 — Nền** | `Surface`, không viền | Nền trang |
| **1 — Phẳng** | `SurfaceRaised` + `Hairline` | Card, panel, ô nhập — **chiếm đa số** |
| **2 — Nổi** | `SurfaceRaised` + `Control` | Card đang hover/chọn, popover |
| **3 — Modal** | `SurfaceRaised` + `Hairline` + `Scrim` phủ nền | Dialog |

> **Vì sao không đổ bóng, kể cả bóng nhẹ.**
> Đổ bóng thật trong WinForms cần `WS_EX_LAYERED` hoặc vẽ alpha thủ công mỗi `WM_PAINT`.
> Cả hai đều buộc composite trên CPU cho mọi lần vẽ lại. Trên máy cấu hình thấp — đúng đối
> tượng người dùng AutoJMS — đây là nguồn nháy hình và tụt FPS khi cuộn.
> Pinterest cũng đi tới kết luận tương tự ở mức nội dung: *"effectively no shadow elevation
> in its content surfaces"* — bóng duy nhất nằm ở tầng modal. AutoJMS đi xa hơn một bước:
> tầng modal dùng **scrim**, không dùng bóng.

---

## J. Phong cách icon

- **Nguồn:** `FontAwesome` có sẵn trong SunnyUI (`UISymbolButton.Symbol`). Không thêm bộ icon mới,
  không thêm file ảnh.
- **Kiểu:** nét viền (outline), không tô đặc — trừ khi icon đang biểu thị trạng thái active.
- **Cỡ:** 16px mặc định, 14px trong toolbar dày đặc, 20px cho nav chính.
- **Màu:** `TextSecondary` ở trạng thái thường, `Primary` khi active, `OnPrimary` khi nằm trên nền accent.
- **Icon một mình phải có tooltip.** Không có ngoại lệ — đây là yêu cầu accessibility, không phải góp ý.
- **Không icon nhiều màu.** Không emoji trong chrome.

---

## K. Phân cấp nút

| Cấp | Nền | Chữ | Viền | Dùng khi |
|---|---|---|---|---|
| **Primary** | `Primary` | `OnPrimary` | không | Hành động chính của màn. **Tối đa 1 trên mỗi vùng.** |
| **Secondary** | `SurfaceRaised` | `Text` | `Control` | Hành động thường |
| **Ghost** | trong suốt | `TextSecondary` | không | Hành động phụ, nút icon trong toolbar |
| **Danger** | `Danger` | `OnPrimary` | không | Huỷ, xoá, dừng khẩn |
| **Status** | `Success`/`Warning` | `OnPrimary` | không | **Chỉ** nút mang nghĩa nghiệp vụ (xem §B) |

### Trạng thái — bắt buộc đủ cho mọi cấp

| Trạng thái | Cách thể hiện |
|---|---|
| Default | như bảng trên |
| Hover | nền → biến thể `Hover`; Ghost → nền `SurfaceAlt` |
| Pressed | nền → biến thể `Pressed`; **không dịch chuyển control** |
| Focused | vòng `Focus` 2px bên ngoài, **cộng thêm** trạng thái hiện tại |
| Disabled | nền `PrimaryDisabled`/`SurfaceAlt`, chữ `TextMuted`, không hover |

**Pressed không được dịch control xuống 1px.** Đó là animation trá hình và gây vẽ lại vùng lớn.

---

## L. Phân cấp ô nhập

| Trạng thái | Nền | Viền | Chữ |
|---|---|---|---|
| Default | `SurfaceRaised` | `Control` | `Text` |
| Hover | `SurfaceRaised` | `Primary` | `Text` |
| Focused | `SurfaceRaised` | `Focus` 2px | `Text` |
| Disabled | `SurfaceAlt` | `Hairline` | `TextMuted` |
| Error | `SurfaceRaised` | `Danger` 1px | `Text` |
| Readonly | `SurfaceAlt` | `Hairline` | `Text` |

- **Placeholder dùng `TextMuted`, không bao giờ là giá trị thật.**
  `frmLogin` hiện đang so sánh giá trị ô với chuỗi watermark để validate
  (`frmLogin.cs:69` — `key == watermarkText`). Đó là một lỗi cần sửa riêng, không phải mẫu để nhân bản.
- **Lỗi hiển thị dưới ô**, `Small` + `Danger`. Không dùng tooltip cho lỗi validation —
  tooltip biến mất khi di chuột đi.
- **Nhãn nằm trên ô**, `BodyStrong`, cách `Xs` (4px).

---

## M. Điều hướng

Thanh nav chính, cao 40px, dính trên cùng.

```
┌──────────────────────────────────────────────────────────────────────┐
│ [icon] AutoJMS │ HOME  CHUYỂN HOÀN  TRA HÀNH TRÌNH  IN ĐƠN  ABOUT │ ● Online  [user] │ ─ □ ✕ │
└──────────────────────────────────────────────────────────────────────┘
```

| Vùng | Nội dung |
|---|---|
| Trái | Icon + chữ "AutoJMS" — nhận diện, không phải nút |
| Giữa | Tab điều hướng chính |
| Phải | Trạng thái kết nối · tài khoản · nút cửa sổ |

- **Active:** chữ `Text` + `BodyStrong` + gạch dưới 2px `Primary`.
- **Inactive:** chữ `TextSecondary`, không viền.
- **Hover (chưa chọn):** chữ `Text`, nền `SurfaceAlt`.
- **Không tô nền tab đang chọn bằng `Primary`.** Một vệt màu đặc 40px cao cạnh vùng dữ liệu
  kéo mắt khỏi dữ liệu. Gạch dưới đủ rõ và tốn ít mực hơn hẳn.
- **Cao đúng 40px.** Mỗi pixel thêm vào nav là một pixel mất khỏi bảng.
- **`ABOUT` luôn là tab cuối** — ràng buộc từ `CLAUDE.md` § Tab Boundary Rule.

---

## N. Tab

Tab con (ví dụ 4 mode của IN ĐƠN) dùng **cùng ngôn ngữ** với nav chính, chỉ nhỏ hơn:

- Cao 30px (nav chính 40px)
- Chữ `Body`, active thành `BodyStrong`
- Gạch dưới 2px `Primary` khi active
- Nền dải tab: `SurfaceAlt`; nền trang tab: `Surface`

> **Viền vàng `PremiumTabAccent` được giữ nguyên.** Nó là yêu cầu rõ ràng của Owner
> (commit `173c94c`, `10dab5f`) và mang thông tin thật: không nhầm mode đang mở.
> Đây là ngoại lệ **duy nhất** với quy tắc một-accent, và được ghi nhận có chủ đích.

---

## O. Card

```
┌─ SurfaceRaised, Border 1px, Radius Md(6) ──┐
│  Tiêu đề            H2 / Text              │  ← padding Md(12)
│  ─────────────────── Border 1px ────────── │
│  Nội dung           Body / Text            │
└────────────────────────────────────────────┘
```

- Padding `Md` (12px) mọi phía. Card dày đặc dữ liệu: `Sm` (8px).
- **Không đổ bóng.** Không gradient nền.
- Tiêu đề card **không tô `Primary`** — đó là tô màu trang trí (xem §Z).
- Card lồng nhau tối đa **1 cấp**. Sâu hơn thì cấu trúc đã sai, không phải cần thêm card.

---

## P. KPI card

Phần tử "quét trong 1–3 giây" quan trọng nhất của AutoJMS.

```
┌────────────────────────┐
│ TỔNG ĐƠN        Small/TextSecondary, IN HOA
│ 1.248           Display/Text          ← số trước, luôn to nhất
│ ▲ 12%           Small/Success         ← delta, có dấu ▲▼ chứ không chỉ màu
└────────────────────────┘
```

- **Số là thứ to nhất.** Nhãn nhỏ, phía trên, `TextSecondary`.
- **Số dùng `Mono`** để các KPI xếp cạnh nhau thẳng cột.
- **Delta luôn kèm ký hiệu** `▲`/`▼`/`—`, không chỉ dựa vào màu (§C).
- Card KPI **không có viền accent**, không nền màu. Viền `Hairline` như mọi card khác.
- Phân tách nhóm KPI bằng `Lg` (16px), trong nhóm `Sm` (8px).

---

## Q. DataGrid

**Component quan trọng nhất và nhạy cảm hiệu năng nhất của toàn hệ thống.**

### Thị giác

| Phần | Quy tắc |
|---|---|
| Đầu cột | Nền `SurfaceAlt`, chữ `GridHeader`, cao 30px, viền dưới 1px `BorderStrong` |
| Ô dữ liệu | Nền `SurfaceRaised`, chữ `Grid`, cao hàng **26px cố định** |
| Sọc chẵn/lẻ | `SurfaceAlt` / `SurfaceRaised` — chênh lệch rất nhẹ, **không** dùng màu |
| Đường kẻ | 1px `Border`, **chỉ kẻ ngang**. Không kẻ dọc trừ khi > 8 cột. |
| Hàng đang chọn | Nền `PrimaryTint`, chữ `Text`. **Không** nền `Primary` đặc. |
| Ô đang focus | Viền `Focus` 2px bên trong ô |
| Cột số | Căn phải, font `Mono` |
| Cột mã vận đơn | Căn trái, font `Mono` |
| Cột trạng thái | Badge (§S), **không** tô nền cả ô |

### Hiệu năng — ràng buộc cứng

| Quy tắc | Vì sao |
|---|---|
| **Chiều cao hàng cố định, `AutoSizeRowsMode = None`** | Cho phép grid tính vùng cuộn bằng phép nhân thay vì đo từng hàng |
| **`AutoSizeColumnsMode = None` (bề rộng cố định) hoặc `Fill` + `FillWeight`** | `ADataGridView` chốt `None` trong constructor. **Không** `DisplayedCells` — nó đo lại mọi ô đang hiện sau mỗi lần cuộn. **Không bao giờ** `AllCells`/`AllCellsExceptHeader` — chúng đo **mọi** dòng. `PrintService.cs:156` đang dùng `AllCells`, cần xử lý riêng. |
| **`VirtualMode = true` khi > 5.000 dòng** | Hiện đang là `false` tường minh (`WaybillTrackingService.cs:68`). Cần chuyển cho đường 100k dòng. |
| **`DoubleBuffered = true`** | Bắt buộc, nếu không sẽ nháy khi cuộn |
| **`EnableHeadersVisualStyles = false`** | Cần thiết để header nhận màu theme |
| **Không `CellPainting`/`RowPrePaint` tuỳ biến trên đường nóng** | Mỗi handler chạy cho mọi ô hiển thị, mọi lần vẽ. Badge trạng thái vẽ bằng `CellFormatting` + style, không vẽ tay. |
| **Không ảnh trong ô** | Mỗi `Image` là một GDI handle sống suốt vòng đời hàng |
| **Cuộn ngang được phép** | Nhồi 15 cột vào 1366px bằng cách bóp chữ còn tệ hơn cuộn |

### Mật độ

Grid theo nhịp **chật** (§F): padding ô `Sm`×`Xs` (8×4). Không có chế độ "comfortable" —
AutoJMS luôn ở mật độ cao.

---

## R. Search / filter

```
┌─ SearchPanel ──────────────────────────────────────────┐
│ [🔍 Nhập mã vận đơn...            ] [Tìm]  [Xoá]      │
│ ─────────────────────────────────────────────────────  │
│ [Từ ngày ▾] [Đến ngày ▾] [Trạng thái ▾]     12 kết quả│
└────────────────────────────────────────────────────────┘
```

- **Ô tìm kiếm là control rộng nhất trong hàng.** Nó là hành động chính.
- **Enter = Tìm.** Bắt buộc ở mọi search panel. `Esc` = xoá ô.
- **Nút "Tìm" là Primary**, nút "Xoá" là Ghost.
- **Filter đang bật phải nhìn thấy được** — chip có nền `PrimaryTint` + dấu `✕` để bỏ.
  Filter ẩn là nguồn lỗi "sao không thấy đơn của tôi" phổ biến nhất.
- **Số kết quả luôn hiển thị**, căn phải, `Small`/`TextSecondary`.
- Filter bar cao 32px, khoảng cách control `Sm` (8px).

---

## S. Chỉ báo trạng thái

### Badge

Pill (radius ½ chiều cao), cao 18px, padding ngang `Sm`, chữ `Caption` Semibold.
Nền = màu trạng thái @ 12%, chữ = màu trạng thái đặc.

**Luôn có chữ.** Không bao giờ chỉ là một chấm màu.

### Chấm trạng thái kết nối

`● Online` / `● Mạng chậm` / `● Mất kết nối` — chấm **và** chữ, dùng
`Success`/`Warning`/`Danger`. Thay cho chuỗi màu literal hiện tại ở `Main.cs:1150-1174`.

### Vòng focus — quy tắc hai lớp

Focus bàn phím dùng **hai lớp**:

```
  ┌─ Focus 2px (#111827 Light / #FAFAFA Dark)
  │ ┌─ SurfaceRaised 1px  ← khe trắng
  │ │ ┌─ control
```

> **Vì sao không dùng `Primary` làm vòng focus.**
> Ở preset Light, `Primary` là xanh `#3B82F6`. Vòng focus xanh quanh một **nút Primary xanh**
> là vô hình. Khe sáng 1px bên trong — đúng thủ thuật `focus-inner` của Pinterest — làm vòng
> focus hiện rõ trên **cả** nền accent đặc **và** nền trang sáng, ở cả ba preset.
>
> Vòng focus phải khác màu chọn (`PrimaryTint`). Nếu giống nhau, người dùng bàn phím không
> phân biệt được "đang ở đâu" với "đang chọn cái gì".

---

## T. Dialog

```
┌─ Radius Lg(8), Border 1px, trên Scrim ─────────┐
│  Tiêu đề                      H2         [✕]   │  padding Xxl(32)
│                                                 │
│  Nội dung                     Body              │
│                                                 │
│                        [Huỷ]  [Xác nhận]        │  ← primary bên PHẢI
└─────────────────────────────────────────────────┘
```

- Rộng: 360 (nhắn) · 480 (xác nhận) · 640 (nội dung dài). Không tự do.
- **Nút chính nằm ngoài cùng bên phải** — quy ước Windows.
- `Esc` = huỷ, `Enter` = hành động chính. Bắt buộc.
- Dialog phá hoại (xoá, dừng) dùng nút `Danger` và **không** để nó là nút mặc định.
- **Giữ nguyên câu chữ tiếng Việt hiện tại.** Redesign là phần nhìn, không phải phần chữ.
- Scrim `Scrim` phủ toàn form cha. Không đổ bóng (§I).

---

## U. Trạng thái rỗng

```
        [icon 32px / TextMuted]
        Chưa có dữ liệu            H2 / TextSecondary
        Nhập mã vận đơn để bắt đầu Small / TextMuted
        [Hành động gợi ý]          Secondary button (tuỳ chọn)
```

- Căn giữa trong vùng chứa.
- **Luôn nói người dùng cần làm gì tiếp theo.** "Không có dữ liệu" một mình là ngõ cụt.
- Phân biệt rõ **rỗng vì chưa tìm** với **rỗng vì tìm không ra** — hai câu chữ khác nhau.
- Không minh hoạ, không ảnh. Một icon là đủ.

---

## V. Trạng thái đang tải

- **Thanh tiến trình mảnh 2px** ở đỉnh vùng đang tải, màu `Primary`. Không spinner giữa màn.
- **Khoá control của vùng đó**, không khoá cả form.
- **> 2 giây thì phải có chữ**: "Đang tải 1.248 đơn…" — người dùng cần biết tiến độ, không
  chỉ biết "đang bận".
- **Không skeleton screen.** Nó cần animation liên tục và vẽ lại — vi phạm §A.5.
- Giữ nguyên dữ liệu cũ trên màn khi đang tải dữ liệu mới. Xoá bảng rồi mới tải làm màn nhấp nháy.

---

## W. Trạng thái lỗi

```
┌─ Border 1px Danger, nền Danger @8%, Radius Sm ─┐
│ [!] Không tải được danh sách đơn               │  BodyStrong / Danger
│     Mất kết nối tới máy chủ JMS.               │  Small / Text
│     [Thử lại]                                   │  Secondary button
└─────────────────────────────────────────────────┘
```

- **Lỗi hiện tại chỗ nó xảy ra**, không phải trong hộp thoại toàn cục — trừ khi nó chặn cả app.
- **Luôn có đường thoát**: Thử lại / Huỷ / Xem log.
- **Không hiện stack trace cho người dùng.** Ghi vào log, hiện câu tiếng Việt.
- Lỗi nghiệp vụ JMS (mã `code`/`msg` trong thân HTTP 200) hiện đúng thông điệp của JMS,
  không hiện "thành công" chỉ vì HTTP 200.

---

## X. DPI và độ phân giải

### Độ phân giải mục tiêu

| Cấu hình | Phải dùng được |
|---|---|
| 1366×768 @100% | ✅ Sàn — laptop bưu cục |
| 1920×1080 @100% | ✅ Phổ biến nhất |
| 1920×1080 @125% | ✅ |
| 1920×1080 @150% | ✅ |
| 2560×1440 @150% | ✅ |

### Quy tắc

1. **Mọi hằng số pixel đi qua `DpiHelper.Scale(int)`.** Không có ngoại lệ.
   Mẫu chuẩn — lấy từ `PremiumTabAccent.cs:92`, chỗ duy nhất trong repo đang làm đúng:
   ```csharp
   int scaled = DpiHelper.Scale(control, baseValue);   // baseValue * (DeviceDpi / 96.0)
   ```
2. **Layout dùng `Dock` / `Anchor` / `TableLayoutPanel`**, không dùng `Location` tuyệt đối.
3. **Font khai báo bằng point, không bao giờ bằng pixel.** WinForms tự scale point theo DPI.
4. **Không `Size` cứng cho control chứa chữ.** Để `AutoSize` hoặc đo bằng `TextRenderer.MeasureText`.
5. **`AutoScaleMode = Dpi`** cho mọi form mới.

> ⚠️ **`Main` hiện là `AutoScaleMode.None`** (`Main.Designer.cs:1789`) cộng với
> `PerMonitorV2` trong csproj → **app không scale gì cả** ở DPI cao. Sửa việc này làm xê
> dịch mọi toạ độ cứng trong `Main.Designer.cs`, nên nó **phải là một commit riêng, có lượt
> smoke test riêng** ở cả ba mức DPI. Xem `AutoJMS.UI.AUDIT.md` §5.1 và §10 Bước 4.

---

## Y. Khả năng tiếp cận

| Yêu cầu | Chuẩn |
|---|---|
| Tương phản chữ thường | ≥ 4,5:1 với nền |
| Tương phản chữ lớn (≥14pt Semibold) | ≥ 3:1 |
| Tương phản viền control | ≥ 3:1 với nền kề |
| Vòng focus | ≥ 3:1 với **cả** control **và** nền — bảo đảm bằng quy tắc hai lớp (§S) |
| Vùng bấm nhỏ nhất | 24×24px |

- **Mọi control tương tác phải tới được bằng `Tab`.** Thứ tự `TabIndex` theo thứ tự đọc.
- **Mọi nút chỉ-có-icon phải có tooltip.**
- **Không truyền đạt thông tin chỉ bằng màu** (§C).
- **Không bẫy focus** ngoài modal. Modal phải nhả focus khi `Esc`.
- `Enter` kích hoạt hành động chính, `Esc` huỷ — nhất quán ở mọi màn.

---

## Z. Do / Don't

### ✅ Do

- Đọc mọi màu, font, khoảng cách từ token. Không ngoại lệ.
- Dùng `Primary` cho **đúng một** hành động chính mỗi vùng.
- Xây phân cấp bằng **weight + khoảng cách**, không bằng màu.
- Để bề mặt cấu trúc **góc vuông**; chỉ bo góc phần tử tương tác.
- Giữ chiều cao hàng bảng **cố định**.
- Cho nhịp chrome thở (12–24px), nhịp dữ liệu chật (4–8px).
- Luôn kèm **chữ** với mọi tín hiệu màu.
- Cho mọi trạng thái lỗi một **đường thoát**.
- Đo trước khi tối ưu; tin hồ sơ đo, đừng tin trực giác.

### ❌ Don't

- **Đừng hard-code màu trong control.** Đây là quy tắc số một.
- **Đừng phân nhánh style theo `ctrl.Name`.** Đổi tên control là mất style, im lặng
  (8 chỗ đang mắc — `AutoJMS.UI.AUDIT.md` §3.4).
- **Đừng gán `Font` cho mọi control trong một vòng lặp đệ quy.** Nó xoá sạch thang chữ và
  rò GDI handle. Chính dòng này đã làm hỏng 4 lần thử migration trước (`AUDIT` §6.1).
- **Đừng thêm đổ bóng.** Kể cả bóng nhẹ. Kể cả chỉ một chỗ.
- **Đừng dùng gradient** — ngoại lệ duy nhất đã được duyệt là `PremiumTabAccent`.
- **Đừng thêm animation, transition, hay hiệu ứng hover có thời lượng.**
- **Đừng dùng `AutoSizeColumnsMode.AllCells`** trên grid có thể lớn.
- **Đừng tô nền `Primary` đặc cho hàng đang chọn** — dùng `PrimaryTint`.
- **Đừng tô tiêu đề bằng `Primary`.** Đó là trang trí, và nó làm loãng tín hiệu accent.
- **Đừng thêm giá trị radius thứ sáu**, hay bậc chữ thứ mười hai.
- **Đừng đổi câu chữ tiếng Việt** khi đang redesign phần nhìn.
- **Đừng redesign control mang nghĩa nghiệp vụ** mà chưa hỏi (`AUDIT` §6.4).

---

## Phụ lục — tra nhanh khi migrate

| Thấy cái này | Thay bằng |
|---|---|
| `Color.FromArgb(...)` / `ColorTranslator.FromHtml(...)` | `ThemeColors.Current.<Token>` |
| `new Font("Segoe UI", 10F)` | `ThemeTypography.Body` |
| `new Font("Microsoft Sans Serif", ...)` | `ThemeTypography.<bậc phù hợp>` |
| `Padding = new Padding(10)` | `ThemeSpacing.Md` qua `DpiHelper` |
| `Radius = 6` | `ThemeRadius.Md` |
| `Height = 32` | `ThemeMetrics.ControlHeight` qua `DpiHelper` |
| `if (ctrl.Name == "...")` | Thuộc tính trên control (`AButton.Variant`) |
| `MessageBox.Show(...)` | `AMessageDialog` / `AConfirmDialog` |

---

*Tài liệu này thắng mọi skill, mọi thói quen, và mọi tài liệu thiết kế khác trong repo.
Xung đột với `CLAUDE.md` hoặc `AGENTS.md` thì hai file đó thắng.*
