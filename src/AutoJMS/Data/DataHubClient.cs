using AutoJMS.Diagnostics;
using AutoJMS.Data;
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
/// API-only data adapter. The desktop never connects to PostgreSQL directly;
/// all shared state is sent to the DataHub API hosted on the VPS.
///
/// Wire contract (AutoJMS.DataHub.Api):
///   * POST /lease/acquire  — no body; the response carries the fencing term.
///   * POST /lease/renew    — body { leaderTerm }; the term does NOT change.
///   * POST /lease/release  — body { leaderTerm }; the server then bumps the term.
///   * POST /jms/ingest     — bulk; requires the X-Leader-Term header (409 without it).
///   * POST /jms/observations — interactive; unfenced.
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

    private static int _auxiliaryWarningLogged;

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

    public static Task<List<JObject>> PullWaybillDeltaAsync(string siteCode, DateTime sinceUtc, int limit = 1000) =>
        Task.FromResult(new List<JObject>());

    public static Task<List<JObject>> PullOrderNotesAsync(string siteCode, DateTime sinceUtc, int limit = 1000) =>
        Task.FromResult(new List<JObject>());

    public static Task<List<JObject>> PullOrderChecksAsync(string siteCode, DateTime sinceUtc, int limit = 1000) =>
        Task.FromResult(new List<JObject>());

    public static Task<List<JObject>> PullDispatchTasksAsync(string siteCode, DateTime sinceUtc, int limit = 1000) =>
        Task.FromResult(new List<JObject>());

    public static Task<List<JObject>> PullEventsDeltaAsync(string siteCode, long sinceSeq, int limit = 2000) =>
        ReadChangesAsync(siteCode, sinceSeq, limit);

    public static Task<bool> SubscribeSiteChangesAsync(string siteCode, Action onAnyChange) => Task.FromResult(false);

    public static void UnsubscribeSiteChanges() { }

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
        var (state, reachable) = await SendLeaseRequestAsync(baseUrl, siteId, token, "acquire", null).ConfigureAwait(false);
        if (state == null)
        {
            lock (LeaseLock) _leaderTerm = 0;
            return reachable ? DataHubLeaseOutcome.Denied : DataHubLeaseOutcome.Unreachable;
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
    /// Returns the parsed lease state, plus whether the server answered at all. A null state
    /// with Reachable=true is a real refusal (409 fenced / lease held); with Reachable=false
    /// the network or the VPS is the problem and nothing about leadership is known.
    /// </summary>
    private static async Task<(JObject State, bool Reachable)> SendLeaseRequestAsync(string baseUrl, Guid siteId, string token, string action, long? leaderTerm)
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
                var text = "DataHub lease " + action + " -> " + (int)response.StatusCode + " " + Truncate(body, 300);
                if (response.StatusCode == HttpStatusCode.Conflict) AppLogger.Info(text);
                else AppLogger.Warning(text);
                return (null, true);
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
        var payload = JsonConvert.SerializeObject(new { items });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Content-derived, not random: a retry of the same batch must reuse the same key or
        // the server's idempotency check cannot dedupe it.
        request.Headers.Add("Idempotency-Key", ComputeIdempotencyKey(payload));
        if (bulk) request.Headers.Add("X-Leader-Term", term.ToString(CultureInfo.InvariantCulture));
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return items.Count;

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AppLogger.Warning("DataHub ingest -> " + (int)response.StatusCode + " " + Truncate(body, 400));
            if (bulk && response.StatusCode == HttpStatusCode.Conflict)
            {
                // The fence rejected us: stop claiming leadership until a fresh acquire.
                lock (LeaseLock) _leaderTerm = 0;
            }
            return 0;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub observation ingest failed: " + ex.Message);
            return 0;
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
            $"{baseUrl}/api/v1/sites/{siteId:D}/projections/snapshot?limit={Math.Clamp(limit, 1, 5000)}", token);
        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new List<WaybillDbModel>();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
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

    private static async Task<List<JObject>> ReadChangesAsync(string siteCode, long after, int limit)
    {
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token))
            return new List<JObject>();

        using var request = CreateAuthorizedRequest(HttpMethod.Get,
            $"{baseUrl}/api/v1/sites/{siteId:D}/changes?after={Math.Max(0, after)}&limit={Math.Clamp(limit, 1, 5000)}", token);
        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new List<JObject>();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return json["items"] is JArray items
                ? items.OfType<JObject>().Select(item => item["body"] as JObject ?? item).ToList()
                : new List<JObject>();
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub change read failed: " + ex.Message);
            return new List<JObject>();
        }
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static WaybillDbModel ToWaybill(JObject projection)
    {
        if (projection == null) return null;
        var payload = projection["payload"] as JObject
            ?? projection["activityPayload"] as JObject
            ?? projection["statePayload"] as JObject
            ?? new JObject();
        var row = payload.ToObject<WaybillDbModel>() ?? new WaybillDbModel();
        row.WaybillNo ??= projection.Value<string>("waybillNo");
        row.TrangThaiHienTai ??= projection.Value<string>("stateStatus");
        row.ThaoTacCuoi ??= projection.Value<string>("stateName") ?? projection.Value<string>("lastActivityName");
        row.ThoiGianThaoTac ??= projection.Value<string>("stateEventAt") ?? projection.Value<string>("lastActivityAt");
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

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value.Substring(0, max) + "…");

    /// <summary>
    /// The site lease and every ingest path are addressed by the site's GUID. The old code
    /// fell back to parsing the site *code* (e.g. "214A02") as a Guid, which can never
    /// succeed — it only turned "DataHub is not configured" into a silent no-op.
    /// </summary>
    private static bool TryGetSiteId(string siteCode, out Guid siteId)
    {
        lock (ConfigLock)
        {
            if (Guid.TryParse(_siteId, out siteId)) return true;
        }
        siteId = Guid.Empty;
        AppLogger.Debug("DataHub call skipped: no site GUID configured (site code=" +
            (string.IsNullOrWhiteSpace(siteCode) ? "<none>" : siteCode) + ").");
        return false;
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
