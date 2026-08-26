using AutoJMS.Diagnostics;
using AutoJMS.Data;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AutoJMS;

/// <summary>
/// Result of a lease acquire. "Denied" and "Unreachable" must not be collapsed into one
/// boolean: denied means the server named another leader, while unreachable means nothing
/// is known — and if the caller treats unreachable as denied, no machine at the site pulls
/// JMS at all while the VPS is down.
/// </summary>
public enum DataHubLeaseOutcome
{
    Granted,
    Denied,
    Unreachable
}

/// <summary>
/// One page of the site change feed, already projected onto local row shape.
/// </summary>
public sealed class DataHubChangePage
{
    public DataHubChangePage(
        List<JObject> rows,
        long nextAfter,
        bool hasMore,
        bool resynced,
        bool truncated = false,
        List<string> deletedWaybillNos = null)
    {
        Rows = rows ?? new List<JObject>();
        NextAfter = nextAfter;
        HasMore = hasMore;
        Resynced = resynced;
        Truncated = truncated;
        DeletedWaybillNos = deletedWaybillNos ?? new List<string>();
    }

    public List<JObject> Rows { get; }

    /// <summary>Cursor to persist; the next read starts strictly after it.</summary>
    public long NextAfter { get; }

    /// <summary>More rows wait past <see cref="NextAfter"/> — pull again without waiting a full interval.</summary>
    public bool HasMore { get; }

    /// <summary>The cursor was older than the retained range, so <see cref="Rows"/> is a full snapshot.</summary>
    public bool Resynced { get; }

    /// <summary>
    /// The snapshot behind a <see cref="Resynced"/> page hit the server's row cap, so
    /// <see cref="Rows"/> is only part of the site's projection. Carried on the page rather
    /// than announced from inside <see cref="DataHubClient"/> because that class is static
    /// and holds no reference to a form; the sync service owns the UI channel and can put
    /// this in front of the operator, who is otherwise looking at a grid that is quietly
    /// missing its older waybills. Always false on a delta page — only a snapshot truncates.
    /// </summary>
    public bool Truncated { get; }

    /// <summary>
    /// Waybill numbers the server has deleted, carried separately from <see cref="Rows"/>
    /// because a deletion is not a row shape: its change body holds only the key.
    ///
    /// Kept apart rather than folded into <see cref="Rows"/> with a flag column so a caller
    /// that does not know about deletions cannot merge one as an upsert and blank out a good
    /// local row. Always empty on a <see cref="Resynced"/> page: a snapshot states what
    /// exists, not what was removed, and it may be <see cref="Truncated"/>, so absence from
    /// it is not evidence of deletion.
    /// </summary>
    public List<string> DeletedWaybillNos { get; }
}

