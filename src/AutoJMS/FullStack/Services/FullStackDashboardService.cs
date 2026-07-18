using AutoJMS.FullStack.LocalDb;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Repositories;
using AutoJMS.FullStack.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
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

        public Task<FullStackSyncResult> SyncInventoryAndRefreshTrackingAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
            => SyncInventoryAndRefreshTrackingAsync(from, to, null, ct);

        // onBatchPersisted (optional): fired after each fetched page of codes has been enriched and
        // written, so the caller can refresh the UI in realtime.
        public async Task<FullStackSyncResult> SyncInventoryAndRefreshTrackingAsync(
            DateTime? from,
            DateTime? to,
            Func<Task> onBatchPersisted,
            CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);

            if (!await _syncGate.WaitAsync(0, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Đang đồng bộ tồn kho, vui lòng đợi hoàn tất.");

            try
            {
                // Producer/consumer: the inventory paginator writes each page's new codes into the
                // channel; the consumer enriches (bulk tracking + per-waybill order detail) those codes
                // immediately while the next page is still downloading, then fires onBatchPersisted so
                // the grid updates in realtime. DB writes are serialized by the repository write-gate.
                var channel = Channel.CreateUnbounded<IReadOnlyList<string>>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

                int enrichedCount = 0;
                var consumer = Task.Run(async () =>
                {
                    await foreach (var pageCodes in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        try
                        {
                            await _trackingEnrichmentService.EnrichAsync(pageCodes, ct).ConfigureAwait(false);
                            enrichedCount += pageCodes.Count;
                            if (onBatchPersisted != null)
                            {
                                try { await onBatchPersisted().ConfigureAwait(false); }
                                catch (Exception cbEx) { AppLogger.Warning($"[FullStackDashboard] onBatchPersisted error: {cbEx.Message}"); }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception enrichEx)
                        {
                            AppLogger.Warning($"[FullStackDashboard] page enrichment failed: {enrichEx.Message}");
                        }
                    }
                }, ct);

                FullStackSyncResult syncResult;
                try
                {
                    syncResult = await _inventorySyncService.SyncInventoryAsync(
                        from, to,
                        onPageCodes: (codes, token) => channel.Writer.WriteAsync(codes, token).AsTask(),
                        ct: ct).ConfigureAwait(false);
                }
                finally
                {
                    channel.Writer.Complete();
                }

                await consumer.ConfigureAwait(false);
                AppLogger.Info($"[FullStackDashboard] risk engine updated rows={enrichedCount}");
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

        public async Task<string> GetWaybillLastActionTimeAsync(string waybillNo, CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            return await _repository.GetWaybillLastActionTimeAsync(waybillNo, ct).ConfigureAwait(false);
        }

        // Phase 2 T0: cheap page-1 head probe (1 request) to detect working-set changes.
        public async Task<FullStackInventorySyncService.InventoryHead> FetchInventoryHeadAsync(CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            return await _inventorySyncService.FetchInventoryHeadAsync(ct).ConfigureAwait(false);
        }

        // Phase 2 T1: enrich only a bounded "hot set" (route history) instead of the whole inventory.
        // Hot set = new-from-head arrivals + backlog/CSKH problems + orders DUE by next_track_at.
        // There is deliberately NO blanket "all active" fallback: active orders are refreshed as they
        // fall due (next_track_at), which spreads JMS load over time; the T2 full sync covers the rest.
        // This keeps each T1 tick cheap and avoids re-enriching hundreds of unchanged orders every cycle.
        public async Task<int> SyncHotSetAsync(IReadOnlyCollection<string> newFromHead, int cap = 300, CancellationToken ct = default)
        {
            await InitializeAsync(ct).ConfigureAwait(false);
            var rows = await _repository.GetDashboardRowsAsync(ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hot = new List<string>();
            void Add(string wb)
            {
                if (string.IsNullOrWhiteSpace(wb)) return;
                var c = wb.Trim().ToUpperInvariant();
                if (seen.Add(c)) hot.Add(c);
            }
            bool Real(string s) => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "empty", StringComparison.OrdinalIgnoreCase);

            if (newFromHead != null) foreach (var c in newFromHead) Add(c);                                     // 1) new arrivals
            int nNew = hot.Count;
            foreach (var r in rows) if (r.IsActive && Real(r.NguyenNhanKienVanDe)) Add(r.WaybillNo);            // 2) backlog / CSKH / problem
            int nProblem = hot.Count - nNew;
            foreach (var r in rows.Where(r => r.IsActive && r.NextTrackAt.ToUniversalTime() <= now).OrderBy(r => r.NextTrackAt)) Add(r.WaybillNo); // 3) due
            int nDue = hot.Count - nNew - nProblem;

            if (hot.Count > cap) hot = hot.GetRange(0, cap);
            if (hot.Count == 0) return 0;

            await _trackingEnrichmentService.EnrichWithResultAsync(hot, ct).ConfigureAwait(false);
            AppLogger.Info($"[FullStackHotSet] enriched={hot.Count} (new={nNew} problem={nProblem} due={nDue}) cap={cap}");
            return hot.Count;
        }
    }
}
