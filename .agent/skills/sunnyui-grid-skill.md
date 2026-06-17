# SunnyUI DataGridView Skill

## Overview

SunnyUI.UIDataGridView is used for displaying tabular data in AutoJMS.

## Basic Setup

```csharp
var grid = new Sunny.UI.UIDataGridView();

// Dock to fill parent
grid.Dock = DockStyle.Fill;

// Disable auto generation
grid.AutoGenerateColumns = false;

// Clear existing columns
grid.Columns.Clear();
```

## Column Configuration

```csharp
grid.Columns.AddRange(new DataGridViewColumn[]
{
    new DataGridViewTextBoxColumn
    {
        Name = "WaybillNo",
        HeaderText = "Mã vận đơn",
        DataPropertyName = "WaybillNo",
        Width = 140,
        ReadOnly = true,
        SortMode = DataGridViewColumnSortMode.Programmatic
    },
    new DataGridViewTextBoxColumn
    {
        Name = "Status",
        HeaderText = "Trạng thái",
        DataPropertyName = "TrangThaiHienTai",
        Width = 150
    }
});
```

## Performance: Double Buffering

For large datasets, enable double buffering:

```csharp
// Enable double buffering
var prop = grid.GetType().GetProperty(
    "DoubleBuffered",
    BindingFlags.Instance | BindingFlags.NonPublic);
prop?.SetValue(grid, true, null);

// Alternative method via extension
public static void EnableDoubleBuffering(this DataGridView grid)
{
    typeof(DataGridView)
        .GetProperty("DoubleBuffered", 
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?.SetValue(grid, true);
}
```

## Standard Settings

```csharp
public static void ApplyStandardGridSettings(DataGridView grid)
{
    if (grid == null) return;
    
    grid.ReadOnly = true;
    grid.AllowUserToAddRows = false;
    grid.AllowUserToDeleteRows = false;
    grid.MultiSelect = true;
    grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    grid.RowHeadersVisible = false;
    grid.AllowUserToResizeColumns = true;
    grid.AllowUserToResizeRows = false;
    
    // SunnyUI specific
    if (grid is Sunny.UI.UIDataGridView uiGrid)
    {
        uiGrid.StripeOddColor = Color.White;
        uiGrid.StripeEvenColor = Color.White;
    }
}
```

## Data Binding

```csharp
// Set data source
grid.DataSource = dataList;

// Handle binding complete
grid.DataBindingComplete += (s, e) =>
{
    // Configure columns after binding
    if (grid.Columns.Contains("Select"))
    {
        grid.Columns["Select"].HeaderText = "Chọn";
        grid.Columns["Select"].Width = 50;
    }
};

// Clear data
grid.DataSource = null;
```

## Cell Formatting

```csharp
grid.CellFormatting += (s, e) =>
{
    if (e.ColumnIndex < 0 || e.RowIndex < 0) return;
    
    var cell = grid[e.ColumnIndex, e.RowIndex];
    var status = cell.Value?.ToString();
    
    if (status?.Contains("Chuyển hoàn") == true)
    {
        cell.Style.ForeColor = Color.Red;
        cell.Style.Font = new Font(grid.Font, FontStyle.Bold);
    }
    else if (status?.Contains("Thành công") == true)
    {
        cell.Style.ForeColor = Color.Green;
    }
};
```

## Row Selection

```csharp
// Select all
foreach (DataGridViewRow row in grid.Rows)
{
    row.Selected = true;
}

// Clear selection
grid.ClearSelection();

// Get selected rows
var selectedRows = grid.SelectedRows.Cast<DataGridViewRow>().ToList();
```

## Column Header Click

```csharp
private string _sortColumn = "WaybillNo";
private SortOrder _sortOrder = SortOrder.Ascending;

grid.ColumnHeaderMouseClick += (s, e) =>
{
    var column = grid.Columns[e.ColumnIndex];
    
    if (_sortColumn == column.Name)
    {
        _sortOrder = _sortOrder == SortOrder.Ascending 
            ? SortOrder.Descending 
            : SortOrder.Ascending;
    }
    else
    {
        _sortColumn = column.Name;
        _sortOrder = SortOrder.Ascending;
    }
    
    // Re-sort data
    var data = grid.DataSource as List<WaybillDbModel>;
    if (data != null)
    {
        var sorted = _sortOrder == SortOrder.Ascending
            ? data.OrderBy(x => typeof(WaybillDbModel).GetProperty(_sortColumn)?.GetValue(x))
            : data.OrderByDescending(x => typeof(WaybillDbModel).GetProperty(_sortColumn)?.GetValue(x));
        
        grid.DataSource = sorted.ToList();
    }
};
```

## Cleanup

```csharp
// Before closing form or reassigning
if (grid.DataSource is IDisposable)
{
    // Clear first
    grid.DataSource = null;
}
```

## Common Issues

| Issue | Solution |
|-------|----------|
| Slow scrolling | Enable double buffering |
| Flickering | Set DoubleBuffered = true |
| Column resizing | Set SortMode = Programmatic |
| Binding errors | Handle DataBindingComplete |
