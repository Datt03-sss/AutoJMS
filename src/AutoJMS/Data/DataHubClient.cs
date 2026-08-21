using AutoJMS.Diagnostics;
using AutoJMS.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AutoJMS;

/// <summary>
/// API-only data adapter. The desktop never connects to PostgreSQL directly;
/// all shared state is sent to the DataHub API hosted on the VPS.
/// </summary>
public static class DataHubClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly object ConfigLock = new();
    private static string _baseUrl = string.Empty;
    private static string _deviceToken = string.Empty;
    private static string _siteId = string.Empty;

    public static string MachineId { get; } = Environment.MachineName + "_" + Guid.NewGuid().ToString("N");

    public static bool HasCredentials
    {
        get { lock (ConfigLock) return !string.IsNullOrWhiteSpace(_baseUrl) && !string.IsNullOrWhiteSpace(_deviceToken); }
    }

    public static void Configure(string baseUrl = null, string deviceToken = null, string siteId = null)
    {
        lock (ConfigLock)
        {
            _baseUrl = FirstNonEmpty(baseUrl, Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_API_BASE_URL"))?.TrimEnd('/') ?? string.Empty;
            _deviceToken = FirstNonEmpty(deviceToken, Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_DEVICE_TOKEN")) ?? string.Empty;
            _siteId = FirstNonEmpty(siteId, Environment.GetEnvironmentVariable("AUTOJMS_DATAHUB_SITE_ID")) ?? string.Empty;
        }
        AppLogger.Info("DataHub API configured baseUrl=" + _baseUrl + ", token=" + (string.IsNullOrWhiteSpace(_deviceToken) ? "<missing>" : TokenRedactor.MaskToken(_deviceToken)));
    }

    public static Task InitializeAsync() => Task.CompletedTask;

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

    public static Task<bool> TryAcquireSiteLeaseAsync(string siteCode, int leaseSeconds = 120) =>
        SendLeaseAsync(siteCode, "acquire", leaseSeconds);

    public static Task<bool> RefreshSiteLeaseAsync(string siteCode, int leaseSeconds = 120) =>
        SendLeaseAsync(siteCode, "renew", leaseSeconds);

    public static Task<bool> ReleaseSiteLeaseAsync(string siteCode) =>
        SendLeaseAsync(siteCode, "release", 0);

    public static Task<int> MergeWaybillRowsV2Async(string siteCode, IReadOnlyList<object> rows) =>
        SendObservationBatchAsync(siteCode, rows?.Select(row => row as JObject ?? JObject.FromObject(row)).ToList());

    public static Task<int> PushOrderNotesAsync(string siteCode, IReadOnlyList<object> rows) =>
        SendObservationBatchAsync(siteCode, rows?.Select(row => row as JObject ?? JObject.FromObject(row)).ToList());

    public static Task<int> MergeOrderChecksAsync(string siteCode, IReadOnlyList<object> rows) =>
        SendObservationBatchAsync(siteCode, rows?.Select(row => row as JObject ?? JObject.FromObject(row)).ToList());

    public static Task<int> MergeDispatchTasksAsync(string siteCode, IReadOnlyList<object> rows) =>
        SendObservationBatchAsync(siteCode, rows?.Select(row => row as JObject ?? JObject.FromObject(row)).ToList());

    public static Task<int> AppendWaybillEventsAsync(string siteCode, IReadOnlyList<object> events) =>
        SendObservationBatchAsync(siteCode, events?.Select(row => row as JObject ?? JObject.FromObject(row)).ToList());

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

    private static async Task<bool> SendLeaseAsync(string siteCode, string action, int leaseSeconds)
    {
        if (!TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token)) return false;
        var endpoint = baseUrl + "/api/v1/sites/" + siteId + "/lease/" + action;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonConvert.SerializeObject(new { leaseSeconds }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub lease " + action + " failed: " + ex.Message);
            return false;
        }
    }

    private static async Task<int> SendObservationBatchAsync(string siteCode, List<JObject> items)
    {
        if (items == null || items.Count == 0 || !TryGetSiteId(siteCode, out var siteId) || !TryGetConfig(out var baseUrl, out var token)) return 0;
        var endpoint = baseUrl + "/api/v1/sites/" + siteId + "/jms/observations";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = new StringContent(JsonConvert.SerializeObject(new { items }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? items.Count : 0;
        }
        catch (Exception ex)
        {
            AppLogger.Warning("DataHub observation ingest failed: " + ex.Message);
            return 0;
        }
    }

    private static async Task<List<WaybillDbModel>> ReadSnapshotAsync(int limit)
    {
        if (!TryGetSiteId(ResolveSiteCode(), out var siteId) || !TryGetConfig(out var baseUrl, out var token))
            return new List<WaybillDbModel>();

        using var request = CreateAuthorizedRequest(HttpMethod.Get,
            $"{baseUrl}/api/v1/sites/{siteId}/projections/snapshot?limit={Math.Clamp(limit, 1, 5000)}", token);
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
            $"{baseUrl}/api/v1/sites/{siteId}/changes?after={Math.Max(0, after)}&limit={Math.Clamp(limit, 1, 5000)}", token);
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

    private static bool TryGetSiteId(string siteCode, out Guid siteId)
    {
        lock (ConfigLock)
        {
            if (Guid.TryParse(_siteId, out siteId)) return true;
        }
        return Guid.TryParse(siteCode, out siteId);
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

    private static string ResolveSiteCode() => new SiteContextProvider().Current?.MiddleCode ?? string.Empty;

    private static string FirstNonEmpty(params string[] values) => values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
