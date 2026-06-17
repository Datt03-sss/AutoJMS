#nullable enable
using AutoJMS.Diagnostics.AppCapture;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    public sealed class GoogleSheetsGrant
    {
        public string AccessToken { get; init; } = "";
        public DateTimeOffset ExpiresAt { get; init; }
        public int ExpiresInSeconds { get; init; }
        public string SpreadsheetId { get; init; } = "";
        public string Provider { get; init; } = "";
    }

    internal sealed class GoogleSheetsTokenBrokerProvider : IGoogleSheetsProvider, IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _brokerHttp;
        private readonly SemaphoreSlim _grantLock = new(1, 1);
        private GoogleSheetsGrant? _grant;
        private SheetsService? _service;

        public GoogleSheetsTokenBrokerProvider()
        {
            var handler = new AppHttpCaptureHandler(new HttpClientHandler(), "GoogleSheetsTokenBrokerProvider");
            _brokerHttp = new HttpClient(handler)
            {
                BaseAddress = new Uri(LicenseApiService.ApiBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public string ProviderName => "TokenBroker";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(LicenseApiService.CurrentAccessToken));
        }

        public async Task<GoogleSheetReadResult> ReadAsync(GoogleSheetReadRequest request, CancellationToken cancellationToken)
        {
            return await ExecuteWithGrantRetryAsync(
                request.SpreadsheetId,
                async service =>
                {
                    var batchGet = service.Spreadsheets.Values.BatchGet(ResolveSpreadsheetId(request.SpreadsheetId));
                    batchGet.Ranges = request.Ranges?.ToList() ?? new List<string>();
                    var response = await batchGet.ExecuteAsync(cancellationToken).ConfigureAwait(false);

                    var ranges = new List<IList<IList<object>>>();
                    if (response?.ValueRanges != null)
                    {
                        foreach (var valueRange in response.ValueRanges)
                            ranges.Add(valueRange.Values ?? new List<IList<object>>());
                    }

                    while (ranges.Count < (request.Ranges?.Count ?? 0))
                        ranges.Add(new List<IList<object>>());

                    await ArchiveLegacyLocalCredentialAfterSuccessAsync(cancellationToken).ConfigureAwait(false);
                    return new GoogleSheetReadResult
                    {
                        Success = true,
                        ProviderName = ProviderName,
                        ValueRanges = ranges
                    };
                },
                ex => GoogleSheetReadResult.Fail(ProviderName, ex.Message),
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<GoogleSheetWriteResult> WriteAsync(GoogleSheetWriteRequest request, CancellationToken cancellationToken)
        {
            return await ExecuteWithGrantRetryAsync(
                request.SpreadsheetId,
                async service =>
                {
                    if (request.Operation == GoogleSheetWriteOperation.Clear)
                    {
                        var clearRequest = service.Spreadsheets.Values.Clear(
                            new ClearValuesRequest(),
                            ResolveSpreadsheetId(request.SpreadsheetId),
                            request.Range);
                        await clearRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var valueRange = new ValueRange { Values = request.Values };
                        var updateRequest = service.Spreadsheets.Values.Update(
                            valueRange,
                            ResolveSpreadsheetId(request.SpreadsheetId),
                            request.Range);
                        updateRequest.ValueInputOption =
                            SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                        await updateRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await ArchiveLegacyLocalCredentialAfterSuccessAsync(cancellationToken).ConfigureAwait(false);
                    return GoogleSheetWriteResult.Ok(ProviderName);
                },
                ex => GoogleSheetWriteResult.Fail(ProviderName, ex.Message),
                cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _service?.Dispose();
            _brokerHttp.Dispose();
            _grantLock.Dispose();
        }

        private async Task<T> ExecuteWithGrantRetryAsync<T>(
            string requestedSpreadsheetId,
            Func<SheetsService, Task<T>> operation,
            Func<Exception, T> fail,
            CancellationToken cancellationToken)
        {
            try
            {
                var service = await EnsureServiceAsync(requestedSpreadsheetId, forceRefresh: false, cancellationToken)
                    .ConfigureAwait(false);
                return await operation(service).ConfigureAwait(false);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
            {
                try
                {
                    var service = await EnsureServiceAsync(requestedSpreadsheetId, forceRefresh: true, cancellationToken)
                        .ConfigureAwait(false);
                    return await operation(service).ConfigureAwait(false);
                }
                catch (Exception retryEx)
                {
                    return fail(retryEx);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                return fail(ex);
            }
        }

        private async Task<SheetsService> EnsureServiceAsync(
            string requestedSpreadsheetId,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            await _grantLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (forceRefresh || _grant == null || IsGrantExpiring(_grant))
                {
                    _grant = await GetGrantAsync(cancellationToken).ConfigureAwait(false);
                    _service?.Dispose();
                    _service = BuildSheetsService(_grant.AccessToken);
                    AppLogger.Info($"[GoogleSheets] provider=TokenBroker grant=success expiresAt={_grant.ExpiresAt:O}");
                }

                if (_service == null)
                    _service = BuildSheetsService(_grant.AccessToken);

                string grantSpreadsheetId = (_grant.SpreadsheetId ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(grantSpreadsheetId)
                    && !string.IsNullOrWhiteSpace(requestedSpreadsheetId)
                    && !string.Equals(grantSpreadsheetId, requestedSpreadsheetId, StringComparison.Ordinal))
                {
                    AppLogger.Warning("[GoogleSheets] provider=TokenBroker requested spreadsheet differs from grant spreadsheet; using requested id for backward compatibility.");
                }

                return _service;
            }
            finally
            {
                _grantLock.Release();
            }
        }

        private static bool IsGrantExpiring(GoogleSheetsGrant grant)
        {
            var settings = SettingsManager.Load();
            int skewMinutes = settings.GoogleSheetsTokenRefreshSkewMinutes <= 0
                ? 5
                : settings.GoogleSheetsTokenRefreshSkewMinutes;
            return DateTimeOffset.UtcNow >= grant.ExpiresAt.AddMinutes(-skewMinutes);
        }

        private async Task<GoogleSheetsGrant> GetGrantAsync(CancellationToken cancellationToken)
        {
            string licenseToken = LicenseApiService.CurrentAccessToken;
            if (string.IsNullOrWhiteSpace(licenseToken))
                throw new InvalidOperationException("Missing license token for Google Sheets grant.");

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/google-sheets/grant");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", licenseToken);
            using var response = await _brokerHttp.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Grant failed HTTP {(int)response.StatusCode}");

            var dto = JsonSerializer.Deserialize<GrantResponseDto>(body, JsonOptions);
            if (dto?.Ok != true || string.IsNullOrWhiteSpace(dto.AccessToken))
                throw new InvalidOperationException(dto?.Error ?? "Invalid Google Sheets grant response.");

            return new GoogleSheetsGrant
            {
                AccessToken = dto.AccessToken,
                ExpiresAt = dto.ExpiresAt == default ? DateTimeOffset.UtcNow.AddSeconds(dto.ExpiresInSeconds) : dto.ExpiresAt,
                ExpiresInSeconds = dto.ExpiresInSeconds,
                SpreadsheetId = dto.SpreadsheetId ?? "",
                Provider = dto.Provider ?? "google-sheets-token-broker"
            };
        }

        private static SheetsService BuildSheetsService(string accessToken)
        {
            var credential = GoogleCredential.FromAccessToken(accessToken);
            return new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AutoJMS"
            });
        }

        private string ResolveSpreadsheetId(string requestedSpreadsheetId)
        {
            if (!string.IsNullOrWhiteSpace(requestedSpreadsheetId))
                return requestedSpreadsheetId.Trim();

            if (!string.IsNullOrWhiteSpace(_grant?.SpreadsheetId))
                return _grant.SpreadsheetId.Trim();

            return AppConfig.Current.DataSpreadsheetId;
        }

        private static async Task ArchiveLegacyLocalCredentialAfterSuccessAsync(CancellationToken cancellationToken)
        {
            var settings = SettingsManager.Load();
            if (!settings.ArchiveLegacyServiceAccountAfterBrokerSuccess &&
                !settings.DeleteLegacyServiceAccountAfterBrokerSuccess)
            {
                return;
            }

            string path = AppPaths.GoogleServiceAccountJson;
            if (!File.Exists(path))
                return;

            try
            {
                if (settings.DeleteLegacyServiceAccountAfterBrokerSuccess)
                {
                    File.Delete(path);
                    AppLogger.Info("[GoogleSheets] legacy service_account.json deleted after broker success");
                    return;
                }

                string backupDir = Path.Combine(AppPaths.UserDataDir, "backup");
                Directory.CreateDirectory(backupDir);
                string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string backupPath = Path.Combine(backupDir, $"service_account.json.legacy-{stamp}.bak");
                File.Move(path, backupPath);
                AppLogger.Info($"[GoogleSheets] legacy service_account.json archived path={backupPath}");
            }
            catch (Exception ex)
            {
                await Task.CompletedTask.ConfigureAwait(false);
                if (!cancellationToken.IsCancellationRequested)
                    AppLogger.Warning($"[GoogleSheets] legacy service_account archive failed: {ex.Message}");
            }
        }

        private sealed class GrantResponseDto
        {
            [JsonPropertyName("ok")] public bool Ok { get; set; }
            [JsonPropertyName("provider")] public string Provider { get; set; } = "";
            [JsonPropertyName("accessToken")] public string AccessToken { get; set; } = "";
            [JsonPropertyName("expiresAt")] public DateTimeOffset ExpiresAt { get; set; }
            [JsonPropertyName("expiresInSeconds")] public int ExpiresInSeconds { get; set; }
            [JsonPropertyName("spreadsheetId")] public string SpreadsheetId { get; set; } = "";
            [JsonPropertyName("scopes")] public List<string> Scopes { get; set; } = new();
            [JsonPropertyName("error")] public string Error { get; set; } = "";
        }
    }
}
