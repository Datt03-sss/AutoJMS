#nullable enable
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    internal abstract class LocalGoogleSheetsProvider : IGoogleSheetsProvider, IDisposable
    {
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private SheetsService? _service;

        public abstract string ProviderName { get; }

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await EnsureServiceAsync(cancellationToken).ConfigureAwait(false) != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task<GoogleSheetReadResult> ReadAsync(GoogleSheetReadRequest request, CancellationToken cancellationToken)
        {
            var service = await EnsureServiceAsync(cancellationToken).ConfigureAwait(false);
            if (service == null)
                return GoogleSheetReadResult.Fail(ProviderName, "Local Google credential unavailable.");

            try
            {
                var batchGet = service.Spreadsheets.Values.BatchGet(request.SpreadsheetId);
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

                return new GoogleSheetReadResult
                {
                    Success = true,
                    ProviderName = ProviderName,
                    ValueRanges = ranges
                };
            }
            catch (Exception ex)
            when (ex is not GoogleApiException googleEx ||
                  googleEx.HttpStatusCode != System.Net.HttpStatusCode.TooManyRequests)
            {
                return GoogleSheetReadResult.Fail(ProviderName, ex.Message);
            }
        }

        public async Task<GoogleSheetWriteResult> WriteAsync(GoogleSheetWriteRequest request, CancellationToken cancellationToken)
        {
            var service = await EnsureServiceAsync(cancellationToken).ConfigureAwait(false);
            if (service == null)
                return GoogleSheetWriteResult.Fail(ProviderName, "Local Google credential unavailable.");

            try
            {
                if (request.Operation == GoogleSheetWriteOperation.Clear)
                {
                    var clearRequest = service.Spreadsheets.Values.Clear(
                        new ClearValuesRequest(),
                        request.SpreadsheetId,
                        request.Range);
                    await clearRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var valueRange = new ValueRange { Values = request.Values };
                    var updateRequest = service.Spreadsheets.Values.Update(
                        valueRange,
                        request.SpreadsheetId,
                        request.Range);
                    updateRequest.ValueInputOption =
                        SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                    await updateRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                }

                return GoogleSheetWriteResult.Ok(ProviderName);
            }
            catch (Exception ex)
            when (ex is not GoogleApiException googleEx ||
                  googleEx.HttpStatusCode != System.Net.HttpStatusCode.TooManyRequests)
            {
                return GoogleSheetWriteResult.Fail(ProviderName, ex.Message);
            }
        }

        public void Dispose()
        {
            if (_service is IDisposable disposable)
                disposable.Dispose();
            _service = null;
            _initLock.Dispose();
        }

        protected abstract Task<GoogleCredential?> LoadCredentialAsync(CancellationToken cancellationToken);

        private async Task<SheetsService?> EnsureServiceAsync(CancellationToken cancellationToken)
        {
            if (_service != null) return _service;

            await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_service != null) return _service;

                var credential = await LoadCredentialAsync(cancellationToken).ConfigureAwait(false);
                if (credential == null) return null;

                _service = new SheetsService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential.CreateScoped(SheetsService.Scope.Spreadsheets),
                    ApplicationName = "AutoJMS"
                });

                return _service;
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[GoogleSheets] provider={ProviderName} unavailable: {ex.Message}");
                return null;
            }
            finally
            {
                _initLock.Release();
            }
        }

        protected static GoogleCredential? TryCreateCredentialFromJson(string json, string providerName)
        {
            try
            {
                return CredentialFactory
                    .FromJson<ServiceAccountCredential>(json)
                    .ToGoogleCredential();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[GoogleSheets] provider={providerName} credential JSON invalid: {ex.Message}");
                return null;
            }
        }

        protected static GoogleCredential? TryCreateCredentialFromStream(Stream stream, string providerName)
        {
            try
            {
                return CredentialFactory
                    .FromStream<ServiceAccountCredential>(stream)
                    .ToGoogleCredential();
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[GoogleSheets] provider={providerName} credential file invalid: {ex.Message}");
                return null;
            }
        }
    }
}
