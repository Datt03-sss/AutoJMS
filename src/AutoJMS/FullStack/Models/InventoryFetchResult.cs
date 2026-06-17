using System.Collections.Generic;

namespace AutoJMS.FullStack.Models
{
    public sealed class InventoryFetchResult
    {
        public List<InventoryFetchItem> Items { get; set; } = new();
        public int TotalPages { get; set; }
        public int TotalRecords { get; set; }
    }

    public sealed class InventoryFetchItem
    {
        public string WaybillNo { get; set; }
        public int PageNo { get; set; }
    }
}
