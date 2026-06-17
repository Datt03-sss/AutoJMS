using AutoJMS.Data;
using AutoJMS.FullStack.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Repositories
{
    public interface IFullStackWaybillRepository
    {
        Task<List<WaybillDbModel>> GetDashboardRowsAsync(CancellationToken ct = default);
        Task<FullStackSyncResult> ApplyInventoryRunAsync(InventoryRun run, IReadOnlyList<InventoryFetchItem> items, CancellationToken ct = default);
        Task UpsertTrackingRowsAsync(IReadOnlyList<WaybillDbModel> rows, CancellationToken ct = default);
        Task UpsertTrackingEventsAsync(IReadOnlyList<TrackingEvent> events, CancellationToken ct = default);
        Task MarkEnrichedAsync(IEnumerable<string> waybillNos, CancellationToken ct = default);
        Task IncrementReminderCountAsync(IEnumerable<string> waybillNos, CancellationToken ct = default);
        Task<string> GetSyncStateAsync(string key, CancellationToken ct = default);
        Task SetSyncStateAsync(string key, string value, CancellationToken ct = default);
    }
}
