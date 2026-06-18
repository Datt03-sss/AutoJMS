# Global Default Printer Page Size Plan (Win32 DEVMODE)

## Summary
Đổi lại phương pháp: Thay vì dùng `System.Printing` (WPF) vốn chỉ thay đổi `PrintTicket` và thường không ăn vào Control Panel của các máy in nhiệt truyền thống (GDI), chúng ta sẽ sử dụng API Win32 nguyên thủy (`DocumentProperties` và `SetPrinter`) để can thiệp trực tiếp vào cấu trúc `DEVMODE` của máy in.

## Current behavior
- Lần thử nghiệm với `System.Printing` đã ghi log thành công nhưng không thực sự làm thay đổi giao diện Control Panel (Printing Preferences) đối với một số dòng máy in, dẫn đến hệ thống vẫn dùng khổ mặc định cũ.

## Required behavior
- **Set 3x3:** Can thiệp `DEVMODE` của Windows, đổi cấu hình máy in mặc định thành khổ tùy chỉnh (User-defined / Custom) với kích thước `76.2 x 76.2 mm` (762 x 762 trong đơn vị 0.1mm của Windows).
- **Unset:** Can thiệp `DEVMODE` của Windows, đổi về khổ `4x6 inch` (101.6 x 152.4 mm, tức 1016 x 1524).

## Proposed changes

### 1. `src/AutoJMS/Utils/PrinterDevModeHelper.cs` (New File)
- Tạo class helper chứa các hàm P/Invoke gọi API Windows:
  - `OpenPrinter`
  - `DocumentProperties`
  - `SetPrinter` (Level 9)
- Hàm `SetGlobalPaperSize(string printerName, int widthTenthMm, int heightTenthMm)` sẽ cập nhật `DEVMODE`.

### 2. `src/AutoJMS/Forms/Main.cs`
- Thay thế hàm `SetGlobalDefaultPrinterPaperSize` cũ bằng cách gọi `PrinterDevModeHelper`.
- **Trong `SetAutoJmsPaperSize3x3`:** Gọi `PrinterDevModeHelper.SetGlobalPaperSize(defaultPrinter, 762, 762);`
- **Trong `RestoreOriginalPaperSize`:** Gọi `PrinterDevModeHelper.SetGlobalPaperSize(defaultPrinter, 1016, 1524);`

## Validation/error handling
- Win32 API trả về false sẽ được catch và log rõ ràng.
- Đảm bảo cấp đủ cờ `PRINTER_ALL_ACCESS` khi mở máy in.

## Owner review checklist
- Bấm Set 3x3.
- Mở Control Panel > Devices and Printers > Default Printer > Printing Preferences xem khổ giấy có hiển thị thay đổi không.
- Bấm Unset và kiểm tra lại.
