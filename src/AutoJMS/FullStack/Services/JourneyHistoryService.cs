using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    /// <summary>
    /// Tracks the shipping journey ("Hành trình vận chuyển") from the JMS
    /// podTracking API and stores it in the standalone journey_history database.
    /// The request clones the header pattern used by WaybillTrackingService:
    /// it posts via <see cref="JmsApiClient"/> with routeName "trackingExpress",
    /// which attaches the current authToken and the browser-equivalent headers.
    /// </summary>
    public sealed class JourneyHistoryService
    {
        private const string TrackingRouteName = "trackingExpress";
        private const string KeywordListPath = "operatingplatform/podTracking/inner/query/keywordList";

        private readonly JourneyHistoryRepository _repository;

        public JourneyHistoryService()
            : this(new JourneyHistoryRepository())
        {
        }

        public JourneyHistoryService(JourneyHistoryRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        /// <summary>
        /// Fetch the journey for a waybill from the API, persist it to
        /// journey_history.db, and return the parsed events (newest first).
        /// </summary>
        public async Task<List<JourneyHistoryEvent>> FetchAndStoreAsync(
            string waybillNo,
            CancellationToken cancellationToken = default)
        {
            waybillNo = (waybillNo ?? string.Empty).Trim();
            if (waybillNo.Length == 0)
                return new List<JourneyHistoryEvent>();

            try
            {
                var events = await FetchFromApiAsync(waybillNo, cancellationToken).ConfigureAwait(false);
                if (events.Count > 0)
                    await _repository.SaveAsync(waybillNo, events, cancellationToken).ConfigureAwait(false);

                AppLogger.Info($"[JourneyHistory] stored {events.Count} event(s) for {waybillNo}");
                return events;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[JourneyHistory] FetchAndStore failed for {waybillNo}", ex);
                return new List<JourneyHistoryEvent>();
            }
        }

        /// <summary>Read the stored journey for a waybill from journey_history.db.</summary>
        public Task<List<JourneyHistoryEvent>> GetStoredAsync(
            string waybillNo,
            CancellationToken cancellationToken = default) =>
            _repository.GetByWaybillAsync(waybillNo, cancellationToken);

        /// <summary>
        /// Persist an already-parsed journey (from the existing FetchFreshJourneyAsync
        /// path) for a single waybill, replacing the previous snapshot. Used by the
        /// on-click "Hành trình vận chuyển" refresh.
        /// </summary>
        public async Task StoreRowsAsync(
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            CancellationToken cancellationToken = default)
        {
            waybillNo = (waybillNo ?? string.Empty).Trim();
            if (waybillNo.Length == 0 || rows == null || rows.Count == 0)
                return;

            var fetchedAt = DateTime.Now;
            var events = new List<JourneyHistoryEvent>(rows.Count);
            foreach (var row in rows)
            {
                if (row == null) continue;
                events.Add(new JourneyHistoryEvent
                {
                    WaybillNo = waybillNo,
                    Stt = row.Stt,
                    ScanTime = row.ActionTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                    UploadTime = row.UploadedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                    ScanTypeName = row.ActionType ?? string.Empty,
                    Description = row.Description ?? string.Empty,
                    Source = row.ScanSource ?? string.Empty,
                    Weight = row.Weight ?? string.Empty,
                    RawJson = row.RawJson ?? string.Empty,
                    FetchedAt = fetchedAt
                });
            }

            try
            {
                await _repository.SaveAsync(waybillNo, events, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[JourneyHistory] StoreRows failed for {waybillNo}", ex);
            }
        }

        /// <summary>
        /// Persist tracking events collected by the enrichment (bulk) flow into
        /// journey_history.db. Each event's RawJson is the original API detail, so
        /// all 7 columns are extracted from it. Additive / de-duplicated.
        /// </summary>
        public async Task StoreTrackingEventsAsync(
            IEnumerable<TrackingEvent> trackingEvents,
            CancellationToken cancellationToken = default)
        {
            if (trackingEvents == null) return;

            var fetchedAt = DateTime.Now;
            var byWaybill = new Dictionary<string, List<JourneyHistoryEvent>>(StringComparer.OrdinalIgnoreCase);
            foreach (var te in trackingEvents)
            {
                if (te == null || string.IsNullOrWhiteSpace(te.WaybillNo) || string.IsNullOrWhiteSpace(te.RawJson))
                    continue;

                JourneyHistoryEvent ev;
                try
                {
                    using var doc = JsonDocument.Parse(te.RawJson);
                    var d = doc.RootElement;
                    var scanType = GetString(d, "scanTypeName");
                    var content = GetString(d, "waybillTrackingContent");
                    ev = new JourneyHistoryEvent
                    {
                        WaybillNo = te.WaybillNo.Trim(),
                        ScanTime = GetString(d, "scanTime"),
                        UploadTime = GetString(d, "uploadTime"),
                        ScanTypeName = string.IsNullOrEmpty(scanType) ? (te.Action ?? string.Empty) : scanType,
                        Description = string.IsNullOrEmpty(content) ? (te.Status ?? string.Empty) : content,
                        Source = GetFirstString(d, "scanSource", "source", "remark6"),
                        Weight = GetString(d, "weight"),
                        RawJson = te.RawJson,
                        FetchedAt = fetchedAt
                    };
                }
                catch
                {
                    continue;
                }

                if (!byWaybill.TryGetValue(ev.WaybillNo, out var bucket))
                {
                    bucket = new List<JourneyHistoryEvent>();
                    byWaybill[ev.WaybillNo] = bucket;
                }
                bucket.Add(ev);
            }

            if (byWaybill.Count == 0) return;

            var all = new List<JourneyHistoryEvent>();
            foreach (var kv in byWaybill)
            {
                var ordered = kv.Value
                    .OrderByDescending(r => DateTime.TryParse(r.ScanTime, out var t) ? t : DateTime.MinValue)
                    .ToList();
                for (var i = 0; i < ordered.Count; i++)
                    ordered[i].Stt = i + 1;
                all.AddRange(ordered);
            }

            try
            {
                await _repository.InsertManyAsync(all, cancellationToken).ConfigureAwait(false);
                AppLogger.Info($"[JourneyHistory] bulk stored {all.Count} event(s) across {byWaybill.Count} waybill(s)");
            }
            catch (Exception ex)
            {
                AppLogger.Error("[JourneyHistory] bulk store failed", ex);
            }
        }

        private static async Task<List<JourneyHistoryEvent>> FetchFromApiAsync(
            string waybillNo,
            CancellationToken cancellationToken)
        {
            var rows = new List<JourneyHistoryEvent>();

            var url = AppConfig.Current.BuildJmsApiUrl(KeywordListPath);
            var payload = new { keywordList = new[] { waybillNo }, trackingTypeEnum = "WAYBILL", countryId = "1" };
            var body = JsonSerializer.Serialize(payload);

            using var response = await JmsApiClient
                .PostJsonAsync(url, body, routeName: TrackingRouteName, ct: cancellationToken)
                .ConfigureAwait(false);

            if (response == null)
                return rows;
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warning($"[JourneyHistory] tracking HTTP {(int)response.StatusCode} for {waybillNo}");
                return rows;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return rows;

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return rows;

            // Prefer the data entry whose keyword matches the waybill; otherwise
            // fall back to the first entry that actually has a details[] array.
            JsonElement chosen = default;
            bool haveAny = false;
            foreach (var entry in data.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (!entry.TryGetProperty("details", out var detailsProbe) || detailsProbe.ValueKind != JsonValueKind.Array) continue;

                if (!haveAny)
                {
                    chosen = entry;
                    haveAny = true;
                }
                if (string.Equals(GetString(entry, "keyword"), waybillNo, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = entry;
                    break;
                }
            }

            if (!haveAny || !chosen.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
                return rows;

            var fetchedAt = DateTime.Now;
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object) continue;
                rows.Add(new JourneyHistoryEvent
                {
                    WaybillNo = waybillNo,
                    ScanTime = GetString(detail, "scanTime"),
                    UploadTime = GetString(detail, "uploadTime"),
                    ScanTypeName = GetString(detail, "scanTypeName"),
                    Description = GetString(detail, "waybillTrackingContent"),
                    Source = GetFirstString(detail, "scanSource", "source", "remark6"),
                    Weight = GetString(detail, "weight"),
                    RawJson = detail.GetRawText(),
                    FetchedAt = fetchedAt
                });
            }

            // Newest first, then assign STT (1 = newest).
            rows = rows
                .OrderByDescending(r => DateTime.TryParse(r.ScanTime, out var t) ? t : DateTime.MinValue)
                .ToList();
            for (var i = 0; i < rows.Count; i++)
                rows[i].Stt = i + 1;

            return rows;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String) return prop.GetString() ?? string.Empty;
                if (prop.ValueKind == JsonValueKind.Number) return prop.GetRawText();
            }
            return string.Empty;
        }

        private static string GetFirstString(JsonElement element, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var value = GetString(element, name);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return string.Empty;
        }
    }
}
