#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    public sealed class GoogleSheetAccessService : IDisposable
    {
        private readonly Func<string> _credentialPathFactory;
        private readonly object _sync = new();
        private List<IGoogleSheetsProvider>? _providers;
        private string _lastProviderName = "";

        public GoogleSheetAccessService(Func<string> credentialPathFactory)
        {
            _credentialPathFactory = credentialPathFactory;
        }

        public string CurrentProviderName { get; private set; } = "None";

        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            foreach (var provider in BuildProviders())
            {
                if (await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                    return true;
            }

            return false;
        }

        public async Task<GoogleSheetReadResult> ReadAsync(GoogleSheetReadRequest request, CancellationToken cancellationToken = default)
        {
            string lastError = "";
            foreach (var provider in BuildProviders())
            {
                if (!await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    lastError = $"{provider.ProviderName} unavailable";
                    continue;
                }

                var result = await provider.ReadAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    MarkProvider(provider.ProviderName);
                    return result;
                }

                lastError = result.Message;
                AppLogger.Warning($"[GoogleSheets] provider={provider.ProviderName} read failed: {result.Message}");
            }

            CurrentProviderName = "None";
            return GoogleSheetReadResult.Fail("None", lastError.Length == 0 ? "No GoogleSheet provider available." : lastError);
        }

        public async Task<GoogleSheetWriteResult> WriteAsync(GoogleSheetWriteRequest request, CancellationToken cancellationToken = default)
        {
            string lastError = "";
            foreach (var provider in BuildProviders())
            {
                if (!await provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
                {
                    lastError = $"{provider.ProviderName} unavailable";
                    continue;
                }

                var result = await provider.WriteAsync(request, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    MarkProvider(provider.ProviderName);
                    return result;
                }

                lastError = result.Message;
                AppLogger.Warning($"[GoogleSheets] provider={provider.ProviderName} write failed: {result.Message}");
            }

            CurrentProviderName = "None";
            return GoogleSheetWriteResult.Fail("None", lastError.Length == 0 ? "No GoogleSheet provider available." : lastError);
        }

        public void Reset()
        {
            lock (_sync)
            {
                if (_providers != null)
                {
                    foreach (var provider in _providers.OfType<IDisposable>())
                        provider.Dispose();
                }

                _providers = null;
                CurrentProviderName = "None";
                _lastProviderName = "";
            }
        }

        public void Dispose() => Reset();

        private IReadOnlyList<IGoogleSheetsProvider> BuildProviders()
        {
            lock (_sync)
            {
                var settings = SettingsManager.Load();
                if (settings.GoogleSheetsAccessMode == GoogleSheetsAccessMode.Disabled)
                    return Array.Empty<IGoogleSheetsProvider>();

                if (_providers != null)
                    return _providers;

                var broker = new GoogleSheetsTokenBrokerProvider();
                var encrypted = new LocalEncryptedServiceAccountProvider();
                var legacy = new LegacyLocalServiceAccountProvider(_credentialPathFactory);
                var providers = new List<IGoogleSheetsProvider>();

                switch (settings.GoogleSheetsAccessMode)
                {
                    case GoogleSheetsAccessMode.ServerOnly:
                        providers.Add(broker);
                        break;
                    case GoogleSheetsAccessMode.TokenBroker:
                        providers.Add(broker);
                        if (settings.AllowLegacyLocalServiceAccountFallback)
                            AddLocalFallbacks(providers, encrypted, legacy, settings);
                        break;
                    case GoogleSheetsAccessMode.LegacyLocalOnly:
                        AddLocalFallbacks(providers, encrypted, legacy, settings);
                        break;
                    default:
                        if (settings.PreferGoogleSheetsTokenBroker)
                            providers.Add(broker);
                        if (settings.AllowLegacyLocalServiceAccountFallback)
                            AddLocalFallbacks(providers, encrypted, legacy, settings);
                        if (!settings.PreferGoogleSheetsTokenBroker)
                            providers.Add(broker);
                        break;
                }

                _providers = providers;
                return _providers;
            }
        }

        private static void AddLocalFallbacks(
            List<IGoogleSheetsProvider> providers,
            IGoogleSheetsProvider encrypted,
            IGoogleSheetsProvider legacy,
            AppSettings settings)
        {
            providers.Add(encrypted);
            if (settings.AllowLegacyLocalServiceAccount)
                providers.Add(legacy);
        }

        private void MarkProvider(string providerName)
        {
            CurrentProviderName = providerName;
            if (string.Equals(_lastProviderName, providerName, StringComparison.OrdinalIgnoreCase))
                return;

            _lastProviderName = providerName;
            AppLogger.Info($"[GoogleSheets] active provider={providerName}");
            if (string.Equals(providerName, "TokenBroker", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info("[GoogleSheets] provider=TokenBroker direct Google Sheets access enabled");
            }
            else if (string.Equals(providerName, "LegacyLocalServiceAccount", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warning("[GoogleSheets] Đang dùng GoogleSheet credential local legacy. Bản sau sẽ chuyển sang token broker.");
            }
        }
    }
}
