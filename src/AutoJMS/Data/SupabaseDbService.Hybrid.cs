using AutoJMS.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Supabase.Realtime;
using Supabase.Realtime.PostgresChanges;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AutoJMS
{
    // Hybrid local-first + Supabase sync API (docs/hybrid-supabase-sync-plan.md).
    // All writes go through SECURITY DEFINER RPCs added by migration 202607110001_hybrid_sync.sql.
    public static partial class SupabaseDbService
    {
        /// <summary>True when a Supabase URL + key are resolvable (license/env/const).</summary>
        public static bool HasCredentials =>
            !string.IsNullOrWhiteSpace(ResolveUrl()) && !string.IsNullOrWhiteSpace(ResolveKey());

        private static async Task<string> RpcRawAsync(string fn, object args)
        {
            if (_client == null) await InitializeAsync().ConfigureAwait(false);
            var res = await _client.Rpc(fn, args).ConfigureAwait(false);
            return res.Content ?? "";
        }

        // ------------------------------------------------------------------
        // Site-scoped inventory lease
        // ------------------------------------------------------------------
        public static Task<bool> TryAcquireSiteLeaseAsync(string siteCode, int leaseSeconds = 1800) =>
            RpcAsync<bool>("try_acquire_site_lease", new { p_site_code = siteCode, p_owner_id = MachineId, p_lease_seconds = leaseSeconds });

        public static Task<bool> RefreshSiteLeaseAsync(string siteCode, int leaseSeconds = 1800) =>
            RpcAsync<bool>("refresh_site_lease", new { p_site_code = siteCode, p_owner_id = MachineId, p_lease_seconds = leaseSeconds });

        public static Task<bool> ReleaseSiteLeaseAsync(string siteCode) =>
            RpcAsync<bool>("release_site_lease", new { p_site_code = siteCode, p_owner_id = MachineId });

        // ------------------------------------------------------------------
        // Group A — dashboard waybill rows (newest-wins by updated_at)
        // ------------------------------------------------------------------
        public static async Task<int> MergeWaybillRowsV2Async(string siteCode, IReadOnlyList<object> rows)
        {
            if (rows == null || rows.Count == 0) return 0;
            int total = 0;
            foreach (var chunk in rows.Chunk(500))
                total += await RpcAsync<int>("merge_waybill_rows_v2", new { p_site_code = siteCode, p_rows = chunk }).ConfigureAwait(false);
            return total;
        }

        public static async Task<List<JObject>> PullWaybillDeltaAsync(string siteCode, DateTime sinceUtc, int limit = 1000)
        {
            var content = await RpcRawAsync("pull_waybill_delta",
                new { p_site_code = siteCode, p_since = sinceUtc, p_limit = limit }).ConfigureAwait(false);
            return ParseRows(content);
        }

        // ------------------------------------------------------------------
        // Group B — collaboration/workflow
        // ------------------------------------------------------------------
        public static Task<int> PushOrderNotesAsync(string siteCode, IReadOnlyList<object> rows) =>
            rows == null || rows.Count == 0
                ? Task.FromResult(0)
                : RpcAsync<int>("push_order_notes", new { p_site_code = siteCode, p_rows = rows });

        public static async Task<List<JObject>> PullOrderNotesAsync(string siteCode, DateTime sinceUtc, int limit = 1000)
        {
            var content = await RpcRawAsync("pull_order_notes",
                new { p_site_code = siteCode, p_since = sinceUtc, p_limit = limit }).ConfigureAwait(false);
            return ParseRows(content);
        }

        public static Task<int> MergeOrderChecksAsync(string siteCode, IReadOnlyList<object> rows) =>
            rows == null || rows.Count == 0
                ? Task.FromResult(0)
                : RpcAsync<int>("merge_order_checks", new { p_site_code = siteCode, p_rows = rows });

        public static async Task<List<JObject>> PullOrderChecksAsync(string siteCode, DateTime sinceUtc, int limit = 1000)
        {
            var content = await RpcRawAsync("pull_order_checks",
                new { p_site_code = siteCode, p_since = sinceUtc, p_limit = limit }).ConfigureAwait(false);
            return ParseRows(content);
        }

        public static Task<int> MergeDispatchTasksAsync(string siteCode, IReadOnlyList<object> rows) =>
            rows == null || rows.Count == 0
                ? Task.FromResult(0)
                : RpcAsync<int>("merge_dispatch_tasks", new { p_site_code = siteCode, p_rows = rows });

        public static async Task<List<JObject>> PullDispatchTasksAsync(string siteCode, DateTime sinceUtc, int limit = 1000)
        {
            var content = await RpcRawAsync("pull_dispatch_tasks",
                new { p_site_code = siteCode, p_since = sinceUtc, p_limit = limit }).ConfigureAwait(false);
            return ParseRows(content);
        }

        private static List<JObject> ParseRows(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return new List<JObject>();
            try
            {
                var token = JToken.Parse(content);
                return token is JArray array
                    ? array.OfType<JObject>().ToList()
                    : new List<JObject>();
            }
            catch (JsonException ex)
            {
                AppLogger.Warning("[HybridSync] RPC result parse failed: " + ex.Message);
                return new List<JObject>();
            }
        }

        // ------------------------------------------------------------------
        // Realtime "doorbell": any change on synced tables for this site fires
        // the callback. Payload is intentionally ignored — consistency comes
        // from the delta-pull (newest-wins), realtime only wakes it up.
        // ------------------------------------------------------------------
        private static readonly List<RealtimeChannel> _realtimeChannels = new();

        public static async Task<bool> SubscribeSiteChangesAsync(string siteCode, Action onAnyChange)
        {
            if (_client == null) await InitializeAsync().ConfigureAwait(false);

            var tables = new[] { "waybills", "order_notes", "order_checks", "dispatch_tasks" };
            bool anySubscribed = false;
            foreach (var table in tables)
            {
                try
                {
                    var channel = _client.Realtime.Channel("realtime", "public", table);
                    channel.Register(new PostgresChangesOptions(
                        "public", table,
                        PostgresChangesOptions.ListenType.All,
                        $"site_code=eq.{siteCode}"));
                    channel.AddPostgresChangeHandler(
                        PostgresChangesOptions.ListenType.All,
                        (_, _) =>
                        {
                            try { onAnyChange?.Invoke(); }
                            catch (Exception ex) { AppLogger.Warning("[HybridSync] realtime callback error: " + ex.Message); }
                        });
                    await channel.Subscribe().ConfigureAwait(false);
                    lock (_realtimeChannels) { _realtimeChannels.Add(channel); }
                    anySubscribed = true;
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[HybridSync] realtime subscribe failed table={table}: {ex.Message}");
                }
            }

            AppLogger.Info($"[HybridSync] realtime doorbell subscribed={anySubscribed} site={siteCode}");
            return anySubscribed;
        }

        public static void UnsubscribeSiteChanges()
        {
            lock (_realtimeChannels)
            {
                foreach (var channel in _realtimeChannels)
                {
                    try { channel.Unsubscribe(); }
                    catch (Exception ex) { AppLogger.Warning("[HybridSync] realtime unsubscribe error: " + ex.Message); }
                }
                _realtimeChannels.Clear();
            }
        }
    }
}
