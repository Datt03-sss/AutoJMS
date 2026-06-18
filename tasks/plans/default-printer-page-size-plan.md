# Default Printer Page Size Plan

## Summary
Cập nhật logic cấu hình khổ giấy in đơn của AutoJMS: Set (3x3 - 7.62cm x 7.62cm) và Unset (về mặc định 4x6 inch) chỉ áp dụng cho máy in đang được set là Default Printer của Windows. Bất kể đó là loại máy in nào, cứ in qua Default Printer là sẽ áp dụng khổ giấy đã chọn.

## Current behavior
- `SetAutoJmsPaperSize3x3` gán cứng `PrintPaperWidthInch = 3m` (3 inch ~ 7.62cm) và áp dụng cho mọi máy in.
- `RestoreOriginalPaperSize` (Unset) gán cứng `4m x 6m` (4 inch x 6 inch) và áp dụng cho mọi máy in.
- Không phân biệt máy in mặc định hay máy in phụ.

## Required behavior
- **Set 3x3**: Lưu cấu hình kích thước 3x3 inch (7.62cm x 7.62cm). Khi in, nếu máy in đích đang là **máy in mặc định của Windows**, thì áp dụng khổ 3x3.
- **Unset**: Lưu cấu hình kích thước 4x6 inch. Khi in, nếu máy in đích đang là **máy in mặc định của Windows**, thì áp dụng khổ 4x6.
- **Máy in không phải Default**: Bỏ qua override, không áp dụng khổ giấy 3x3 hay 4x6 của AutoJMS.

## Files inspected
- `src/AutoJMS/Forms/Main.cs`

## Proposed files to edit
- `src/AutoJMS/Forms/Main.cs`

## Default printer handling
- Nhận diện máy in mặc định tức thời bằng `new System.Drawing.Printing.PrinterSettings().PrinterName`.

## Set / Unset page size behavior
- Code hiện tại của `SetAutoJmsPaperSize3x3()` và `RestoreOriginalPaperSize()` đã lưu đúng kích thước (3m x 3m và 4m x 6m). Không cần sửa nhiều ở 2 hàm này ngoài việc làm rõ log.
- Trọng tâm là sửa ở `ApplyPrintPaperSettings(PrintDocument printDocument)`:
  - Lấy tên máy in mặc định: `string defaultPrinter = new PrinterSettings().PrinterName;`
  - So sánh `printDocument.PrinterSettings.PrinterName` với `defaultPrinter`.
  - Chỉ khi 2 tên khớp nhau, mới thực thi lệnh `printDocument.DefaultPageSettings.PaperSize = new PaperSize(...)`.
  - Nếu không khớp, `return;` ngay lập tức để không can thiệp khổ giấy.

## Validation/error handling
- Wrap lệnh gán `PaperSize` vào `try-catch` để phòng hờ trường hợp driver máy in chặn không cho override size, giúp app không bị crash.

## Risks
- Không có rủi ro lớn. Cách làm này an toàn vì chỉ áp dụng khi tên máy in khớp với Default Printer hiện tại, hoàn toàn không sửa Global Preferences của Windows.
