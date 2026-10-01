using AutoJMS.FullStack.Models;
using AutoJMS.UI.DesignSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    public partial class FullStackOperation
    {
        private void ShowWaybillJourneyWorkspace(string waybillNo)
        {
            _ = OpenJourneyAsync(waybillNo);
        }

        private async Task OpenJourneyAsync(string waybillNo)
        {
            if (string.IsNullOrWhiteSpace(waybillNo)) return;

            CancelCurrentJourneyLoad();
            var journeyWaybill = waybillNo.Trim().ToUpperInvariant();
            _activeJourneyWaybillNo = journeyWaybill;
            var requestCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _journeyLoadCts = requestCts;

            SetJourneyStatus("Đang tải hành trình vận chuyển từ Tracking API...");

            try
            {
                var token = await ResolveJourneyAuthTokenAsync(requestCts.Token).ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(token))
                {
                    var hasCache = await _fullStackJourneyService
                        .HasLocalJourneyAsync(journeyWaybill, requestCts.Token)
                        .ConfigureAwait(true);

                    if (!IsActiveJourneyRequest(journeyWaybill, requestCts))
                        return;

                    ShowJourneyError(
                        "Chưa có authToken, không gọi JMS. Không hiển thị dữ liệu cũ để tránh sai lệch.",
                        hasCache);
                    return;
                }

                var refresh = await _fullStackJourneyService
                    .FetchFreshJourneyAsync(journeyWaybill, token, requestCts.Token)
                    .ConfigureAwait(true);

                if (!IsActiveJourneyRequest(journeyWaybill, requestCts))
                    return;

                if (refresh.Success && refresh.ViewModel?.Rows?.Count > 0)
                {
                    BindJourneyGrid(
                        refresh.ViewModel,
                        BuildJourneyLoadedStatus(refresh.ViewModel.Rows.Count, refresh.FetchedAt ?? DateTime.Now, "Fresh"));
                    QueueJourneySnapshotSave(journeyWaybill, refresh);
                    _ = _journeyHistoryService.StoreRowsAsync(journeyWaybill, refresh.ViewModel.Rows);
                    _ = GatherAndPostOrderImagesAsync(journeyWaybill, refresh.RawJson);
                }
                else if (refresh.Success)
                {
                    SetJourneyStatus("Response có data nhưng parser không lấy được details[].");
                    QueueJourneySnapshotSave(journeyWaybill, refresh);
                }
                else
                {
                    var hasCache = await _fullStackJourneyService
                        .HasLocalJourneyAsync(journeyWaybill, requestCts.Token)
                        .ConfigureAwait(true);

                    if (!IsActiveJourneyRequest(journeyWaybill, requestCts))
                        return;

                    var message = refresh.ResponseMismatch
                        ? "Response không khớp mã vận đơn đang chọn."
                        : refresh.AuthExpired
                            ? "AuthToken không hợp lệ hoặc đã hết hạn."
                            : "Không lấy được hành trình vận chuyển từ JMS. Không hiển thị dữ liệu cũ để tránh sai lệch.";
                    ShowJourneyError(message, hasCache);
                }
            }
            catch (OperationCanceledException)
            {
                // User selected another waybill; stale responses are intentionally ignored.
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Open journey workspace failed for {journeyWaybill}: {ex.Message}");
                if (IsActiveJourneyRequest(journeyWaybill, requestCts))
                {
                    var hasCache = false;
                    try
                    {
                        hasCache = await _fullStackJourneyService
                            .HasLocalJourneyAsync(journeyWaybill, _cts.Token)
                            .ConfigureAwait(true);
                    }
                    catch
                    {
                    }

                    ShowJourneyError(
                        "Không lấy được hành trình vận chuyển. Không hiển thị dữ liệu cũ để tránh sai lệch.",
                        hasCache);
                }
            }
            finally
            {
                if (ReferenceEquals(_journeyLoadCts, requestCts))
                    _journeyLoadCts = null;
                requestCts.Dispose();
            }
        }

        private void CancelCurrentJourneyLoad()
        {
            var cts = _journeyLoadCts;
            if (cts == null || cts.IsCancellationRequested) return;
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private bool IsActiveJourneyRequest(string waybillNo, CancellationTokenSource requestCts)
        {
            return !_isClosing
                && !IsDisposed
                && ReferenceEquals(_journeyLoadCts, requestCts)
                && !requestCts.IsCancellationRequested
                && string.Equals(_activeJourneyWaybillNo, waybillNo, StringComparison.OrdinalIgnoreCase);
        }

        private void QueueJourneySnapshotSave(string waybillNo, WaybillJourneyRefreshResult refresh)
        {
            var rows = refresh?.ViewModel?.Rows?.Select(CloneJourneyRow).ToList() ?? new List<WaybillJourneyRow>();
            var rawJson = refresh?.RawJson ?? string.Empty;
            var parsedCount = rows.Count;

            AppLogger.Info(
                "[FullStackJourney] queue-save " +
                $"waybill={waybillNo}; parsedRows={parsedCount}; rawLength={rawJson.Length}");

            _ = Task.Run(async () =>
            {
                try
                {
                    await _fullStackJourneyService
                        .SaveJourneyAsync(waybillNo, rows, rawJson, CancellationToken.None)
                        .ConfigureAwait(false);

                    AppLogger.Info($"[FullStackJourney] details.db save completed waybill={waybillNo}; rows={parsedCount}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[FullStackJourney] details.db save failed waybill={waybillNo}: {ex.Message}");
                }
            });
        }

        private static async Task<string> ResolveJourneyAuthTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var memoryToken = JmsAuthStateService.CurrentToken;
            if (JmsAuthTokenService.IsValidJmsToken(memoryToken))
                return memoryToken;

            var webViewToken = string.Empty;
            if (JmsAuthTokenService.WebViewTokenReader != null)
            {
                try
                {
                    webViewToken = await JmsAuthTokenService.WebViewTokenReader().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppLogger.Warning($"[FullStackJourney] WebView authToken read failed: {ex.Message}");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (JmsAuthTokenService.IsValidJmsToken(webViewToken))
            {
                JmsAuthStateService.SetToken(webViewToken);
                AuthStateService.Instance.SetToken(webViewToken);
                AppLogger.Info("[FullStackJourney] authToken source=webview, valid=32hex");
                return webViewToken;
            }

            var configToken = string.Empty;
            try
            {
                configToken = JmsAuthTokenService.ConfigTokenProvider?.Invoke() ?? string.Empty;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[FullStackJourney] config authToken read failed: {ex.Message}");
            }

            if (JmsAuthTokenService.IsValidJmsToken(configToken))
            {
                JmsAuthStateService.SetToken(configToken);
                AuthStateService.Instance.SetToken(configToken);
                AppLogger.Info("[FullStackJourney] authToken source=local-config, valid=32hex");
                return configToken;
            }

            var authStateToken = AuthStateService.Instance.AuthToken;
            if (JmsAuthTokenService.IsValidJmsToken(authStateToken))
            {
                JmsAuthStateService.SetToken(authStateToken);
                AppLogger.Info("[FullStackJourney] authToken source=auth-state, valid=32hex");
                return authStateToken;
            }

            if (AuthStateService.Instance.IsAuthenticated)
                AppLogger.Warning("[FullStackJourney] rejected non-JMS token from AuthStateService; expected 32-hex JMS authToken.");

            return string.Empty;
        }

        private void ShowJourneyError(string message, bool hasCache)
        {
            SetJourneyStatus(message + (hasCache ? " Có dữ liệu cache cũ." : string.Empty));
        }

        private void BindJourneyGrid(WaybillJourneyViewModel vm, string statusText = null)
        {
            vm ??= new WaybillJourneyViewModel();

            var rows = vm.Rows?
                .OrderByDescending(x => x.ActionTime ?? DateTime.MinValue)
                .Select(CloneJourneyRow)
                .ToList() ?? new List<WaybillJourneyRow>();

            for (int i = 0; i < rows.Count; i++)
                rows[i].Stt = i + 1;

            var eventRows = rows
                .Select((row, index) => ToJourneyEventViewModel(row, index))
                .ToList();

            if (_webView != null && _webViewInitialized && _webView.CoreWebView2 != null)
            {
                try
                {
                    var payload = new
                    {
                        type = "JOURNEY_DATA",
                        journeyRows = eventRows
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(payload);
                    _webView.CoreWebView2.PostWebMessageAsJson(json);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Post JOURNEY_DATA failed", ex);
                }
            }

            LogJourneyBind(vm.WaybillNo, rows, eventRows.Count);
            SetJourneyStatus(statusText ?? (rows.Count == 0
                ? "Chưa có dữ liệu hành trình cho mã này."
                : BuildJourneyLoadedStatus(rows.Count, vm.FetchedAt ?? DateTime.Now, vm.Source)));
        }

        private static WaybillJourneyRow CloneJourneyRow(WaybillJourneyRow row)
        {
            return new WaybillJourneyRow
            {
                Stt = row.Stt,
                ActionTime = row.ActionTime,
                UploadedAt = row.UploadedAt,
                ActionType = row.ActionType,
                Description = row.Description,
                SiteInfo = row.SiteInfo,
                ContainerCode = row.ContainerCode,
                ScanSource = row.ScanSource,
                Weight = row.Weight,
                ConvertedWeight = row.ConvertedWeight,
                AttachmentText = row.AttachmentText,
                Severity = row.Severity,
                SiteCode = row.SiteCode,
                SiteName = row.SiteName,
                OperatorCode = row.OperatorCode,
                OperatorName = row.OperatorName,
                PackageNumber = row.PackageNumber,
                TaskCode = row.TaskCode,
                ImgType = row.ImgType,
                RawJson = row.RawJson
            };
        }

        private static JourneyEventViewModel ToJourneyEventViewModel(WaybillJourneyRow row, int index)
        {
            return new JourneyEventViewModel
            {
                Stt = index + 1,
                EventTime = row?.ActionTime,
                UploadTime = row?.UploadedAt,
                ActionType = CleanJourneyText(row?.ActionType),
                Description = CleanJourneyText(row?.Description),
                ScanSite = CleanJourneyText(row?.SiteInfo),
                BagNo = CleanJourneyText(row?.ContainerCode),
                ScanSource = CleanJourneyText(row?.ScanSource),
                Weight = CleanJourneyText(row?.Weight),
                VolumeWeight = CleanJourneyText(row?.ConvertedWeight),
                AttachmentText = CleanJourneyText(row?.AttachmentText),
                ImgType = row?.ImgType,
                RawEventJson = row?.RawJson ?? string.Empty
            };
        }

        private static string CleanJourneyText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            value = value.Trim();
            return string.Equals(value, "--", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "empty", StringComparison.OrdinalIgnoreCase)
                ? "-"
                : value;
        }

        private void SetJourneyStatus(string text)
        {
            SetFullStackStatus(text ?? string.Empty);
        }

        private static string BuildJourneyLoadedStatus(int count, DateTime fetchedAt, string source)
        {
            var sourceText = string.IsNullOrWhiteSpace(source) ? "Fresh" : source;
            return $"{sourceText} | {count:N0} sự kiện | Cập nhật lúc {fetchedAt:HH:mm:ss}";
        }

        private void LogJourneyBind(
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            int bindRows)
        {
            var first = rows != null && rows.Count > 0 ? rows[0] : null;
            var last = rows != null && rows.Count > 0 ? rows[^1] : null;
            AppLogger.Info(
                "[FullStackJourney] bind " +
                $"waybill={waybillNo}; currentJourneyWaybillAtBind={_activeJourneyWaybillNo}; " +
                $"detailsCount={(rows?.Count ?? 0)}; bindRows={bindRows}; " +
                $"first={FormatJourneyBindEvent(first)}; last={FormatJourneyBindEvent(last)}");
        }

        private static string FormatJourneyBindEvent(WaybillJourneyRow row)
        {
            if (row == null)
                return "--";
            return $"{row.ActionTime:yyyy-MM-dd HH:mm:ss} {TruncateJourneyLog(row.Description, 180)}";
        }

        private static string TruncateJourneyLog(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}
