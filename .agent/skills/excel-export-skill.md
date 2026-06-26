# Excel Export Skill

## Overview

AutoJMS exports tracking/waybill data to `.xlsx` using the **ClosedXML** library
(`PackageReference Include="ClosedXML" Version="0.105.0"` in `src/AutoJMS/AutoJMS.csproj`).

Reference implementation: `src/AutoJMS/Tracking/WaybillTrackingService.cs`
- `ExportToExcel()` — single sheet from the current display table.
- `ExportSpecial()` — splits rows into two sheets (`PHÁT HÀNG` / `HOÀN PHÁT`).
- `ApplyHeaderStyle(IXLWorksheet, IXLTable)` — shared header/border styling helper.

## Namespace

```csharp
using ClosedXML.Excel;
```

## Basic Export Pattern

Build a `DataTable`, insert it as a ClosedXML table starting at cell A1, style, then `SaveAs`.

```csharp
using var wb = new XLWorkbook();
var ws = wb.Worksheets.Add("Trạng thái hiện tại");

// InsertTable(data, tableName, addHeaders: true) starting at A1
var table = ws.Cell(1, 1).InsertTable(dataTable, "TrackingTable", true);

ApplyHeaderStyle(ws, table);

wb.SaveAs(filePath);
```

## SaveFileDialog (standard AutoJMS pattern)

```csharp
using var sfd = new SaveFileDialog
{
    Filter = "Excel Files (*.xlsx)|*.xlsx",
    FileName = $"Trạng thái vận đơn__{DateTime.Now:HH-mm-ss  dd/MM/yyyy}.xlsx",
    InitialDirectory = _exportFolder
};
if (sfd.ShowDialog() != DialogResult.OK) return;
```

Always guard against empty data before opening the dialog:

```csharp
if (_allRows.Count == 0)
{
    MessageBox.Show("Chưa có dữ liệu!", "Thông báo");
    return;
}
```

## Header & Border Styling

Shared helper used by every export. Clears formats first so styles apply cleanly,
themes the header row, adds thin borders, and auto-fits columns.

```csharp
private void ApplyHeaderStyle(IXLWorksheet ws, IXLTable table)
{
    ws.Range(1, 1, table.RowCount() + 1, table.ColumnCount())
      .Clear(XLClearOptions.AllFormats);

    var headerRow = table.HeadersRow();
    headerRow.Style.Fill.BackgroundColor = XLColor.FromTheme(XLThemeColor.Accent1, 0.2);
    headerRow.Style.Font.Bold = true;
    headerRow.Style.Font.FontColor = XLColor.Black;
    headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    headerRow.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

    table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

    if (table.RowCount() > 1)
    {
        var dataRange = ws.Range(2, 1, table.RowCount() + 1, table.ColumnCount());
        dataRange.Style.Fill.BackgroundColor = XLColor.White;
    }

    ws.Columns().AdjustToContents();
}
```

## Multi-Sheet Export (ExportSpecial)

Filter source rows into multiple `DataTable`s and add one worksheet per slice.
Only add a sheet when it has rows.

```csharp
using var wb = new XLWorkbook();

var phatHangRows = _allRows.Where(r => r.DauChuyenHoan != "Có").ToList();
if (phatHangRows.Any())
{
    var ws = wb.Worksheets.Add("PHÁT HÀNG");
    var dt = CreatePhatHangDataTable(phatHangRows);
    var table = ws.Cell(1, 1).InsertTable(dt, "PhatHangTable", true);
    ApplyHeaderStyle(ws, table);
}

var hoanPhatRows = _allRows.Where(r => r.DauChuyenHoan == "Có").ToList();
if (hoanPhatRows.Any())
{
    var ws = wb.Worksheets.Add("HOÀN PHÁT");
    var dt = CreateHoanPhatDataTable(hoanPhatRows);
    var table = ws.Cell(1, 1).InsertTable(dt, "HoanPhatTable", true);
    ApplyHeaderStyle(ws, table);
}

wb.SaveAs(sfd.FileName);
```

## Building a DataTable for Export

Column order in the `DataTable` becomes the column order in Excel.

```csharp
var dt = new DataTable("PHÁT HÀNG");
dt.Columns.Add("Mã vận đơn");
dt.Columns.Add("Trạng thái hiện tại");
dt.Columns.Add("COD thực tế");
// ...
foreach (var r in rows)
    dt.Rows.Add(r.WaybillNo, r.TrangThaiHienTai, r.CODThucTe /* ... */);
```

> When exporting the live grid, copy first so the on-screen table is not mutated:
> `var exportTable = _displayTable.Copy();`

## Open File After Save

```csharp
if (MessageBox.Show($"Xuất {sfd.FileName} thành công, Mở file ngay?",
        "Xuất file thành công", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
    == DialogResult.Yes)
{
    System.Diagnostics.Process.Start(
        new System.Diagnostics.ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
}
```

## Error Handling

Wrap the build/save in try/catch and surface a message box — never let an export
crash the form.

```csharp
try
{
    // build + SaveAs
}
catch (Exception ex)
{
    MessageBox.Show("Lỗi export: " + ex.Message, "Lỗi",
        MessageBoxButtons.OK, MessageBoxIcon.Error);
}
```

## Key ClosedXML API Reference

| Call | Purpose |
|------|---------|
| `new XLWorkbook()` | Create workbook (wrap in `using`) |
| `wb.Worksheets.Add(name)` | Add a sheet (name ≤ 31 chars, no `\ / ? * [ ]`) |
| `ws.Cell(r, c).InsertTable(dataTable, name, true)` | Insert DataTable as styled table |
| `table.HeadersRow()` | Access the header row for styling |
| `table.RowCount()` / `table.ColumnCount()` | Table dimensions |
| `ws.Range(r1, c1, r2, c2)` | Get a cell range |
| `XLColor.FromTheme(XLThemeColor.Accent1, 0.2)` | Theme tint for header fill |
| `ws.Columns().AdjustToContents()` | Auto-fit column widths |
| `wb.SaveAs(path)` | Write the `.xlsx` to disk |

## Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| Styles not applying | Inserted table carries default formats | `Range(...).Clear(XLClearOptions.AllFormats)` before styling |
| Sheet name error | Name > 31 chars or has invalid chars | Shorten / sanitize sheet name |
| Live grid mutated on export | Exported the bound table directly | Export a `.Copy()` of the table |
| File locked / save fails | File already open in Excel | Catch exception, prompt user to close it |
| Columns too narrow | No auto-fit | `ws.Columns().AdjustToContents()` |

## Notes

- Keep edits inside the `TRACKING` tab boundary (per `CLAUDE.md` Tab Boundary Rule).
- Match existing Vietnamese UI strings and the `Trạng thái vận đơn__<timestamp>.xlsx`
  naming convention for consistency.
