using AutoJMS.FullStack.LocalDb;
using AutoJMS.FullStack.Models;
using AutoJMS.FullStack.Repositories;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackJourneyService : IFullStackJourneyService
    {
        private readonly IFullStackTrackingJourneyService _trackingJourneyService;
        private readonly IWaybillJourneyParser _parser;
        private readonly IWaybillJourneyDetailsRepository _detailsRepository;
        private readonly IFullStackJourneyCleanupService _cleanupService;
        private readonly FullStackDbConnectionFactory _operationDbConnectionFactory;
        private readonly FullStackDbInitializer _operationDbInitializer;

        public FullStackJourneyService()
            : this(
                new FullStackTrackingJourneyService(),
                new WaybillJourneyJsonParser(),
                new WaybillJourneyDetailsRepository(),
                new FullStackJourneyCleanupService(),
                new FullStackDbConnectionFactory())
        {
        }

        public FullStackJourneyService(
            IFullStackTrackingJourneyService trackingJourneyService,
            IWaybillJourneyParser parser,
            IWaybillJourneyDetailsRepository detailsRepository,
            IFullStackJourneyCleanupService cleanupService,
            FullStackDbConnectionFactory operationDbConnectionFactory)
        {
            _trackingJourneyService = trackingJourneyService ?? throw new ArgumentNullException(nameof(trackingJourneyService));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _detailsRepository = detailsRepository ?? throw new ArgumentNullException(nameof(detailsRepository));
            _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
            _operationDbConnectionFactory = operationDbConnectionFactory ?? throw new ArgumentNullException(nameof(operationDbConnectionFactory));
            _operationDbInitializer = new FullStackDbInitializer(_operationDbConnectionFactory);
        }

        public async Task<WaybillJourneyViewModel> GetJourneyAsync(string waybillNo, CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new WaybillJourneyViewModel();

            var local = await GetLocalJourneyAsync(waybillNo, cancellationToken).ConfigureAwait(false);
            var token = ResolveCurrentAuthToken();
            if (string.IsNullOrWhiteSpace(token))
                return local;

            var refreshed = await RefreshJourneyFromJmsAsync(waybillNo, token, cancellationToken).ConfigureAwait(false);
            return refreshed.Success && refreshed.ViewModel.Rows.Count > 0 ? refreshed.ViewModel : local;
        }

        public Task<WaybillJourneyViewModel> GetLocalJourneyAsync(string waybillNo, CancellationToken cancellationToken)
        {
            return _detailsRepository.GetLocalJourneyAsync(NormalizeWaybill(waybillNo), cancellationToken);
        }

        public Task<bool> HasLocalJourneyAsync(string waybillNo, CancellationToken cancellationToken)
        {
            return _detailsRepository.HasLocalJourneyAsync(NormalizeWaybill(waybillNo), cancellationToken);
        }

        public Task<WaybillJourneyRawCache> GetLatestRawJsonAsync(string waybillNo, CancellationToken cancellationToken)
        {
            return _detailsRepository.GetLatestRawJsonAsync(NormalizeWaybill(waybillNo), cancellationToken);
        }

        public async Task<WaybillJourneyViewModel> GetRemoteJourneyAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken)
        {
            var result = await RefreshJourneyFromJmsAsync(waybillNo, authToken, cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                throw new FullStackPodTrackingException(result.Message, result.AuthExpired);
            return result.ViewModel;
        }

        public async Task<WaybillJourneyRefreshResult> FetchFreshJourneyAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new WaybillJourneyRefreshResult { Message = "Missing waybill." };

            try
            {
                var tracking = await _trackingJourneyService
                    .FetchJourneyAsync(waybillNo, authToken, cancellationToken)
                    .ConfigureAwait(false);

                var fetchedAt = tracking.FetchedAt == default ? DateTime.Now : tracking.FetchedAt;
                var parsed = tracking.ViewModel ?? new WaybillJourneyViewModel { WaybillNo = waybillNo };
                parsed.FetchedAt = fetchedAt;
                parsed.Source = tracking.Success ? "Fresh Tracking API" : "Error";

                LogJourneyResult(
                    tracking.Success ? "Fresh API Parsed" : "Error Parsed",
                    waybillNo,
                    parsed,
                    tracking.Message,
                    tracking.ApiEndpoint,
                    tracking.HttpStatusCode,
                    tracking.RawJson);

                if (!tracking.Success || !tracking.IsWaybillMatched)
                {
                    return new WaybillJourneyRefreshResult
                    {
                        Success = false,
                        AuthExpired = tracking.AuthExpired,
                        ResponseMismatch = !tracking.IsWaybillMatched,
                        Message = string.IsNullOrWhiteSpace(tracking.Message) ? "Không tải được từ Tracking API" : tracking.Message,
                        RawJson = tracking.RawJson ?? string.Empty,
                        FetchedAt = fetchedAt,
                        Source = "Error",
                        ApiEndpoint = tracking.ApiEndpoint,
                        HttpStatusCode = tracking.HttpStatusCode,
                        ViewModel = parsed
                    };
                }

                return new WaybillJourneyRefreshResult
                {
                    Success = true,
                    Message = $"Đã parse {parsed.Rows.Count:N0} sự kiện",
                    ViewModel = parsed,
                    RawJson = tracking.RawJson ?? string.Empty,
                    FetchedAt = fetchedAt,
                    Source = "Fresh Tracking API",
                    ApiEndpoint = tracking.ApiEndpoint,
                    HttpStatusCode = tracking.HttpStatusCode
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[FullStackJourney] fetch fresh failed waybill={waybillNo}: {ex.Message}");
                return new WaybillJourneyRefreshResult
                {
                    Success = false,
                    Message = "Không lấy được hành trình vận chuyển cho mã " + waybillNo,
                    Source = "Error",
                    ViewModel = new WaybillJourneyViewModel { WaybillNo = waybillNo }
                };
            }
        }

        public async Task<WaybillJourneyRefreshResult> RefreshJourneyFromJmsAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return new WaybillJourneyRefreshResult { Message = "Missing waybill." };

            try
            {
                var tracking = await _trackingJourneyService
                    .FetchJourneyAsync(waybillNo, authToken, cancellationToken)
                    .ConfigureAwait(false);

                var fetchedAt = tracking.FetchedAt == default ? DateTime.Now : tracking.FetchedAt;
                var expiresAt = EndOfToday(fetchedAt);
                var rawJson = tracking.RawJson ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(rawJson))
                {
                    await _detailsRepository.SaveRawJsonAsync(
                        waybillNo,
                        rawJson,
                        tracking.HttpStatusCode,
                        string.Empty,
                        tracking.EventCount,
                        fetchedAt,
                        expiresAt,
                        tracking.Success ? string.Empty : tracking.Message,
                        cancellationToken).ConfigureAwait(false);
                }

                if (!tracking.Success || !tracking.IsWaybillMatched)
                {
                    LogJourneyResult("Error", waybillNo, tracking.ViewModel, tracking.Message, tracking.ApiEndpoint, tracking.HttpStatusCode, rawJson);
                    return new WaybillJourneyRefreshResult
                    {
                        Success = false,
                        AuthExpired = tracking.AuthExpired,
                        ResponseMismatch = !tracking.IsWaybillMatched,
                        Message = string.IsNullOrWhiteSpace(tracking.Message) ? "Không tải được từ Tracking API" : tracking.Message,
                        RawJson = rawJson,
                        FetchedAt = fetchedAt,
                        Source = "Error",
                        ApiEndpoint = tracking.ApiEndpoint,
                        HttpStatusCode = tracking.HttpStatusCode,
                        ViewModel = tracking.ViewModel ?? new WaybillJourneyViewModel { WaybillNo = waybillNo }
                    };
                }

                var parsed = tracking.ViewModel ?? new WaybillJourneyViewModel { WaybillNo = waybillNo };

                await _detailsRepository.SaveJourneySnapshotAsync(
                    waybillNo,
                    rawJson,
                    tracking.HttpStatusCode,
                    tracking.Message,
                    parsed.Rows,
                    fetchedAt,
                    expiresAt,
                    string.Empty,
                    cancellationToken).ConfigureAwait(false);

                await MarkOperationWaybillEnrichedAsync(waybillNo, cancellationToken).ConfigureAwait(false);
                await _cleanupService.CleanupExpiredAsync(cancellationToken).ConfigureAwait(false);

                parsed.FetchedAt = fetchedAt;
                parsed.Source = "Fresh Tracking API";
                LogJourneyResult("Fresh API", waybillNo, parsed, string.Empty, tracking.ApiEndpoint, tracking.HttpStatusCode, rawJson);

                return new WaybillJourneyRefreshResult
                {
                    Success = true,
                    Message = $"Đã cập nhật {parsed.Rows.Count:N0} sự kiện",
                    ViewModel = parsed,
                    RawJson = rawJson,
                    FetchedAt = fetchedAt,
                    Source = "Fresh Tracking API",
                    ApiEndpoint = tracking.ApiEndpoint,
                    HttpStatusCode = tracking.HttpStatusCode
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[FullStackJourney] refresh failed waybill={waybillNo}: {ex.Message}");
                LogJourneyResult("Error", waybillNo, new WaybillJourneyViewModel { WaybillNo = waybillNo }, ex.Message, string.Empty, null, string.Empty);
                return new WaybillJourneyRefreshResult
                {
                    Success = false,
                    Message = "Không lấy được hành trình vận chuyển cho mã " + waybillNo,
                    Source = "Error",
                    ViewModel = new WaybillJourneyViewModel { WaybillNo = waybillNo }
                };
            }
        }

        public async Task SaveJourneyAsync(
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            string rawJson,
            CancellationToken cancellationToken)
        {
            waybillNo = NormalizeWaybill(waybillNo);
            if (string.IsNullOrWhiteSpace(waybillNo))
                return;

            var fetchedAt = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                var metadata = _parser.ReadMetadata(rawJson);
                await _detailsRepository.SaveJourneySnapshotAsync(
                    waybillNo,
                    rawJson,
                    metadata.ResponseCode,
                    metadata.ResponseMessage,
                    rows,
                    fetchedAt,
                    EndOfToday(fetchedAt),
                    string.Empty,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _detailsRepository.ReplaceJourneyEventsAsync(waybillNo, rows, fetchedAt, cancellationToken).ConfigureAwait(false);
            }

            await MarkOperationWaybillEnrichedAsync(waybillNo, cancellationToken).ConfigureAwait(false);
        }

        public Task CleanupExpiredAsync(CancellationToken cancellationToken)
        {
            return _cleanupService.CleanupExpiredAsync(cancellationToken);
        }

        private async Task MarkOperationWaybillEnrichedAsync(string waybillNo, CancellationToken cancellationToken)
        {
            await _operationDbInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            await using var connection = await _operationDbConnectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (var ensure = connection.CreateCommand())
            {
                ensure.Transaction = transaction;
                ensure.CommandText = @"
INSERT OR IGNORE INTO fs_waybills(
    waybill_no, first_seen_at, last_seen_at, is_in_current_inventory,
    created_at, updated_at, is_active, tracking_interval_mins)
VALUES (
    $waybillNo, $now, $now, 1,
    $now, $now, 1, 30);";
                Add(ensure, "$waybillNo", waybillNo);
                Add(ensure, "$now", now.ToString("O"));
                await ensure.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE fs_waybills
SET is_enriched = 1,
    enriched_at = $enrichedAt,
    last_track_at = $lastTrackAt,
    updated_at = $updatedAt
WHERE waybill_no = $waybillNo;";
                Add(update, "$enrichedAt", now.ToString("O"));
                Add(update, "$lastTrackAt", now.ToString("O"));
                Add(update, "$updatedAt", now.ToString("O"));
                Add(update, "$waybillNo", waybillNo);
                await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static string ResolveCurrentAuthToken()
        {
            if (JmsAuthStateService.HasToken)
                return JmsAuthStateService.CurrentToken;
            return AuthStateService.Instance.IsAuthenticated ? AuthStateService.Instance.AuthToken : string.Empty;
        }

        private static DateTime EndOfToday(DateTime now)
        {
            return now.Date.AddDays(1).AddTicks(-1);
        }

        private void LogJourneyResult(
            string source,
            string waybillNo,
            WaybillJourneyViewModel viewModel,
            string error,
            string endpoint,
            int? httpStatus,
            string rawJson)
        {
            var rows = viewModel?.Rows ?? new List<WaybillJourneyRow>();
            var first = rows.Count > 0 ? rows[0] : null;
            var last = rows.Count > 0 ? rows[^1] : null;
            var detailsPath = (_detailsRepository as WaybillJourneyDetailsRepository)?.DatabasePath ?? "details.db";

            AppLogger.Info(
                "[FullStackJourney] " +
                $"source={source}; waybill={waybillNo}; endpoint={ShortEndpoint(endpoint)}; http={(httpStatus?.ToString() ?? "--")}; " +
                $"detailsDb={detailsPath}; rawSaved={(string.IsNullOrWhiteSpace(rawJson) ? "no" : "yes")}; " +
                $"events={rows.Count}; first={FormatEventForLog(first)}; last={FormatEventForLog(last)}; " +
                $"currentAtBind={waybillNo}; validation={Truncate(viewModel?.ValidationWarning, 120)}; " +
                $"error={Truncate(error, 160)}");
        }

        private static string FormatEventForLog(WaybillJourneyRow row)
        {
            if (row == null)
                return "--";

            var time = row.ActionTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "--";
            return $"{time} {Truncate(row.Description, 120)}";
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static string ShortEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return string.Empty;
            var marker = "operatingplatform/";
            var index = endpoint.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? endpoint.Substring(index) : endpoint;
        }

        private static string NormalizeWaybill(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }

        private static void Add(SqliteCommand command, string name, object value)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
