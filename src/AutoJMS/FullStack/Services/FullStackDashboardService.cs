using AutoJMS.FullStack.LocalDb;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Repositories;
using AutoJMS.FullStack.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackDashboardService
    {
        private readonly FullStackDbConnectionFactory _connectionFactory;
        private readonly FullStackDbInitializer _initializer;
        private readonly IFullStackWaybillRepository _repository;
        private readonly FullStackInventorySyncService _inventorySyncService;
        private readonly FullStackTrackingEnrichmentService _trackingEnrichmentService;
        private readonly SemaphoreSlim _syncGate = new(1, 1);

        public FullStackDashboardService()
        {
            _connectionFactory = new FullStackDbConnectionFactory();
            _initializer = new FullStackDbInitializer(_connectionFactory);
            _repository = new FullStackWaybillRepository(_connectionFactory);
            _inventorySyncService = new FullStackInventorySyncService(_repository);
            _trackingEnrichmentService = new FullStackTrackingEnrichmentService(_repository);
        }

        public string DatabasePath => _connectionFactory.DatabasePath;

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            await _initializer.InitializeAsync(ct).ConfigureAwait(false);
        }

        public async Task<FullStackDashboardSnapshot> LoadSnapshotAsync(CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            var rows = await _repository.GetDashboardRowsAsync(ct).ConfigureAwait(false);
            var lastSync = await _repository.GetSyncStateAsync("last_inventory_sync_at", ct).ConfigureAwait(false);
            DateTime? lastSyncAt = DateTime.TryParse(lastSync, out var dt) ? dt : null;
            AppLogger.Info($"[FullStackDashboard] grid refreshed rows={rows.Count}");
            return new FullStackDashboardSnapshot
            {
                Rows = rows,
                LastSyncAt = lastSyncAt,
                DbPath = DatabasePath
            };
        }

        public async Task<FullStackSyncResult> SyncInventoryAndRefreshTrackingAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);

            if (!await _syncGate.WaitAsync(0, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Đang đồng bộ tồn kho, vui lòng đợi hoàn tất.");

            try
            {
                var syncResult = await _inventorySyncService.SyncInventoryAsync(from, to, ct).ConfigureAwait(false);
                var rows = await _repository.GetDashboardRowsAsync(ct).ConfigureAwait(false);
                var waybills = rows.Select(x => x.WaybillNo).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (waybills.Count > 0)
                    await _trackingEnrichmentService.EnrichAsync(waybills, ct).ConfigureAwait(false);
                AppLogger.Info($"[FullStackDashboard] risk engine updated rows={waybills.Count}");
                return syncResult;
            }
            finally
            {
                _syncGate.Release();
            }
        }

        public async Task SaveReminderCountsAsync(IEnumerable<string> waybillNos, CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            await _repository.IncrementReminderCountAsync(waybillNos, ct).ConfigureAwait(false);
        }

        public async Task EnrichTrackingAsync(IEnumerable<string> waybillNos, CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            await _trackingEnrichmentService.EnrichAsync(waybillNos, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<FullStackJourneyResult>> EnrichTrackingWithResultAsync(IEnumerable<string> waybillNos, CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            return await _trackingEnrichmentService.EnrichWithResultAsync(waybillNos, ct).ConfigureAwait(false);
        }
    }
}
