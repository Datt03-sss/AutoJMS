#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class AppSettings
    {
        [JsonPropertyName("zoomFactor")] public double ZoomFactor { get; set; } = 1.0;
        [JsonPropertyName("defaultUrl")] public string DefaultUrl { get; set; } = AppConfig.Current.JmsBaseUrl.TrimEnd('/');
        [JsonPropertyName("lastAuthToken")] public string LastAuthToken { get; set; } = "";
        [JsonPropertyName("downloadFolder")] public string DownloadFolder { get; set; } = "";
        [JsonPropertyName("defaultSheet")] public string DefaultSheet { get; set; } = "DKCH";
        [JsonPropertyName("useSheetByDefault")] public bool UseSheetByDefault { get; set; } = false;
        [JsonPropertyName("autoRefreshToken")] public bool AutoRefreshToken { get; set; } = true;
        [JsonPropertyName("lastMode")] public string LastMode { get; set; } = "DKCH1";
        [JsonPropertyName("theme")] public string Theme { get; set; } = "Light";
        [JsonPropertyName("defaultRowCount")] public int DefaultRowCount { get; set; } = 1;
        [JsonPropertyName("printerName")] public string PrinterName { get; set; } = "";
        [JsonPropertyName("paperWidth")] public int PaperWidth { get; set; } = 762;  
        [JsonPropertyName("paperHeight")] public int PaperHeight { get; set; } = 762; 
        [JsonPropertyName("BlockWhenQueueHasErrorJob")] public bool BlockWhenQueueHasErrorJob { get; set; } = true;
        [JsonPropertyName("BlockWhenPrinterPaused")] public bool BlockWhenPrinterPaused { get; set; } = true;
        [JsonPropertyName("BlockWhenPrinterOffline")] public bool BlockWhenPrinterOffline { get; set; } = true;
        [JsonPropertyName("PrintDefaultAutoPrint")] public bool PrintDefaultAutoPrint { get; set; } = true;
        [JsonPropertyName("EnablePrinterPreflight")] public bool EnablePrinterPreflight { get; set; } = true;
        [JsonPropertyName("MaxAutoJmsReprintCount")] public int MaxAutoJmsReprintCount { get; set; } = 5;
        [JsonPropertyName("PrintPaperWidthInch")] public decimal PrintPaperWidthInch { get; set; } = 3m;
        [JsonPropertyName("PrintPaperHeightInch")] public decimal PrintPaperHeightInch { get; set; } = 3m;
        [JsonPropertyName("PrinterPaperMode")] public string PrinterPaperMode { get; set; } = "AutoJMS_3x3";
        [JsonPropertyName("PrinterOriginalPaperName")] public string PrinterOriginalPaperName { get; set; } = "";
        [JsonPropertyName("PrinterOriginalSettingsBackup")] public string PrinterOriginalSettingsBackup { get; set; } = "";
        [JsonPropertyName("MiddleCode")] public string MiddleCode { get; set; } = "";
        [JsonPropertyName("MiddleCodeAliases")] public List<string> MiddleCodeAliases { get; set; } = new();
        [JsonPropertyName("MiddleCodeSegment2")] public string MiddleCodeSegment2 { get; set; } = "";
        [JsonPropertyName("AllowMiddleCodeSegment2Match")] public bool AllowMiddleCodeSegment2Match { get; set; } = true;
        [JsonPropertyName("DebugCaptureEnabled")] public bool DebugCaptureEnabled { get; set; } = false;
        [JsonPropertyName("DebugCaptureSlowApiThresholdMs")] public int DebugCaptureSlowApiThresholdMs { get; set; } = 1000;
        [JsonPropertyName("DebugCaptureMaxRequestBodyBytes")] public int DebugCaptureMaxRequestBodyBytes { get; set; } = 128000;
        [JsonPropertyName("DebugCaptureMaxResponseBodyBytes")] public int DebugCaptureMaxResponseBodyBytes { get; set; } = 256000;
        [JsonPropertyName("GoogleSheetsAccessMode")] public GoogleSheetsAccessMode GoogleSheetsAccessMode { get; set; } = GoogleSheetsAccessMode.TokenBroker;
        [JsonPropertyName("PreferServerGoogleSheetsProxy")] public bool PreferServerGoogleSheetsProxy { get; set; } = true;
        [JsonPropertyName("AllowLegacyLocalServiceAccount")] public bool AllowLegacyLocalServiceAccount { get; set; } = true;
        [JsonPropertyName("LegacyServiceAccountPath")] public string LegacyServiceAccountPath { get; set; } = "";
        [JsonPropertyName("EncryptedServiceAccountPath")] public string EncryptedServiceAccountPath { get; set; } = "appdata/secrets/service_account.sec";
        [JsonPropertyName("PreferGoogleSheetsTokenBroker")] public bool PreferGoogleSheetsTokenBroker { get; set; } = true;
        [JsonPropertyName("AllowLegacyLocalServiceAccountFallback")] public bool AllowLegacyLocalServiceAccountFallback { get; set; } = true;
        [JsonPropertyName("ArchiveLegacyServiceAccountAfterBrokerSuccess")] public bool ArchiveLegacyServiceAccountAfterBrokerSuccess { get; set; } = true;
        [JsonPropertyName("DeleteLegacyServiceAccountAfterBrokerSuccess")] public bool DeleteLegacyServiceAccountAfterBrokerSuccess { get; set; } = false;
        [JsonPropertyName("GoogleSheetsTokenRefreshSkewMinutes")] public int GoogleSheetsTokenRefreshSkewMinutes { get; set; } = 5;
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class SettingsManager
    {
        private static readonly string ConfigPath = AppPaths.AutoJmsJson;
        private static readonly string LegacySecureConfigPath = AppPaths.ConfigEncFile;
        private static readonly JsonSerializerOptions JsonOptions = AppConfig.CreateJsonOptions();
        private static readonly object Sync = new();
        private static readonly SemaphoreSlim AsyncSync = new(1, 1);

        public static AppSettings Load()
        {
            lock (Sync)
            {
                var settings = new AppSettings();

                AppSettings? localSettings = TryLoadPlainJson(ConfigPath);
                if (localSettings != null)
                {
                    var normalized = Normalize(localSettings);
                    Save(normalized);
                    return normalized;
                }

                AppSettings? legacySettings = TryLoadLegacySecureConfig();
                if (legacySettings != null)
                {
                    Save(legacySettings);
                    return Normalize(legacySettings);
                }

                Save(settings);
                return Normalize(settings);
            }
        }

        public static void Save(AppSettings settings)
        {
            lock (Sync)
            {
                AsyncSync.Wait();
                try
                {
                    string json = SerializeMergedSettings(Normalize(settings));
                    AtomicWriteAllText(ConfigPath, json);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Lỗi khi lưu cấu hình", ex);
                }
                finally
                {
                    AsyncSync.Release();
                }
            }
        }

        public static async Task SaveAsync(AppSettings settings)
        {
            await AsyncSync.WaitAsync();
            try
            {
                string json = SerializeMergedSettings(Normalize(settings));
                string tempPath = ConfigPath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? AppPaths.UserDataDir);
                await File.WriteAllTextAsync(tempPath, json);
                ReplaceAtomic(tempPath, ConfigPath);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Lỗi khi lưu cấu hình bất đồng bộ", ex);
            }
            finally
            {
                AsyncSync.Release();
            }
        }

        private static void ReplaceAtomic(string tempPath, string targetPath)
        {
            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(tempPath, targetPath);
        }

        private static void AtomicWriteAllText(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppPaths.UserDataDir);
            string temp = path + ".tmp";
            File.WriteAllText(temp, content);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static AppSettings Normalize(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.DefaultUrl))
                settings.DefaultUrl = AppConfig.Current.JmsBaseUrl.TrimEnd('/');

            settings.MiddleCode = (settings.MiddleCode ?? "").Trim().ToUpperInvariant();
            if (settings.PrintPaperWidthInch <= 0)
                settings.PrintPaperWidthInch = 3m;
            if (settings.PrintPaperHeightInch <= 0)
                settings.PrintPaperHeightInch = 3m;
            if (settings.MaxAutoJmsReprintCount <= 0)
                settings.MaxAutoJmsReprintCount = 5;
            settings.PrinterPaperMode = string.IsNullOrWhiteSpace(settings.PrinterPaperMode)
                ? "AutoJMS_3x3"
                : settings.PrinterPaperMode.Trim();
            settings.PrinterOriginalPaperName = (settings.PrinterOriginalPaperName ?? "").Trim();
            settings.PrinterOriginalSettingsBackup = (settings.PrinterOriginalSettingsBackup ?? "").Trim();
            settings.MiddleCodeSegment2 = (settings.MiddleCodeSegment2 ?? "").Trim().ToUpperInvariant();
            settings.MiddleCodeAliases = (settings.MiddleCodeAliases ?? new List<string>())
                .Append(settings.MiddleCode)
                .Select(x => (x ?? "").Trim().ToUpperInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            settings.LegacyServiceAccountPath = (settings.LegacyServiceAccountPath ?? "").Trim();
            settings.EncryptedServiceAccountPath = string.IsNullOrWhiteSpace(settings.EncryptedServiceAccountPath)
                ? "appdata/secrets/service_account.sec"
                : settings.EncryptedServiceAccountPath.Trim();
            if (settings.GoogleSheetsTokenRefreshSkewMinutes <= 0)
                settings.GoogleSheetsTokenRefreshSkewMinutes = 5;
            if (!settings.AllowLegacyLocalServiceAccountFallback)
                settings.AllowLegacyLocalServiceAccount = false;

            return settings;
        }

        private static string SerializeMergedSettings(AppSettings settings)
        {
            JsonObject root = new();
            if (File.Exists(ConfigPath))
            {
                try
                {
                    root = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    root = new JsonObject();
                }
            }

            RemoveKnownSettings(root);

            Set(root, "zoomFactor", settings.ZoomFactor);
            Set(root, "defaultUrl", settings.DefaultUrl);
            Set(root, "lastAuthToken", settings.LastAuthToken);
            Set(root, "downloadFolder", settings.DownloadFolder);
            Set(root, "defaultSheet", settings.DefaultSheet);
            Set(root, "useSheetByDefault", settings.UseSheetByDefault);
            Set(root, "autoRefreshToken", settings.AutoRefreshToken);
            Set(root, "lastMode", settings.LastMode);
            Set(root, "theme", settings.Theme);
            Set(root, "defaultRowCount", settings.DefaultRowCount);
            Set(root, "printerName", settings.PrinterName);
            Set(root, "paperWidth", settings.PaperWidth);
            Set(root, "paperHeight", settings.PaperHeight);
            Set(root, "BlockWhenQueueHasErrorJob", settings.BlockWhenQueueHasErrorJob);
            Set(root, "BlockWhenPrinterPaused", settings.BlockWhenPrinterPaused);
            Set(root, "BlockWhenPrinterOffline", settings.BlockWhenPrinterOffline);
            Set(root, "PrintDefaultAutoPrint", settings.PrintDefaultAutoPrint);
            Set(root, "EnablePrinterPreflight", settings.EnablePrinterPreflight);
            Set(root, "MaxAutoJmsReprintCount", settings.MaxAutoJmsReprintCount);
            Set(root, "PrintPaperWidthInch", settings.PrintPaperWidthInch);
            Set(root, "PrintPaperHeightInch", settings.PrintPaperHeightInch);
            Set(root, "PrinterPaperMode", settings.PrinterPaperMode);
            Set(root, "PrinterOriginalPaperName", settings.PrinterOriginalPaperName);
            Set(root, "PrinterOriginalSettingsBackup", settings.PrinterOriginalSettingsBackup);
            Set(root, "MiddleCode", settings.MiddleCode);
            Set(root, "MiddleCodeAliases", settings.MiddleCodeAliases);
            Set(root, "MiddleCodeSegment2", settings.MiddleCodeSegment2);
            Set(root, "AllowMiddleCodeSegment2Match", settings.AllowMiddleCodeSegment2Match);
            Set(root, "DebugCaptureEnabled", settings.DebugCaptureEnabled);
            Set(root, "DebugCaptureSlowApiThresholdMs", settings.DebugCaptureSlowApiThresholdMs);
            Set(root, "DebugCaptureMaxRequestBodyBytes", settings.DebugCaptureMaxRequestBodyBytes);
            Set(root, "DebugCaptureMaxResponseBodyBytes", settings.DebugCaptureMaxResponseBodyBytes);
            Set(root, "GoogleSheetsAccessMode", settings.GoogleSheetsAccessMode.ToString());
            Set(root, "PreferServerGoogleSheetsProxy", settings.PreferServerGoogleSheetsProxy);
            Set(root, "AllowLegacyLocalServiceAccount", settings.AllowLegacyLocalServiceAccount);
            Set(root, "LegacyServiceAccountPath", settings.LegacyServiceAccountPath);
            Set(root, "EncryptedServiceAccountPath", settings.EncryptedServiceAccountPath);
            Set(root, "PreferGoogleSheetsTokenBroker", settings.PreferGoogleSheetsTokenBroker);
            Set(root, "AllowLegacyLocalServiceAccountFallback", settings.AllowLegacyLocalServiceAccountFallback);
            Set(root, "ArchiveLegacyServiceAccountAfterBrokerSuccess", settings.ArchiveLegacyServiceAccountAfterBrokerSuccess);
            Set(root, "DeleteLegacyServiceAccountAfterBrokerSuccess", settings.DeleteLegacyServiceAccountAfterBrokerSuccess);
            Set(root, "GoogleSheetsTokenRefreshSkewMinutes", settings.GoogleSheetsTokenRefreshSkewMinutes);
            return root.ToJsonString(JsonOptions);
        }

        private static void Set(JsonObject root, string name, string value) => root[name] = value ?? "";
        private static void Set(JsonObject root, string name, int value) => root[name] = value;
        private static void Set(JsonObject root, string name, double value) => root[name] = value;
        private static void Set(JsonObject root, string name, decimal value) => root[name] = JsonValue.Create(value);
        private static void Set(JsonObject root, string name, bool value) => root[name] = value;
        private static void Set(JsonObject root, string name, List<string> values)
        {
            var array = new JsonArray();
            foreach (var value in values ?? new List<string>())
                array.Add(value ?? "");
            root[name] = array;
        }

        private static void RemoveKnownSettings(JsonObject root)
        {
            var names = new[]
            {
                "zoomFactor", "defaultUrl", "lastAuthToken", "downloadFolder", "defaultSheet",
                "useSheetByDefault", "autoRefreshToken", "lastMode", "theme", "defaultRowCount",
                "printerName", "paperWidth", "paperHeight", "MiddleCode", "MiddleCodeAliases",
                "MaxAllowedQueueJobsBeforePrint", "BlockWhenQueueHasErrorJob",
                "BlockWhenPrinterPaused", "BlockWhenPrinterOffline",
                "PrintDefaultAutoPrint", "EnablePrinterPreflight", "MaxAutoJmsReprintCount",
                "PrintPaperWidthInch", "PrintPaperHeightInch", "PrinterPaperMode",
                "PrinterOriginalPaperName", "PrinterOriginalSettingsBackup",
                "MiddleCodeSegment2", "AllowMiddleCodeSegment2Match",
                "DebugCaptureEnabled", "DebugCaptureSlowApiThresholdMs",
                "DebugCaptureMaxRequestBodyBytes", "DebugCaptureMaxResponseBodyBytes",
                "GoogleSheetsAccessMode", "PreferServerGoogleSheetsProxy",
                "AllowLegacyLocalServiceAccount", "LegacyServiceAccountPath",
                "EncryptedServiceAccountPath", "PreferGoogleSheetsTokenBroker",
                "AllowLegacyLocalServiceAccountFallback",
                "ArchiveLegacyServiceAccountAfterBrokerSuccess",
                "DeleteLegacyServiceAccountAfterBrokerSuccess",
                "GoogleSheetsTokenRefreshSkewMinutes"
            };

            foreach (var name in names)
                RemoveCaseInsensitive(root, name);
        }

        private static void RemoveCaseInsensitive(JsonObject root, string name)
        {
            var matches = root
                .Select(kvp => kvp.Key)
                .Where(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in matches)
                root.Remove(key);
        }

        private static AppSettings? TryLoadPlainJson(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Không đọc được {Path.GetFileName(path)}, thử cấu hình fallback. {ex.Message}");
                return null;
            }
        }

        private static AppSettings? TryLoadLegacySecureConfig()
        {
            if (!File.Exists(LegacySecureConfigPath)) return null;

            try
            {
                string protectedJson = File.ReadAllText(LegacySecureConfigPath);
                string json = SecureConfigCrypto.UnprotectString(protectedJson, BuildSettingsSecret());
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Không đọc được AutoJMS.config.enc legacy, dùng mặc định. {ex.Message}");
                return null;
            }
        }

        private static string BuildSettingsSecret()
            => $"{Environment.MachineName}|{Environment.UserName}|AutoJMS|settings";
    }
}
