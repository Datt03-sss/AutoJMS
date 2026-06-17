#nullable enable
using Google.Apis.Auth.OAuth2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    internal sealed class LegacyLocalServiceAccountProvider : LocalGoogleSheetsProvider
    {
        private readonly Func<string> _configuredPathFactory;

        public LegacyLocalServiceAccountProvider(Func<string> configuredPathFactory)
        {
            _configuredPathFactory = configuredPathFactory;
        }

        public override string ProviderName => "LegacyLocalServiceAccount";

        protected override async Task<GoogleCredential?> LoadCredentialAsync(CancellationToken cancellationToken)
        {
            string inlineJson = AppConfig.Current.GoogleServiceAccountJson;
            if (!string.IsNullOrWhiteSpace(inlineJson))
            {
                AppLogger.Warning("[GoogleSheets] provider=LegacyLocalServiceAccount source=inline-config-json");
                return TryCreateCredentialFromJson(inlineJson, ProviderName);
            }

            foreach (var candidate in GetCandidatePaths())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
                    continue;

                AppLogger.Warning("[GoogleSheets] provider=LegacyLocalServiceAccount source=existing-local-file");
                await using var stream = new FileStream(
                    candidate,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);
                return TryCreateCredentialFromStream(stream, ProviderName);
            }

            AppLogger.Info("[GoogleSheets] legacy service account not found");
            return null;
        }

        private IEnumerable<string> GetCandidatePaths()
        {
            var settings = SettingsManager.Load();
            if (!string.IsNullOrWhiteSpace(settings.LegacyServiceAccountPath))
                yield return ResolvePath(settings.LegacyServiceAccountPath);

            string configuredPath = _configuredPathFactory();
            if (!string.IsNullOrWhiteSpace(configuredPath))
                yield return ResolvePath(configuredPath);

            yield return AppPaths.GoogleServiceAccountJson;
            yield return Path.Combine(AppPaths.InstallRoot, "service_account.json");
            yield return Path.Combine(AppPaths.InstallRoot, "AppData", "service_account.json");
        }

        private static string ResolvePath(string configuredPath)
        {
            string path = configuredPath.Trim();
            if (Path.IsPathRooted(path))
                return path;

            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            if (string.Equals(normalized, "service_account.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, Path.Combine("AppData", "service_account.json"), StringComparison.OrdinalIgnoreCase))
            {
                return AppPaths.GoogleServiceAccountJson;
            }

            return Path.Combine(AppPaths.InstallRoot, normalized);
        }
    }
}
