using AutoJMS.Data;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Events
{
    /// <summary>
    /// Thin façade the existing write paths call to emit events additively.
    /// All methods are no-ops unless EventPipelineEnabled is on, so wiring them
    /// into sync/workflow does not change behavior until the pipeline is enabled.
    ///
    /// Tracking/inventory/detail = observations → pushed to the shared remote
    /// event store (enqueueOutbox). Workflow (note/check/task) already syncs via
    /// its own row channels, so those events are logged locally for history/fold
    /// only (no duplicate push).
    /// </summary>
    public static class FullStackEventPipeline
    {
        private static readonly FullStackEventLog _log = new();

        public static bool IsEnabled
        {
            get
            {
                try { return SettingsManager.Load().EventPipelineEnabled; }
                catch { return false; }
            }
        }

        private static string ClientId
        {
            get { try { return SupabaseDbService.MachineId; } catch { return Environment.MachineName; } }
        }

        /// <summary>Emit a bounded set of observations for a freshly-tracked waybill row.</summary>
        public static async Task EmitTrackingAsync(WaybillDbModel row, CancellationToken ct = default)
        {
            if (!IsEnabled || row == null || string.IsNullOrWhiteSpace(row.WaybillNo)) return;
            try
            {
                var events = new List<FullStackEvent>();
                var now = DateTime.UtcNow;
                DateTime eventTime = ParseVnTime(row.ThoiGianThaoTac) ?? now;

                // TrackingObserved — latest movement state (one per waybill per sync;
                // fingerprint dedupes when the state hasn't changed).
                if (!IsEmpty(row.ThaoTacCuoi) || !IsEmpty(row.TrangThaiHienTai))
                {
                    var payload = new JObject
                    {
                        ["trang_thai_hien_tai"] = Clean(row.TrangThaiHienTai),
                        ["thao_tac_cuoi"] = Clean(row.ThaoTacCuoi),
                        ["thoi_gian_thao_tac"] = Clean(row.ThoiGianThaoTac),
                        ["buu_cuc_thao_tac"] = Clean(row.BuuCucThaoTac),
                        ["nguoi_thao_tac"] = Clean(row.NguoiThaoTac)
                    };
                    string semantic = $"{Clean(row.ThaoTacCuoi)}|{Clean(row.TrangThaiHienTai)}|{Clean(row.BuuCucThaoTac)}|{Clean(row.NguoiThaoTac)}";
                    events.Add(NewEvent(row.WaybillNo, FullStackEventTypes.TrackingObserved,
                        eventTime, FullStackEventSources.JmsTracking, semantic, payload));
                }

                // OrderDetailObserved — static per waybill; only when detail is present.
                if (!IsEmpty(row.TenNguoiGui) || !IsEmpty(row.DiaChiNhanHang) || !IsEmpty(row.MaDoanFull))
                {
                    var payload = new JObject
                    {
                        ["ten_nguoi_gui"] = Clean(row.TenNguoiGui),
                        ["dia_chi_nhan_hang"] = Clean(row.DiaChiNhanHang),
                        ["cod_thuc_te"] = Clean(row.CODThucTe),
                        ["noi_dung_hang_hoa"] = Clean(row.NoiDungHangHoa),
                        ["ma_doan_full"] = Clean(row.MaDoanFull)
                    };
                    string semantic = $"{Clean(row.TenNguoiGui)}|{Clean(row.DiaChiNhanHang)}|{Clean(row.CODThucTe)}|{Clean(row.MaDoanFull)}";
                    events.Add(NewEvent(row.WaybillNo, FullStackEventTypes.OrderDetailObserved,
                        now, FullStackEventSources.JmsOrderDetail, semantic, payload));
                }

                if (events.Count > 0)
                    await _log.AppendLocalAsync(events, enqueueOutbox: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[EventPipeline] EmitTracking skipped: " + ex.Message);
            }
        }

        /// <summary>Emit an inventory membership observation (leader only decides InventoryLeft).</summary>
        public static async Task EmitInventoryAsync(string waybillNo, bool inCurrentInventory, CancellationToken ct = default)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(waybillNo)) return;
            try
            {
                var now = DateTime.UtcNow;
                string type = inCurrentInventory ? FullStackEventTypes.InventorySeen : FullStackEventTypes.InventoryLeft;
                // Membership events are keyed to the day so one seen/left per waybill per day.
                string semantic = now.ToString("yyyy-MM-dd");
                var ev = NewEvent(waybillNo, type, now, FullStackEventSources.JmsInventory, semantic, new JObject());
                await _log.AppendLocalAsync(new[] { ev }, enqueueOutbox: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[EventPipeline] EmitInventory skipped: " + ex.Message);
            }
        }

        /// <summary>Emit a workflow event (note/check/task) — logged locally for history/fold.</summary>
        public static async Task EmitWorkflowAsync(string eventType, string waybillNo, JObject payload, string clientKey, CancellationToken ct = default)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(waybillNo)) return;
            try
            {
                var now = DateTime.UtcNow;
                // clientKey (stable per local row) keeps the workflow event fingerprint unique
                // per action while still deduping re-emits of the same action.
                string semantic = clientKey ?? (payload?.ToString(Newtonsoft.Json.Formatting.None) ?? "");
                var ev = NewEvent(waybillNo, eventType, now, FullStackEventSources.Manual, semantic, payload ?? new JObject());
                await _log.AppendLocalAsync(new[] { ev }, enqueueOutbox: false, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warning("[EventPipeline] EmitWorkflow skipped: " + ex.Message);
            }
        }

        private static FullStackEvent NewEvent(string waybillNo, string type, DateTime eventTime, string source, string semantic, JObject payload)
        {
            waybillNo = waybillNo.Trim().ToUpperInvariant();
            return new FullStackEvent
            {
                WaybillNo = waybillNo,
                EventType = type,
                EventTime = eventTime,
                Source = source,
                SourceClient = ClientId,
                Fingerprint = EventFingerprint.Compute(waybillNo, type, eventTime, semantic),
                Payload = payload?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}",
                ObservedAt = DateTime.UtcNow,
                SchemaVersion = 1
            };
        }

        private static bool IsEmpty(string v) => string.IsNullOrWhiteSpace(v) || v.Trim() == "empty";
        private static string Clean(string v) => IsEmpty(v) ? "" : v.Trim();

        private static DateTime? ParseVnTime(string v)
        {
            if (IsEmpty(v)) return null;
            return DateTime.TryParse(v, out var dt) ? dt.ToUniversalTime() : (DateTime?)null;
        }
    }
}
