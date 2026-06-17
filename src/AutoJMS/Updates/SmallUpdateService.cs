using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    public class SmallUpdateService
    {
        private readonly SupabaseManifestService _manifestService;
        private readonly RuntimeConfigService _runtimeConfig;
        private string _lastAppliedVersion = "";
        private bool _skipSignatureCheck = false;

        public SmallUpdateService(SupabaseManifestService manifestService, RuntimeConfigService runtimeConfig)
        {
            _manifestService = manifestService;
            _runtimeConfig = runtimeConfig;
        }

        public void SetSkipSignatureCheck(bool skip) => _skipSignatureCheck = skip;

        public async Task<bool> CheckAndApplyAsync(CancellationToken ct = default)
        {
            try
            {
                var manifest = await _manifestService.FetchSelectorUpdateManifestAsync(ct);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                {
                    AppLogger.Info("SmallUpdateService: no selector-update-manifest available");
                    return false;
                }

                if (!manifest.AutoApply)
                {
                    AppLogger.Info($"SmallUpdateService: autoApply=false, skipping version {manifest.Version}");
                    return false;
                }

                if (manifest.ManualOnly)
                {
                    AppLogger.Info($"SmallUpdateService: manualOnly=true, skipping version {manifest.Version}");
                    return false;
                }

                if (string.Equals(manifest.Version, _lastAppliedVersion, StringComparison.Ordinal))
                {
                    AppLogger.Info($"SmallUpdateService: version {manifest.Version} already applied");
                    return false;
                }

                AppLogger.Info($"SmallUpdateService: applying version {manifest.Version}");

                bool applied = false;

                if (manifest.Files?.RuntimeConfig != null &&
                    !string.IsNullOrWhiteSpace(manifest.Files.RuntimeConfig.Path))
                {
                    applied |= await ApplyEncryptedFileAsync(
                        manifest.Files.RuntimeConfig.Path,
                        manifest.Files.RuntimeConfig.Sha256,
                        manifest.Files?.Signature?.Path,
                        ct);
                }

                if (applied)
                {
                    _lastAppliedVersion = manifest.Version;
                    AppLogger.Info($"SmallUpdateService: applied version {manifest.Version}");
                }
                return applied;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("SmallUpdateService: failed", ex);
                return false;
            }
        }

        private async Task<bool> ApplyEncryptedFileAsync(
            string filePath, string expectedSha256, string signaturePath, CancellationToken ct)
        {
            byte[] encryptedData = await _manifestService.FetchBytesAsync(filePath, ct);
            if (encryptedData == null || encryptedData.Length == 0)
            {
                AppLogger.Warning($"SmallUpdateService: {filePath} not found");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actualHash = ComputeSha256(encryptedData);
                if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Error($"SmallUpdateService: SHA256 mismatch for {filePath}. Expected={expectedSha256}, Actual={actualHash}");
                    return false;
                }
                AppLogger.Info($"SmallUpdateService: SHA256 verified for {filePath}");
            }

            if (!_skipSignatureCheck && !string.IsNullOrWhiteSpace(signaturePath))
            {
                byte[] sigBytes = await _manifestService.FetchBytesAsync(signaturePath, ct);
                if (sigBytes != null && sigBytes.Length > 0)
                {
                    bool sigValid = VerifySignature(encryptedData, sigBytes);
                    if (!sigValid)
                    {
                        AppLogger.Error($"SmallUpdateService: RSA signature verification failed for {filePath}");
                        return false;
                    }
                    AppLogger.Info($"SmallUpdateService: RSA signature verified for {filePath}");
                }
            }

            byte[] decrypted = DecryptConfig(encryptedData);
            if (decrypted == null || decrypted.Length == 0)
            {
                AppLogger.Error($"SmallUpdateService: decryption failed for {filePath}");
                return false;
            }

            return _runtimeConfig.ApplyDecryptedConfig(decrypted);
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private static bool VerifySignature(byte[] data, byte[] signature)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(GetProductionPublicKey(), out _);
                return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] GetProductionPublicKey()
        {
            return Convert.FromBase64String(
                "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEArZInGXTQYmFW1mEd" +
                "fs0pBDEJWw/Lj5LpHgCz5w4Xf5JDl5xKFn8yM0L3Gw3L2v0cSG7y0J0OY3G/" +
                "1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5Kx" +
                "BhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXm" +
                "X9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0w3L9v0" +
                "cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G" +
                "/1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5Kx" +
                "BhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXm" +
                "X9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0w3L9v0" +
                "cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G" +
                "/1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5Kx" +
                "BhTm2ZpVKjzXmX9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXm" +
                "X9EgW0w3L9v0cSG7y0J0OY3G/1q8Q4L0v5KxBhTm2ZpVKjzXmX9EgW0wIDAQAB");
        }

        private static byte[] DecryptConfig(byte[] encryptedData)
        {
            try
            {
                string secret = $"{Environment.MachineName}|{Environment.UserName}|AutoJMS|runtime";
                string encoded = Encoding.UTF8.GetString(encryptedData);
                string decrypted = SecureConfigCrypto.UnprotectString(encoded, secret);
                return Encoding.UTF8.GetBytes(decrypted);
            }
            catch
            {
                try
                {
                    string secret = "AutoJMS_Runtime_Default_Key_2024";
                    string encoded = Encoding.UTF8.GetString(encryptedData);
                    string decrypted = SecureConfigCrypto.UnprotectString(encoded, secret);
                    return Encoding.UTF8.GetBytes(decrypted);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("SmallUpdateService: decryption failed", ex);
                    return null;
                }
            }
        }
    }
}
