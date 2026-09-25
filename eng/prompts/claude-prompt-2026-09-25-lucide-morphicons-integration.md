# Claude Prompt Proposal: Tích hợp Lucide Icons & Morphicons Animated Icons cho AutoJMS

> **Ngày tạo**: 2026-09-25  
> **Chủ đề**: Tích hợp bộ icon chuẩn Lucide (https://lucide.dev/icons/) và hiệu ứng chuyển động icon Morphicons (https://www.morphicons.com/) đồng bộ cho toàn bộ ứng dụng AutoJMS (WinForms Native + WebView2 Dashboard), kèm bộ quy chuẩn rules.  
> **Trạng thái**: Đề xuất kiến trúc & hướng dẫn thực thi chuẩn từ Antigravity (Advisor).

---

## 1. Bối cảnh (Context)
AutoJMS là ứng dụng vận hành Desktop Hybrid gồm:
- **Tầng Desktop Native (WinForms .NET 8)**: Giao diện chính, điều hướng, các nút chức năng (`AButton`), ô tìm kiếm, bảng dữ liệu. Hiện đang dùng `ASymbols.cs` vẽ các glyph từ font `Segoe MDL2 Assets` của Windows. Nhược điểm: phụ thuộc vào font Windows (có thể thiếu glyph trên máy cũ), số lượng icon rất ít, không đồng bộ với giao diện hiện đại.
- **Tầng Web Dashboard (WebView2 / HTML / React)**: `src/AutoJMS/Web/index.html` (FullStackOperation, Chatbot...). Hiện đang copy-paste hàng chục đoạn mã SVG inline rải rác, không có hệ thống quản lý icon và chưa hỗ trợ icon chuyển động (animated transitions).
- **Yêu cầu của Owner**: Chuẩn hóa toàn bộ icon sang **Lucide Icons** và icon động sang **Morphicons**, đồng thời thiết lập bộ rules chuẩn để từ nay về sau toàn bộ dự án và các agent luôn tuân thủ đồng bộ.

---

## 2. Thiết kế Kiến trúc Đồng bộ (Architecture)

```
                    ┌────────────────────────────────────────────────────────┐
                    │      HỆ THỐNG RULES & DESIGN SYSTEM (BẮT BUỘC)         │
                    │  - .agent/rules/11-icon-and-animation-rules.md         │
                    │  - DesignReference/AutoJMS.DESIGN.md (§J & §Animation) │
                    └───────────────────────────┬────────────────────────────┘
                                                │
                 ┌──────────────────────────────┴──────────────────────────────┐
                 ▼                                                             ▼
┌─────────────────────────────────┐                           ┌─────────────────────────────────┐
│     Lớp WinForms (.NET 8)      │                           │    Lớp WebView2 (Dashboard)    │
├─────────────────────────────────┤                           ├─────────────────────────────────┤
│ • Embedded Asset: lucide.ttf    │                           │ • Offline Assets:               │
│ • PrivateFontCollection nạp RAM │                           │   - lucide-icons.js             │
│ • ASymbols.cs / LucideSymbols   │                           │   - morphicons.min.js           │
│ • Draw(g, symbol, size, color)  │                           │ • Helper renderLucideIcon()     │
│ • Tương thích 100% AButton,     │                           │ • Component <morph-icon>        │
│   SearchPanel, EmptyState       │                           │ • State Transition Interactions │
└─────────────────────────────────┘                           └─────────────────────────────────┘
```

---

## 3. Chi tiết Các Bước Thực thi (Required Changes)

### Bước 1: Tạo Rule Chuẩn `.agent/rules/11-icon-and-animation-rules.md`
Tạo file `.agent/rules/11-icon-and-animation-rules.md` với nội dung cốt lõi:
1. **Nguồn Icon duy nhất**: Chỉ sử dụng Lucide Icons (https://lucide.dev/icons/).
2. **Nguồn Animation Icon duy nhất**: Chỉ sử dụng Morphicons (https://www.morphicons.com/).
3. **Thang kích thước chuẩn (Size Scale)**:
   - `Xs` (12px): Badge, status tags, inline metadata.
   - `Sm` (14px): Dense toolbars, small secondary buttons.
   - `Md` (16px): Kích thước chuẩn cho nút bấm (`AButton`), icon trong ô tìm kiếm, bảng dữ liệu.
   - `Lg` (20px): TopNavigation tabs, dialog headers.
   - `Xl` (24px): Empty state, Hero KPI card icon.
4. **Quy chuẩn màu sắc (Color Tokens)**:
   - Không bao giờ hardcode mã hex `#...` cho icon.
   - Luôn gán theo semantic tokens từ `ThemeColors`: `Primary`, `TextSecondary`, `OnPrimary`, `Success`, `Danger`, `TextMuted`.
5. **Quy tắc Animation (Morphicons)**:
   - **Chỉ dùng cho phản hồi tương tác (Action Feedback / State Transitions)**, ví dụ:
     - `copy` ➔ `check` (tự đảo ngược sau 1.5s).
     - `refresh-cw` ➔ `check` (khi sync thành công) hoặc ➔ `alert-triangle` (lỗi).
     - `search` ➔ `x` (khi có text tìm kiếm để xóa nhanh).
     - `eye` ➔ `eye-off` (hiển thị / ẩn mật khẩu).
     - `chevron-down` ➔ `chevron-up` (thu gọn / mở rộng).
   - Thời lượng chuyển động: 250ms - 350ms, spring physics êm ái, interruptible.
   - **CẤM animation lặp vô tận (infinite loop)** trừ loading spinner khi đang fetch dữ liệu.

### Bước 2: Cập nhật `DesignReference/AutoJMS.DESIGN.md`
- Cập nhật mục **§J. Phong cách icon**:
  - Ghi nhận Lucide Icons là bộ icon chính thức (thay thế Segoe MDL2 Assets).
  - Ghi nhận Morphicons là chuẩn chuyển động icon (State transition morphing).
- Cập nhật mục **§A.4 & §I**: Bổ sung ngoại lệ có kiểm soát: Cho phép animation micro-feedback qua Morphicons trên WebView2.

### Bước 3: Tích hợp Lucide Font vào WinForms Native
1. **Thêm font file**:
   - Thêm file `lucide.ttf` (từ gói chuẩn `lucide-static`) vào `src/AutoJMS/Resources/Fonts/lucide.ttf`.
2. **Khai báo trong `src/AutoJMS/AutoJMS.csproj`**:
   ```xml
   <ItemGroup>
     <EmbeddedResource Include="Resources\Fonts\lucide.ttf" />
   </ItemGroup>
   ```
3. **Nâng cấp `src/AutoJMS/UI/DesignSystem/Helpers/ASymbols.cs`**:
   - Sử dụng `PrivateFontCollection` để nạp `lucide.ttf` từ `Assembly.GetExecutingAssembly().GetManifestResourceStream(...)` vào bộ nhớ RAM 1 lần duy nhất lúc khởi động.
   - Bổ sung bảng mã hằng số các icon Lucide phổ biến:
     `Home`, `Back` (arrow-left), `Forward` (arrow-right), `Refresh` (refresh-cw), `Search`, `Download`, `Upload`, `Export` (share/external-link), `Copy`, `Check`, `Settings`, `More` (more-horizontal), `Calendar`, `Send`, `View` (eye), `Hide` (eye-off), `Warning` (alert-triangle), `Inbox` (package), `Print` (printer), `Trash` (trash-2), `Plus`, `Minus`, `Filter`, v.v.
   - Giữ nguyên method signature:
     ```csharp
     public static void Draw(Graphics g, int symbol, int size, Color color, Rectangle bounds)
     ```
   - Đảm bảo 100% tương thích ngược với toàn bộ control `AButton`, `SearchPanel`, `EmptyState`, `ErrorState` hiện hữu.

### Bước 4: Tích hợp Lucide & Morphicons vào WebView2 (`src/AutoJMS/Web/`)
1. **Thêm thư viện offline**:
   - Thêm `morphicons.min.js` (từ gói npm `morphicons`) vào `src/AutoJMS/Web/morphicons.min.js`.
   - Thêm `lucide-icons.js` vào `src/AutoJMS/Web/lucide-icons.js` (chứa dictionary các SVG stroke path của Lucide và helper `renderLucideIcon(name, size, color)`).
2. **Cập nhật `src/AutoJMS/Web/index.html`**:
   - Khai báo thêm `<script src="./lucide-icons.js"></script>` và `<script src="./morphicons.min.js"></script>` trong `<head>` (đảm bảo CSP `script-src 'self' 'unsafe-eval'` cho phép).
   - Áp dụng Morphicons cho các nút tương tác quan trọng:
     - Nút Copy mã vận đơn (Copy ➔ Check).
     - Nút Đồng bộ dữ liệu (Refresh ➔ Check khi hoàn thành).
     - Nút Xóa/Tìm kiếm (Search ➔ X).

---

## 4. Ràng buộc Kỹ thuật (Technical Constraints)
1. **Tuân thủ Single-Writer Lock**: Trước khi chỉnh sửa code, Claude Code phải acquire lock trong `.agent-lock.md`.
2. **Minimal Edit Rule**: Không refactor lan man các class không liên quan. Không xóa các dây dispatch sự kiện WinForms hiện có.
3. **Hiệu năng trên máy yếu**: Font Lucide nạp qua `PrivateFontCollection` 1 lần duy nhất, tái sử dụng `Font` cache theo size (đã có sẵn cơ chế `Dictionary<int, Font>` trong `ASymbols.cs`).
4. **An toàn CSP trong WebView2**: Mọi script và font phải nằm 100% offline nội bộ trong thư mục `src/AutoJMS/Web/`, không tải từ CDN bên ngoài.

---

## 5. Các Bước Xác minh (Verification Steps)
1. **Biên dịch Release**:
   ```powershell
   dotnet restore .\AutoJMS.slnx
   dotnet build .\AutoJMS.slnx -c Release
   ```
2. **Chạy Harness Verification**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\eng\harness\verify.ps1
   ```
3. **Smoke Test Giao diện**:
   - Mở ứng dụng, kiểm tra các nút `AButton` tại thanh điều hướng, các tab HOME, DKCH, TRACKING, PRINT, ABOUT: icon Lucide sắc nét, căn giữa hoàn hảo.
   - Thử nghiệm trên WebView2 Dashboard: bấm nút Copy hoặc Đồng bộ để chiêm ngưỡng hiệu ứng chuyển động mượt mà của Morphicons.
