#nullable enable
using Google.Apis.Auth.OAuth2;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    internal sealed class LocalEncryptedServiceAccountProvider : LocalGoogleSheetsProvider
    {
        public override string ProviderName => "LocalEncryptedServiceAccount";

        protected override async Task<GoogleCredential?> LoadCredentialAsync(CancellationToken cancellationToken)
        {
            string path = ResolveEncryptedPath();
            if (!File.Exists(path))
            {
                await TryCreateEncryptedCopyAsync(path, cancellationToken).ConfigureAwait(false);
                if (!File.Exists(path)) return null;
            }

            try
            {
                string protectedJson = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                string json = SecureConfigCrypto.UnprotectString(protectedJson, ResolveSecret());
                AppLogger.Info("[GoogleSheets] provider=LocalEncryptedServiceAccount source=encrypted-local-file");
                return TryCreateCredentialFromJson(json, ProviderName);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[GoogleSheets] encrypted local credential unavailable: {ex.Message}");
                return null;
            }
        }

        private static async Task TryCreateEncryptedCopyAsync(string targetPath, CancellationToken cancellationToken)
        {
            var settings = SettingsManager.Load();
            if (!settings.AllowLegacyLocalServiceAccount)
                return;

            string legacyPath = AppPaths.GoogleServiceAccountJson;
            if (!File.Exists(legacyPath))
                return;

            try
            {
                string json = await File.ReadAllTextAsync(legacyPath, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? AppPaths.SecretsDir);
                string protectedJson = SecureConfigCrypto.ProtectString(json, ResolveSecret());
                string tempPath = targetPath + ".tmp";
                await File.WriteAllTextAsync(tempPath, protectedJson, cancellationToken).ConfigureAwait(false);
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(tempPath, targetPath);
                AppLogger.Info("[GoogleSheets] encrypted local credential created");
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"[GoogleSheets] encrypted local credential creation failed: {ex.Message}");
            }
        }

        private static string ResolveEncryptedPath()
        {
            var settings = SettingsManager.Load();
            string configured = settings.EncryptedServiceAccountPath;
            if (string.IsNullOrWhiteSpace(configured))
                return AppPaths.EncryptedGoogleServiceAccount;

            string normalized = configured.Trim().Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized))
                return normalized;

            if (normalized.StartsWith("appdata" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(AppPaths.InstallRoot, normalized);

            return Path.Combine(AppPaths.InstallRoot, normalized);
        }

        private static string ResolveSecret() =>
            $"{Environment.MachineName}|{Environment.UserName}|AutoJMS|google-sheets";
    }
}