/// <summary>
/// API-only data adapter. The desktop never connects to PostgreSQL directly;
/// all shared state is sent to the DataHub API hosted on the VPS.
///
/// Wire contract (AutoJMS.DataHub.Api):
///   * POST /lease/acquire  — no body; the response carries the fencing term.
///   * POST /lease/renew    — body { leaderTerm }; the term does NOT change.
///   * POST /lease/release  — body { leaderTerm }; the server then bumps the term.
///   * POST /jms/ingest     — bulk; requires the X-Leader-Term header (409 without it).
///   * POST /jms/observations — interactive; unfenced.
///   * GET  /changes?after=&lt;change_seq&gt; — delta feed; 409 RESYNC_REQUIRED when the cursor
///                            is older than the retained range, then /projections/snapshot.
///   * WS   /hubs/site      — doorbell; the server invokes the client method "change".
/// Both ingest paths accept exactly the JmsObservation shape and the API runs with
/// JsonUnmappedMemberHandling.Disallow, so an extra JSON member is a hard 400. Every
/// outgoing row is therefore normalized in <see cref="ToIngestItem"/> and its original
/// form is carried in `payload`.
/// </summary>
public static class DataHubClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly object ConfigLock = new();
    private static string _baseUrl = string.Empty;
    private static string _deviceToken = string.Empty;
    private static string _siteId = string.Empty;

    private static readonly object LeaseLock = new();
    private static long _leaderTerm;

    private static readonly object MachineIdLock = new();
    private static string _machineId;

    private static readonly System.Threading.SemaphoreSlim RealtimeGate = new(1, 1);
    private static HubConnection _hubConnection;
    private static string _hubEndpoint;
    private static volatile Action _realtimeCallback;

    private static int _auxiliaryWarningLogged;
    private static int _snapshotTruncationLogged;
    private static int _siteIdMalformedLogged;

    /// <summary>The only entity type the change feed publishes today.</summary>
    private const string WaybillProjectionEntity = "waybill_projection";

    /// <summary>
    /// The change operation that says a row is gone. Emitted by the server's retention pass,
    /// never by ingest, so it is the one operation that arrives without a usable body.
    /// </summary>
    private const string DeleteOperation = "delete";

    /// <summary>
    /// Mirrors IngestRepository's hard cap of 200 observations per request. Exceeding it is a
    /// 413 PAYLOAD_TOO_LARGE, so this is a limit to chunk at, not a limit to discover.
    /// </summary>
    private const int MaximumIngestItems = 200;

    /// <summary>
    /// Mirrors ChangeRepository.DefaultSnapshotRows. The snapshot endpoint rejects a limit above
    /// MaximumSnapshotRows (10000) with 400 rather than clamping it, so the client must ask for a
    /// value the server accepts instead of omitting the parameter and taking whatever it gets.
    /// </summary>
    private const int SnapshotPageLimit = 5000;

    /// <summary>
    /// JMS writes naive local time and the local tables store it that way. Vietnam has no DST,
    /// so a fixed offset is exact and keeps this off the Windows time-zone database.
    /// </summary>
    private static readonly TimeSpan JmsUtcOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Stable per-installation identity. It used to embed a fresh Guid per process, which
    /// made every launch a different machine: the sync cache file name in Main.cs changed
    /// on each start (orphan files, cold cache) and the event pipeline's actor id never
    /// matched across restarts. It is now persisted next to the other user data.
    /// </summary>
    public static string MachineId
    {
        get
        {
            lock (MachineIdLock)
            {
                if (!string.IsNullOrWhiteSpace(_machineId)) return _machineId;
                _machineId = LoadOrCreateMachineId();
                return _machineId;
            }
        }
    }

    /// <summary>Current fencing term, or 0 when this process does not hold the site lease.</summary>
    public static long CurrentLeaderTerm
    {
        get { lock (LeaseLock) return _leaderTerm; }
    }

    public static bool HasSiteLease => CurrentLeaderTerm > 0;

    /// <summary>
    /// True when the API models notes / checks / tasks / waybill events as first-class
    /// entities. It does not: the only write contract the server exposes is JmsObservation.
    /// Forcing those rows into an observation would feed the server-side projection reducer
    /// records that are not JMS scans and corrupt the projection, so they stay in the local
    /// outbox until dedicated endpoints (and their migrations) exist.
    /// </summary>
    public static bool SupportsAuxiliaryEntitySync => false;

    public static bool HasCredentials
    {
        get { lock (ConfigLock) return !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_deviceToken); }
    }

    /// <summary>True when a real site GUID is configured; the site lease/ingest paths need it.</summary>
    public static bool HasSiteId
    {
        get { lock (ConfigLock) return Guid.TryParse(_siteId, out _); }
    }

    public static void Configure(string baseUrl = null, string deviceToken = null, string siteId = null)
    {
        lock (ConfigLock)
        {
            _baseUrl = FirstNonEmpty(baseUrl, Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_API_BASE_URL"))?.TrimEnd('/') ?? string.Empty;
            _deviceToken = FirstNonEmpty(deviceToken, Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_DEVICE_TOKEN")) ?? string.Empty;
            _siteId = FirstNonEmpty(siteId, Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_SITE_ID")) ?? string.Empty;
        }
        lock (LeaseLock) _leaderTerm = 0;
        // A new site value deserves a fresh verdict: if it is malformed too, TryGetSiteId must
        // be free to say so rather than stay silent because an earlier value already failed.
        System.Threading.Interlocked.Exchange(ref _siteIdMalformedLogged, 0);
        // Any live hub connection was authorized with the previous token and site, so it is
        // dropped here; the next sync cycle re-subscribes with what we were just given.
        UnsubscribeSiteChanges();
        AppLogger.Info("DataHub API configured baseUrl=" + _baseUrl + ", token=" + (string.IsNullOrWhiteSpace(_deviceToken) ? "<missing>" : TokenRedactor.MaskToken(_deviceToken)));
    }

    public static Task InitializeAsync() => Task.CompletedTask;

    // The lease TTL is fixed at 120 s by the server (LeaseState.LeaseDurationSeconds) and
    // cannot be negotiated. The leaseSeconds parameters below are kept only so existing
    // call sites keep compiling; the value is ignored.
    public static Task<bool> TryAcquireInventoryLeaseAsync(int leaseSeconds = 120) =>
        TryAcquireSiteLeaseAsync(ResolveSiteCode(), leaseSeconds);

    public static Task<bool> RefreshInventoryLeaseAsync(int leaseSeconds = 120) =>
        RefreshSiteLeaseAsync(ResolveSiteCode(), leaseSeconds);

    public static Task<bool> ReleaseInventoryLeaseAsync() =>
        ReleaseSiteLeaseAsync(ResolveSiteCode());

    public static Task<bool> CompleteInventorySyncAsync() => Task.FromResult(true);

    public static Task<bool> UpdateInventorySyncHeartbeatAsync(string ownerId) =>
        RefreshSiteLeaseAsync(ResolveSiteCode(), 120);

    public static Task<int> UpsertNewWaybillsOnlyAsync(IEnumerable<string> fetchedWaybills) =>
        Task.FromResult(fetchedWaybills?.Count(x => !string.IsNullOrWhiteSpace(x)) ?? 0);

    // Bulk JMS observations come from the Windows Service with the original
    // scanTime. The desktop must not invent a business timestamp.
    public static Task<int> IngestBigDataWaybillsAsync(string siteCode, IEnumerable<string> codes) =>
        Task.FromResult(0);

    public static Task<int> IngestStockCheckWaybillsAsync(string siteCode, IEnumerable<string> codes) =>
        IngestBigDataWaybillsAsync(siteCode, codes);

    public static Task<int> ReconcileInventorySourcesAsync(string siteCode) => Task.FromResult(0);

    public static Task<List<WaybillDbModel>> GetActiveWaybillsAsync(int pageSize = 1000) =>
        ReadSnapshotAsync(pageSize);

    public static Task<List<string>> GetWaybillsDueForTrackingAsync(int pageSize = 1000) =>
        ReadSnapshotAsync(pageSize).ContinueWith(
            task => task.Status == TaskStatus.RanToCompletion
                ? task.Result.Select(row => row.WaybillNo).Where(code => !string.IsNullOrWhiteSpace(code)).ToList()
                : new List<string>(),
            TaskScheduler.Default);

    public static Task UpsertManyWaybillsAsync(List<WaybillDbModel> rows) =>
        SendObservationBatchAsync(ResolveSiteCode(), rows?
            .Where(row => row != null && row.NextTrackAt != default)
            .Select(ToObservation)
            .ToList());

    public static async Task<bool> TryAcquireSiteLeaseAsync(string siteCode, int leaseSeconds = 120) =>
        await AcquireSiteLeaseAsync(siteCode).ConfigureAwait(false) == DataHubLeaseOutcome.Granted;

    /// <summary>Acquire that also tells the caller whether the server answered at all.</summary>
    public static Task<DataHubLeaseOutcome> AcquireSiteLeaseAsync(string siteCode) =>
        AcquireLeaseAsync(siteCode);

    public static Task<bool> RefreshSiteLeaseAsync(string siteCode, int leaseSeconds = 120) =>
        RenewLeaseAsync(siteCode);

    public static Task<bool> ReleaseSiteLeaseAsync(string siteCode) =>
        ReleaseLeaseAsync(siteCode);

    public static Task<int> MergeWaybillRowsV2Async(string siteCode, IReadOnlyList<object> rows) =>
        SendObservationBatchAsync(siteCode, rows?.Select(ToJObject).ToList());

    // ── Not yet expressible on the wire ───────────────────────────────────
    // These four used to POST their rows to /jms/observations, where an unmapped member is
    // a hard 400: every call was a guaranteed round-trip failure that the caller then
    // recorded as "flushed". They now return 0 without pretending to have sent anything;
    // FlushOutboxAsync checks SupportsAuxiliaryEntitySync and leaves the rows in the outbox.
    public static Task<int> PushOrderNotesAsync(string siteCode, IReadOnlyList<object> rows) =>
        AuxiliaryEntityNotSupported("notes", rows);

    public static Task<int> MergeOrderChecksAsync(string siteCode, IReadOnlyList<object> rows) =>
        AuxiliaryEntityNotSupported("checks", rows);

    public static Task<int> MergeDispatchTasksAsync(string siteCode, IReadOnlyList<object> rows) =>
        AuxiliaryEntityNotSupported("tasks", rows);

    public static Task<int> AppendWaybillEventsAsync(string siteCode, IReadOnlyList<object> events) =>
        AuxiliaryEntityNotSupported("events", events);

    public static Task<List<JObject>> PullOrderNotesAsync(string siteCode, DateTime sinceUtc, int limit = 1000) =>
        Task.FromResult(new List<JObject>());

    public static Task<List<JObject>> PullOrderChecksAsync(string siteCode, DateTime sinceUtc, int limit = 1000) =>
        Task.FromResult(new List<JObject>());

    public static Task<List<JObject>> PullDispatchTasksAsync(string siteCode, DateTime sinceUtc, int limit = 1000) =>
        Task.FromResult(new List<JObject>());

    /// <summary>
    /// Reads the change feed as local event-log rows. The feed carries projection changes rather
    /// than domain events, so every change becomes exactly one "waybill_projection" record whose
    /// id and fingerprint are derived from its change_seq: re-reading a page must not append a
    /// second copy locally, and the seq must survive so the cursor can advance.
    /// </summary>
    public static async Task<List<JObject>> PullEventsDeltaAsync(string siteCode, long sinceSeq, int limit = 2000)
    {
        var page = await ReadChangePageAsync(siteCode, sinceSeq, limit).ConfigureAwait(false);
        var rows = new List<JObject>(page.Items.Count);
        foreach (var item in page.Items)
        {
            long seq = item.Value<long?>("changeSeq") ?? 0;
            if (seq <= 0) continue;

            // Tombstones are deliberately NOT skipped here, tempting though it looks: this
            // cursor is MAX(remote_seq) over the rows actually stored, so dropping a change
            // stops the cursor dead and a page of nothing but tombstones would be re-read
            // forever. They are harmless as event rows — FoldProjectionAsync folds only
            // TrackingObserved and OrderDetailObserved, so a "waybill_projection" row with a
            // key-only payload is inert. The deletion itself is applied from the waybill
            // pull, which carries operation separately.
            var body = item["body"] as JObject;
            var waybillNo = FirstString(item, "entityKey") ?? (body == null ? null : FirstString(body, "waybillNo"));
            if (string.IsNullOrWhiteSpace(waybillNo)) continue;   // the event log rejects these anyway

            var changeAt = FirstString(item, "changeAt");
            var key = "datahub:" + seq.ToString(CultureInfo.InvariantCulture);
            rows.Add(new JObject
            {
                ["seq"] = seq,
                ["event_id"] = key,
                ["fingerprint"] = key,
                ["waybill_no"] = waybillNo.Trim().ToUpperInvariant(),
                ["event_type"] = FirstString(item, "entityType") ?? WaybillProjectionEntity,
                ["event_time"] = ToIsoUtc(body == null ? changeAt : FirstString(body, "updatedAt") ?? changeAt),
                ["observed_at"] = ToIsoUtc(changeAt),
                ["source"] = "datahub",
                ["source_client"] = string.Empty,
                ["schema_version"] = body?.Value<int?>("reducerVersion") ?? 1,
                ["payload"] = body ?? new JObject()
            });
        }
        return rows;
    }

    // ── Realtime doorbell (/hubs/site) ────────────────────────────────────

    /// <summary>
    /// Opens (or reuses) the SignalR connection that tells us a change landed. The doorbell only
    /// says "something changed" — the delta itself is still read over /changes — so a missed or
    /// duplicated ring costs at most one extra pull. Returns false when realtime is unavailable;
    /// the caller must keep its periodic pull as the floor.
    /// </summary>
    public static async Task<bool> SubscribeSiteChangesAsync(string siteCode, Action onAnyChange)
    {
        if (onAnyChange == null) return false;
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out _)) return false;

        _realtimeCallback = onAnyChange;
        var endpoint = baseUrl + "/hubs/site";

        await RealtimeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var existing = _hubConnection;
            if (existing != null
                && string.Equals(_hubEndpoint, endpoint, StringComparison.OrdinalIgnoreCase)
                && existing.State != HubConnectionState.Disconnected)
            {
                return true;   // already connected, or reconnecting on its own
            }

            if (existing != null)
            {
                _hubConnection = null;
                _hubEndpoint = null;
                await SafeDisposeHubAsync(existing).ConfigureAwait(false);
            }

            HubConnection connection = null;
            try
            {
                connection = BuildHubConnection(endpoint);
                await connection.StartAsync().ConfigureAwait(false);
                _hubConnection = connection;
                _hubEndpoint = endpoint;
                AppLogger.Info("DataHub realtime connected site=" + siteId.ToString("D"));
                return true;
            }
            catch (Exception ex)
            {
                // Not fatal — the periodic pull still sees every change, just later. What would
                // be a bug is leaving a half-built connection behind to retry in the background.
                AppLogger.Warning("DataHub realtime unavailable (" + ex.Message + "); polling only.");
                if (connection != null) await SafeDisposeHubAsync(connection).ConfigureAwait(false);
                return false;
            }
        }
        finally
        {
            RealtimeGate.Release();
        }
    }

    public static void UnsubscribeSiteChanges()
    {
        _realtimeCallback = null;
        var connection = System.Threading.Interlocked.Exchange(ref _hubConnection, null);
        _hubEndpoint = null;
        if (connection == null) return;
        // Fire-and-forget: the caller is usually the UI thread closing the window, and
        // DisposeAsync waits for the server round-trip before it returns.
        _ = Task.Run(() => SafeDisposeHubAsync(connection));
    }

    private static HubConnection BuildHubConnection(string endpoint)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(endpoint, options =>
            {
                // SiteHub aborts any connection without a device identity. The Bearer header
                // covers negotiate; the WebSocket transport cannot send headers, so the client
                // appends ?access_token= — the one query-string token the server's
                // DeviceAuthenticationMiddleware accepts, and only on /hubs/site. Reading the
                // token per negotiate (not once) means a re-enroll is picked up on reconnect.
                options.AccessTokenProvider = () => Task.FromResult(CurrentDeviceToken);
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            })
            .Build();

        connection.On<SiteChangeDoorbell>("change", doorbell =>
        {
            AppLogger.Debug("DataHub doorbell seq=" + (doorbell?.ChangeSeq ?? 0) + " key=" + (doorbell?.EntityKey ?? ""));
            RingRealtimeCallback();
        });

        // Doorbells rang while we were away, so a reconnect is itself a reason to pull.
        connection.Reconnected += _ =>
        {
            AppLogger.Info("DataHub realtime reconnected — requesting a catch-up pull.");
            RingRealtimeCallback();
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            AppLogger.Warning("DataHub realtime closed: " + (error?.Message ?? "no error"));
            return Task.CompletedTask;
        };

        return connection;
    }

    private static void RingRealtimeCallback()
    {
        var callback = _realtimeCallback;
        if (callback == null) return;
        // The hub dispatcher owns this thread: an exception escaping here tears the connection
        // down, so a broken subscriber must only cost its own notification.
        try { callback(); }
        catch (Exception ex) { AppLogger.Warning("DataHub doorbell handler failed: " + ex.Message); }
    }

    private static async Task SafeDisposeHubAsync(HubConnection connection)
    {
        try { await connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { AppLogger.Debug("DataHub realtime dispose: " + ex.Message); }
    }

    /// <summary>Mirror of the server's ChangeDoorbell; only the seq and key are used, for logging.</summary>
    private sealed class SiteChangeDoorbell
    {
        public Guid SiteId { get; set; }
        public long ChangeSeq { get; set; }
        public string EntityType { get; set; }
        public string EntityKey { get; set; }
    }

    // ── Lease state machine ───────────────────────────────────────────────

    private static async Task<DataHubLeaseOutcome> AcquireLeaseAsync(string siteCode)
    {
        // Not configured is reported as Unreachable, not Denied: nothing was asked, so the
        // caller must fall back to acting locally rather than believe another machine leads.
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token))
        {
            lock (LeaseLock) _leaderTerm = 0;
            return DataHubLeaseOutcome.Unreachable;
        }

        // AcquireAsync takes no body parameter on the server, so none is sent.
        var (state, definitive) = await SendLeaseRequestAsync(baseUrl, siteId, token, "acquire", null).ConfigureAwait(false);
        if (state == null)
        {
            lock (LeaseLock) _leaderTerm = 0;
            return definitive ? DataHubLeaseOutcome.Denied : DataHubLeaseOutcome.Unreachable;
        }

        var term = state.Value<long?>("leaderTerm") ?? 0;
        if (term < 1)
        {
            AppLogger.Warning("DataHub lease acquire returned no leaderTerm; treating as not leader.");
            lock (LeaseLock) _leaderTerm = 0;
            return DataHubLeaseOutcome.Denied;
        }

        lock (LeaseLock) _leaderTerm = term;
        AppLogger.Info("DataHub lease acquired term=" + term.ToString(CultureInfo.InvariantCulture));
        return DataHubLeaseOutcome.Granted;
    }

    private static async Task<bool> RenewLeaseAsync(string siteCode)
    {
        long term = CurrentLeaderTerm;
        if (term < 1)
        {
            // Nothing to renew — the caller must acquire first. Renewing with a guessed term
            // is what the server's fence exists to reject.
            return false;
        }
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token)) return false;

        var (state, _) = await SendLeaseRequestAsync(baseUrl, siteId, token, "renew", term).ConfigureAwait(false);
        if (state == null)
        {
            // Fenced, expired or unreachable: drop the term so the next cycle re-acquires
            // instead of sending bulk ingest under a term the server no longer honours.
            lock (LeaseLock) _leaderTerm = 0;
            return false;
        }

        var renewed = state.Value<long?>("leaderTerm") ?? term;
        lock (LeaseLock) _leaderTerm = renewed;
        return true;
    }

    private static async Task<bool> ReleaseLeaseAsync(string siteCode)
    {
        long term = CurrentLeaderTerm;
        lock (LeaseLock) _leaderTerm = 0;
        if (term < 1) return true;
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token)) return false;

        var (state, _) = await SendLeaseRequestAsync(baseUrl, siteId, token, "release", term).ConfigureAwait(false);
        return state != null;
    }

    /// <summary>
    /// Returns the parsed lease state, plus whether the server gave a DEFINITIVE answer about
    /// leadership. A null state with Definitive=true means another machine really does hold the
    /// lease (409 fenced / lease held). Definitive=false means nothing about leadership is
    /// known — the network, the device token or the VPS is the problem.
    ///
    /// Only 409 is definitive. Every other failure (401 stale device token, 403 suspended, 429
    /// throttled, 5xx) says nothing about who leads, and reporting one as Denied is worse than
    /// reporting it as unknown: Denied makes this station stand down believing a peer took over,
    /// so when the real cause is an expired token or a down VPS every station stands down at
    /// once and the site stops ingesting with nothing in the log but "lease denied".
    /// </summary>
    private static async Task<(JObject State, bool Definitive)> SendLeaseRequestAsync(string baseUrl, Guid siteId, string token, string action, long? leaderTerm)
    {
        var endpoint = baseUrl + "/api/v1/sites/" + siteId.ToString("D") + "/lease/" + action;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (leaderTerm.HasValue)
        {
            request.Content = new StringContent(
                JsonConvert.SerializeObject(new { leaderTerm = leaderTerm.Value }),
                Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 409 is the normal "someone else is leader" / "your term is stale" answer,
                // so it is logged as information rather than as a failure.
                bool fenced = response.StatusCode == HttpStatusCode.Conflict;
                var text = "DataHub lease " + action + " -> " + (int)response.StatusCode + " " + Truncate(body, 300);
                if (fenced) AppLogger.Info(text);
                else AppLogger.Warning(text + " (leadership unknown, not denied)");
                return (null, fenced);
            }
            return (string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body), true);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub lease " + action + " failed: " + ex.Message);
            return (null, false);
        }
    }

    // ── Ingest ────────────────────────────────────────────────────────────

    private static async Task<int> SendObservationBatchAsync(string siteCode, List<JObject> rows)
    {
        if (rows == null || rows.Count == 0) return 0;
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token)) return 0;

        var items = new List<JObject>(rows.Count);
        int dropped = 0;
        foreach (var row in rows)
        {
            var item = ToIngestItem(row);
            if (item == null) dropped++;
            else items.Add(item);
        }
        if (dropped > 0)
            AppLogger.Warning("DataHub ingest skipped " + dropped + " row(s) without a usable waybillNo/scanTime.");
        if (items.Count == 0) return 0;

        // Holding the fencing term means this is the site leader doing a bulk push, which is
        // exactly what /jms/ingest is for. Without a term the write is an interactive one and
        // must go to the unfenced /jms/observations, or the server answers 409 LEADER_FENCED.
        long term = CurrentLeaderTerm;
        bool bulk = term > 0;
        var endpoint = baseUrl + "/api/v1/sites/" + siteId.ToString("D") + (bulk ? "/jms/ingest" : "/jms/observations");

        // The server caps a request at MaximumIngestItems observations, so anything larger is a
        // guaranteed 413. Callers routinely hand this method a page of 1000 rows, and the whole
        // page used to be posted as one request, rejected, logged, and thrown away — while
        // UpsertManyWaybillsAsync discards the return value, so nothing surfaced the loss.
        int accepted = 0;
        for (int offset = 0; offset < items.Count; offset += MaximumIngestItems)
        {
            var chunk = items.GetRange(offset, Math.Min(MaximumIngestItems, items.Count - offset));
            var (sent, fenced) = await SendIngestChunkAsync(endpoint, token, chunk, bulk, term).ConfigureAwait(false);
            accepted += sent;
            if (fenced)
            {
                // The fence rejected us: stop claiming leadership until a fresh acquire, and do
                // not keep pushing the remaining chunks under a term the server no longer honours.
                lock (LeaseLock) _leaderTerm = 0;
                AppLogger.Warning("DataHub ingest fenced after " + accepted.ToString(CultureInfo.InvariantCulture)
                    + " of " + items.Count.ToString(CultureInfo.InvariantCulture) + " observation(s); abandoning the rest of the batch.");
                break;
            }
        }

        return accepted;
    }

    /// <summary>
    /// Posts one chunk, halving and retrying if the server still answers 413. The item cap is
    /// not the only 413: the body is also capped at 1 MiB, and observation payloads carry a
    /// clone of the whole source row, so a chunk that is legal by count can still be too fat.
    /// Halving finds the workable size without needing to model the server's byte budget here.
    /// Returns the number of observations the server accepted, and whether the fence rejected us.
    /// </summary>
    private static async Task<(int Accepted, bool Fenced)> SendIngestChunkAsync(
        string endpoint, string token, List<JObject> chunk, bool bulk, long term)
    {
        if (chunk.Count == 0) return (0, false);

        var payload = JsonConvert.SerializeObject(new { items = chunk });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Content-derived, not random: a retry of the same batch must reuse the same key or
        // the server's idempotency check cannot dedupe it. Deriving it from the chunk rather
        // than the whole batch is what makes splitting safe -- each half gets its own key, so
        // neither is mistaken for a replay of the request that was rejected.
        request.Headers.Add("Idempotency-Key", ComputeIdempotencyKey(payload));
        if (bulk) request.Headers.Add("X-Leader-Term", term.ToString(CultureInfo.InvariantCulture));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return (chunk.Count, false);

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AppLogger.Warning("DataHub ingest (" + chunk.Count.ToString(CultureInfo.InvariantCulture)
                + " item(s)) -> " + (int)response.StatusCode + " " + Truncate(body, 400));

            if (bulk && response.StatusCode == HttpStatusCode.Conflict) return (0, true);
            if (response.StatusCode != HttpStatusCode.RequestEntityTooLarge) return (0, false);

            if (chunk.Count == 1)
            {
                // One observation the server will not take at any size. Dropping it is the only
                // option left, but say so explicitly instead of folding it into a batch total.
                AppLogger.Warning("DataHub ingest dropped a single oversized observation: "
                    + Truncate(chunk[0].Value<string>("waybillNo") ?? "(no waybillNo)", 64));
                return (0, false);
            }

            int half = chunk.Count / 2;
            var (leftSent, leftFenced) = await SendIngestChunkAsync(
                endpoint, token, chunk.GetRange(0, half), bulk, term).ConfigureAwait(false);
            if (leftFenced) return (leftSent, true);
            var (rightSent, rightFenced) = await SendIngestChunkAsync(
                endpoint, token, chunk.GetRange(half, chunk.Count - half), bulk, term).ConfigureAwait(false);
            return (leftSent + rightSent, rightFenced);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub observation ingest failed: " + ex.Message);
            return (0, false);
        }
    }

    /// <summary>
    /// Projects an arbitrary local row onto the JmsObservation contract. Anything the
    /// contract has no member for travels inside `payload`, because the API rejects
    /// unmapped members outright.
    /// </summary>
    internal static JObject ToIngestItem(JObject row)
    {
        if (row == null) return null;

        var waybillNo = FirstString(row, "waybillNo", "waybill_no", "WaybillNo");
        if (string.IsNullOrWhiteSpace(waybillNo)) return null;

        var rawScanTime = FirstString(row, "scanTime", "scan_time",
            "thoi_gian_thao_tac", "last_action_time", "lastActionTime", "updated_at", "updatedAt");
        if (!TryNormalizeScanTime(rawScanTime, out var scanTime)) return null;

        var item = new JObject
        {
            ["waybillNo"] = waybillNo.Trim(),
            ["scanTime"] = scanTime,
            ["payload"] = row.DeepClone()
        };

        AddIfPresent(item, "status", FirstString(row, "status", "current_status", "trang_thai_hien_tai"));
        AddIfPresent(item, "scanTypeName", FirstString(row, "scanTypeName", "last_action", "thao_tac_cuoi"));
        AddIfPresent(item, "scanNetworkCode", FirstString(row, "scanNetworkCode", "last_site_code", "buu_cuc_thao_tac"));
        AddIfPresent(item, "scanByCode", FirstString(row, "scanByCode", "employee_code", "nguoi_thao_tac"));
        AddIfPresent(item, "packageNumber", FirstString(row, "packageNumber", "package_number"));
        AddIfPresent(item, "taskCode", FirstString(row, "taskCode", "task_code"));

        var code = row.Value<int?>("code");
        if (code.HasValue) item["code"] = code.Value;

        return item;
    }

    /// <summary>
    /// Emits a value ScanTimeParser accepts. A naive timestamp is left naive on purpose —
    /// the server reads it as Asia/Ho_Chi_Minh, which is what JMS produced — while an
    /// offset-bearing value is converted to UTC so no interpretation is left to the VPS.
    /// </summary>
    internal static bool TryNormalizeScanTime(string raw, out string normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var candidate = raw.Trim();

        if (DateTime.TryParseExact(candidate, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            normalized = candidate;
            return true;
        }

        bool hasOffset = candidate.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(candidate, @"[+-]\d{2}:?\d{2}$");
        if (hasOffset && DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var withOffset))
        {
            normalized = withOffset.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
            return true;
        }

        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault, out var naive) && naive != default)
        {
            normalized = naive.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static string ComputeIdempotencyKey(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty));
        return Convert.ToHexString(hash).ToLowerInvariant();   // 64 chars, inside the server's 8..128 window
    }

    private static Task<int> AuxiliaryEntityNotSupported(string kind, IReadOnlyList<object> rows)
    {
        if (System.Threading.Interlocked.Exchange(ref _auxiliaryWarningLogged, 1) == 0)
        {
            AppLogger.Warning(
                "DataHub has no endpoint for notes/checks/tasks/events yet (the API only accepts " +
                "JmsObservation). Those rows stay in the local outbox and are not sent. " +
                "First blocked kind=" + kind + ".");
        }
        else
        {
            AppLogger.Debug("DataHub auxiliary push skipped kind=" + kind + " rows=" + (rows?.Count ?? 0));
        }
        return Task.FromResult(0);
    }

    // ── Reads ─────────────────────────────────────────────────────────────

    private static async Task<List<WaybillDbModel>> ReadSnapshotAsync(int limit)
    {
        if (!TryGetSiteId(ResolveSiteCode(), out var siteId) || !TryGetConfig(out var baseUrl, out var token))
            return new List<WaybillDbModel>();

        using var request = CreateAuthorizedRequest(HttpMethod.Get,
            $"{baseUrl}/api/v1/sites/{siteId:D}/projections/snapshot?limit={Math.Clamp(limit, 1, SnapshotPageLimit)}", token);
        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new List<WaybillDbModel>();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            // Checked here too, not only in ReadSnapshotRowsAsync: this is the path behind
            // GetActiveWaybillsAsync and the tracking due-list, so a silent truncation here
            // means those callers work from a short list and believe it is the whole site.
            if (json.Value<bool?>("truncated") == true)
                WarnSnapshotTruncated(Math.Clamp(limit, 1, SnapshotPageLimit));
            return json["items"] is not JArray items
                ? new List<WaybillDbModel>()
                : items.OfType<JObject>().Select(ToWaybill).Where(row => row != null).ToList();
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub snapshot read failed: " + ex.Message);
            return new List<WaybillDbModel>();
        }
    }

    /// <summary>
    /// Reads the change feed and projects it onto the local waybill row shape. The cursor is the
    /// server's change_seq, never a clock: /changes is ordered by an append-only sequence, and a
    /// timestamp cursor silently skips rows committed out of order.
    /// </summary>
    public static async Task<DataHubChangePage> PullWaybillChangesAsync(string siteCode, long afterSeq, int limit = 500)
    {
        var page = await ReadChangePageAsync(siteCode, afterSeq, limit).ConfigureAwait(false);
        if (page.ResyncRequired)
        {
            // The cursor is older than the retained change range, so the delta no longer exists.
            // A snapshot is the only way back to a consistent view.
            var (rows, snapshotSeq, truncated) = await ReadSnapshotRowsAsync(siteCode).ConfigureAwait(false);
            return new DataHubChangePage(rows, snapshotSeq > 0 ? snapshotSeq : afterSeq, false, true, truncated);
        }

        var (mapped, deleted) = ProjectChangeItems(page.Items);
        return new DataHubChangePage(mapped, page.NextAfter, page.HasMore, false, false, deleted);
    }

    /// <summary>
    /// Splits one raw change page into rows to merge and keys to delete. Extracted from the
    /// pull so the operation routing can be tested without a server: reading a tombstone as
    /// an upsert writes a blank record over a good one, and the resulting local row looks
    /// perfectly valid, so nothing downstream would report it.
    /// </summary>
    internal static (List<JObject> Rows, List<string> Deleted) ProjectChangeItems(IReadOnlyList<JObject> items)
    {
        var mapped = new List<JObject>(items?.Count ?? 0);
        var deleted = new List<string>();
        if (items == null) return (mapped, deleted);

        foreach (var item in items)
        {
            if (item == null) continue;

            // Only waybill projections belong in fs_waybills; anything else the server starts
            // publishing here must be ignored rather than merged blindly.
            if (!string.Equals(item.Value<string>("entityType"), WaybillProjectionEntity, StringComparison.OrdinalIgnoreCase))
                continue;

            // A tombstone's body carries only the key, so it must be read off entityKey and
            // routed away from the upsert path. The operation field is the only thing
            // separating the two, and ignoring it is what left deleted waybills sitting in
            // local SQLite forever.
            if (string.Equals(item.Value<string>("operation"), DeleteOperation, StringComparison.OrdinalIgnoreCase))
            {
                var waybillNo = FirstString(item, "entityKey");
                if (!string.IsNullOrWhiteSpace(waybillNo))
                    deleted.Add(waybillNo.Trim().ToUpperInvariant());
                continue;
            }

            var row = ToWaybillRow(item["body"] as JObject);
            if (row != null) mapped.Add(row);
        }

        return (mapped, deleted);
    }

    private static async Task<(List<JObject> Items, long NextAfter, bool HasMore, bool ResyncRequired)> ReadChangePageAsync(
        string siteCode, long after, int limit)
    {
        long cursor = Math.Max(0, after);
        (List<JObject>, long, bool, bool) Nothing() => (new List<JObject>(), cursor, false, false);

        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token)) return Nothing();

        using var request = CreateAuthorizedRequest(HttpMethod.Get,
            $"{baseUrl}/api/v1/sites/{siteId:D}/changes?after={cursor}&limit={Math.Clamp(limit, 1, 5000)}", token);
        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                AppLogger.Warning("DataHub change feed wants a resync: " + Truncate(body, 200));
                return (new List<JObject>(), cursor, false, true);
            }
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warning("DataHub change read -> " + (int)response.StatusCode + " " + Truncate(body, 200));
                return Nothing();
            }

            var json = JObject.Parse(body);
            var items = json["items"] is JArray array ? array.OfType<JObject>().ToList() : new List<JObject>();
            // Never move the cursor backwards: a malformed nextAfter would otherwise replay the
            // whole retained range on every cycle.
            long nextAfter = Math.Max(json.Value<long?>("nextAfter") ?? cursor, cursor);
            return (items, nextAfter, json.Value<bool?>("hasMore") ?? false, false);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub change read failed: " + ex.Message);
            return Nothing();
        }
    }

    private static async Task<(List<JObject> Rows, long SnapshotSeq, bool Truncated)> ReadSnapshotRowsAsync(string siteCode)
    {
        var rows = new List<JObject>();
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token)) return (rows, 0, false);

        // Always ask for an explicit limit. Omitting it left the server on its own default and
        // gave no way to tell a complete snapshot from a truncated one.
        using var request = CreateAuthorizedRequest(HttpMethod.Get,
            $"{baseUrl}/api/v1/sites/{siteId:D}/projections/snapshot?limit={SnapshotPageLimit}", token);
        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warning("DataHub snapshot read -> " + (int)response.StatusCode);
                return (rows, 0, false);
            }

            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (json["items"] is JArray items)
            {
                foreach (var item in items.OfType<JObject>())
                {
                    var row = ToWaybillRow(item);
                    if (row != null) rows.Add(row);
                }
            }
            // A truncated snapshot is the one failure here that looks like success: the rows that
            // did arrive are correct and the delta resumes cleanly, so the tail of the projection
            // is simply missing locally with nothing else to show it.
            bool truncated = json.Value<bool?>("truncated") == true;
            if (truncated) WarnSnapshotTruncated(SnapshotPageLimit);
            // snapshot_seq is the change_seq the snapshot was taken at, so resuming the delta
            // from it neither replays nor skips.
            return (rows, json.Value<long?>("snapshot_seq") ?? 0, truncated);
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub snapshot read failed: " + ex.Message);
            return (rows, 0, false);
        }
    }

    /// <summary>
    /// Records a snapshot the server had to cut short. Latched, because the condition is
    /// standing rather than momentary: the rows past the cap stay missing until they change
    /// again, so an unlatched warning repeats on every read for the life of the process and
    /// buries the rest of the log. The operator-facing half of this travels separately, on
    /// <see cref="DataHubChangePage.Truncated"/>.
    /// </summary>
    private static void WarnSnapshotTruncated(int limit)
    {
        if (System.Threading.Interlocked.Exchange(ref _snapshotTruncationLogged, 1) != 0) return;
        AppLogger.Warning("DataHub snapshot was TRUNCATED at " + limit.ToString(CultureInfo.InvariantCulture)
            + " rows; this site's remaining waybills are absent locally until they change again. "
            + "Raise the server's snapshot cap or shorten change retention.");
    }

    /// <summary>
    /// Projects a server ProjectionBody onto the snake_case row shape the local fs_waybills merge
    /// expects. The ingest payload is the desktop's own row, so it is the base; the reducer's
    /// typed fields then win over whatever that payload happened to carry.
    /// </summary>
    internal static JObject ToWaybillRow(JObject body)
    {
        if (body == null) return null;
        var waybillNo = FirstString(body, "waybillNo", "waybill_no");
        if (string.IsNullOrWhiteSpace(waybillNo)) return null;

        var basePayload = body["payload"] as JObject
            ?? body["activityPayload"] as JObject
            ?? body["statePayload"] as JObject
            ?? body["inventoryPayload"] as JObject;
        var row = basePayload?.DeepClone() as JObject ?? new JObject();

        row["waybill_no"] = waybillNo.Trim().ToUpperInvariant();

        var status = FirstString(body, "stateStatus", "lastActivityStatus");
        var action = FirstString(body, "lastActivityName", "stateName");
        // The local columns hold JMS-local naive time and the merge compares them with SQLite's
        // datetime(); storing the server's offset form here would shift every comparison by 7h.
        var actionTime = ToJmsLocalTimestamp(FirstString(body, "lastActivityAt", "stateEventAt"));

        Overwrite(row, "current_state", FirstString(body, "stateName"));
        Overwrite(row, "current_status", status);
        Overwrite(row, "last_action", action);
        Overwrite(row, "last_action_time", actionTime);
        Overwrite(row, "trang_thai_hien_tai", status);
        Overwrite(row, "thao_tac_cuoi", action);
        Overwrite(row, "thoi_gian_thao_tac", actionTime);
        // updated_at drives the newest-wins half of the merge, so it must carry the server's
        // projection timestamp rather than "now" on this machine.
        Overwrite(row, "updated_at", ToIsoUtc(FirstString(body, "updatedAt")));

        // Inventory membership is deliberately not re-derived from the reducer's inventory
        // triplet — it has its own vocabulary — so it keeps whatever the pushing leader sent.
        return row;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    /// <summary>
    /// Snapshot-reader twin of <see cref="ToWaybillRow"/>. Internal, like its twin, so a test can
    /// pin the two to the same fallback order — the mismatch between them was invisible in both.
    /// </summary>
    internal static WaybillDbModel ToWaybill(JObject projection)
    {
        if (projection == null) return null;
        var payload = projection["payload"] as JObject
            ?? projection["activityPayload"] as JObject
            ?? projection["statePayload"] as JObject
            ?? new JObject();
        var row = payload.ToObject<WaybillDbModel>() ?? new WaybillDbModel();
        row.WaybillNo ??= projection.Value<string>("waybillNo");

        // Same projection, same fallback order as ToWaybillRow -- deliberately, because both
        // read the SAME server rows and write the SAME local columns. This method used to
        // prefer stateName/stateEventAt while ToWaybillRow preferred lastActivityName/
        // lastActivityAt, so a waybill's "last action" depended on whether it arrived through
        // the snapshot reader or the change feed, and the two disagreed for any waybill whose
        // latest activity had not yet advanced its state.
        row.TrangThaiHienTai ??= FirstString(projection, "stateStatus", "lastActivityStatus");
        row.ThaoTacCuoi ??= FirstString(projection, "lastActivityName", "stateName");
        // And JMS-local naive, not the server's offset form, for the reason given in
        // ToWaybillRow: the merge compares this column with SQLite's datetime(), so an offset
        // timestamp here shifts every comparison by 7 hours.
        row.ThoiGianThaoTac ??= ToJmsLocalTimestamp(FirstString(projection, "lastActivityAt", "stateEventAt"));
        return row;
    }

    private static JObject ToObservation(WaybillDbModel row) => new()
    {
        ["waybillNo"] = row?.WaybillNo ?? string.Empty,
        ["scanTime"] = row?.NextTrackAt == default ? string.Empty : row.NextTrackAt.ToUniversalTime().ToString("O"),
        ["scanTypeName"] = row?.ThaoTacCuoi ?? "client-update",
        ["status"] = row?.TrangThaiHienTai ?? string.Empty,
        ["payload"] = row == null ? new JObject() : JObject.FromObject(row)
    };

    // ── Helpers ───────────────────────────────────────────────────────────

    private static JObject ToJObject(object row) =>
        row as JObject ?? (row == null ? null : JObject.FromObject(row));

    private static string FirstString(JObject row, params string[] names)
    {
        foreach (var name in names)
        {
            var token = row[name];
            if (token == null || token.Type == JTokenType.Null) continue;
            var value = token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static void AddIfPresent(JObject item, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) item[name] = value.Trim();
    }

    /// <summary>Writes a value only when we actually have one; a blank must not erase the payload's.</summary>
    private static void Overwrite(JObject row, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) row[name] = value.Trim();
    }

    private static string ToIsoUtc(string raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            : null;

    /// <summary>Renders an instant the way JMS wrote it: naive local time in Asia/Ho_Chi_Minh.</summary>
    private static string ToJmsLocalTimestamp(string raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value.ToOffset(JmsUtcOffset).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : null;

    private static string CurrentDeviceToken
    {
        get { lock (ConfigLock) return _deviceToken; }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value.Substring(0, max) + "…");

    /// <summary>
    /// The site lease and every ingest path are addressed by the site's GUID. The old code
    /// fell back to parsing the site *code* (e.g. "214A02") as a Guid, which can never
    /// succeed — it only turned "DataHub is not configured" into a silent no-op.
    /// </summary>
    private static bool TryGetSiteId(string siteCode, out Guid siteId)
    {
        string configured;
        lock (ConfigLock)
        {
            if (Guid.TryParse(_siteId, out siteId)) return true;
            configured = _siteId;
        }
        siteId = Guid.Empty;

        // Two different situations that used to log identically. Nothing configured is the
        // normal state on a BASE-tier machine, and this method runs several times per sync
        // cycle, so it stays at Debug — promoting it would fill those logs with a non-event.
        if (string.IsNullOrWhiteSpace(configured))
        {
            AppLogger.Debug("DataHub call skipped: no site GUID configured (site code=" +
                (string.IsNullOrWhiteSpace(siteCode) ? "<none>" : siteCode) + ").");
            return false;
        }

        // A value that is present but not a GUID is config drift: enrollment succeeded and
        // then something wrote the wrong field, so every DataHub call silently no-ops on a
        // machine whose licence says it should be syncing. Latched for the same reason the
        // Debug line is not — the value cannot change without a Configure call, which resets
        // the latch, so one Error per bad configuration is exactly one report of one fault.
        if (System.Threading.Interlocked.Exchange(ref _siteIdMalformedLogged, 1) == 0)
        {
            AppLogger.Error("DataHub site id is configured but is not a GUID: \"" + MaskConfigValue(configured)
                + "\" (length=" + configured.Length.ToString(CultureInfo.InvariantCulture)
                + ", site code=" + (string.IsNullOrWhiteSpace(siteCode) ? "<none>" : siteCode)
                + "). Every DataHub call is being skipped until this is corrected — re-enroll the device.");
        }
        return false;
    }

    /// <summary>
    /// first2…last2 of a configuration value. Enough for a technician to recognise which
    /// value was written into the field — the point of the log — without reproducing a
    /// site identifier into a file that gets attached to support tickets.
    /// </summary>
    private static string MaskConfigValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return "<empty>";
        return value.Length <= 4
            ? new string('*', value.Length)
            : value.Substring(0, 2) + "…" + value.Substring(value.Length - 2);
    }

    private static bool TryGetConfig(out string baseUrl, out string token)
    {
        lock (ConfigLock)
        {
            baseUrl = _baseUrl;
            token = _deviceToken;
        }
        return !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(token);
    }

    private static string LoadOrCreateMachineId()
    {
        var fallback = Environment.MachineName + "_" + Guid.NewGuid().ToString("N");
        try
        {
            var path = Path.Combine(AppPaths.UserDataDir, "machine-id.txt");
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }
            Directory.CreateDirectory(AppPaths.UserDataDir);
            File.WriteAllText(path, fallback);
            return fallback;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub machine id persist failed, using a per-process id: " + ex.Message);
            return fallback;
        }
    }

    private static string ResolveSiteCode() => new SiteContextProvider().Current?.MiddleCode ?? string.Empty;

    private static string FirstNonEmpty(params string[] values) => values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
