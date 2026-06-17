#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace AutoJMS
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class AppRuntimeConfig
    {
        public const string DefaultUpdateXmlUrl = "https://raw.githubusercontent.com/Datt03-sss/AutoJMS-Update/main/update.xml";

        [JsonPropertyName("firebaseUrl")] public string FirebaseUrl { get; set; } = "";
        [JsonPropertyName("firebaseDatabaseSecret")] public string FirebaseDatabaseSecret { get; set; } = "";
        [JsonPropertyName("dataSpreadsheetId")] public string DataSpreadsheetId { get; set; } = "";
        [JsonPropertyName("googleSheetUrl")] public string GoogleSheetUrl { get; set; } = "";
        [JsonPropertyName("licenseSpreadsheetId")] public string LicenseSpreadsheetId { get; set; } = "";
        [JsonPropertyName("appsScriptUrl")] public string AppsScriptUrl { get; set; } = "";
        [JsonPropertyName("googleServiceAccountJson")] public string GoogleServiceAccountJson { get; set; } = "";
        [JsonPropertyName("googleCredentialPath")] public string GoogleCredentialPath { get; set; } = Path.Combine("AppData", "service_account.json");
        [JsonPropertyName("jmsBaseUrl")] public string JmsBaseUrl { get; set; } = "https://jms.jtexpress.vn";
        [JsonPropertyName("jmsApiBaseUrl")] public string JmsApiBaseUrl { get; set; } = "https://jmsgw.jtexpress.vn";
        [JsonPropertyName("internetCheckUrl")] public string InternetCheckUrl { get; set; } = "http://clients3.google.com/generate_204";
        [JsonPropertyName("updateXmlUrl")] public string UpdateXmlUrl { get; set; } = DefaultUpdateXmlUrl;
        [JsonPropertyName("actionSiteCode")] public string ActionSiteCode { get; set; } = "";
        [JsonPropertyName("moduleServerUrl")] public string ModuleServerUrl { get; set; } = "";


        public void Normalize()
        {
            FirebaseUrl = NormalizeBaseUrl(FirebaseUrl);
            JmsBaseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(JmsBaseUrl) ? "https://jms.jtexpress.vn" : JmsBaseUrl);
            JmsApiBaseUrl = NormalizeBaseUrl(string.IsNullOrWhiteSpace(JmsApiBaseUrl) ? "https://jmsgw.jtexpress.vn" : JmsApiBaseUrl);

            if (string.IsNullOrWhiteSpace(InternetCheckUrl))
                InternetCheckUrl = "http://clients3.google.com/generate_204";

            if (!string.IsNullOrWhiteSpace(GoogleSheetUrl) && string.IsNullOrWhiteSpace(DataSpreadsheetId))
                DataSpreadsheetId = ExtractSpreadsheetId(GoogleSheetUrl);

            if (!string.IsNullOrWhiteSpace(DataSpreadsheetId) && string.IsNullOrWhiteSpace(GoogleSheetUrl))
                GoogleSheetUrl = $"https://docs.google.com/spreadsheets/d/{DataSpreadsheetId}";

            if (string.IsNullOrWhiteSpace(UpdateXmlUrl))
                UpdateXmlUrl = DefaultUpdateXmlUrl;
        }

        public void MergeFrom(AppRuntimeConfig? source, bool includeFirebase)
        {
            if (source == null) return;

            if (includeFirebase)
            {
                FirebaseUrl = Pick(source.FirebaseUrl, FirebaseUrl);
                FirebaseDatabaseSecret = Pick(source.FirebaseDatabaseSecret, FirebaseDatabaseSecret);
            }

            DataSpreadsheetId = Pick(source.DataSpreadsheetId, DataSpreadsheetId);
            GoogleSheetUrl = Pick(source.GoogleSheetUrl, GoogleSheetUrl);
            LicenseSpreadsheetId = Pick(source.LicenseSpreadsheetId, LicenseSpreadsheetId);
            AppsScriptUrl = Pick(source.AppsScriptUrl, AppsScriptUrl);
            GoogleServiceAccountJson = Pick(source.GoogleServiceAccountJson, GoogleServiceAccountJson);
            GoogleCredentialPath = Pick(source.GoogleCredentialPath, GoogleCredentialPath);
            JmsBaseUrl = Pick(source.JmsBaseUrl, JmsBaseUrl);
            JmsApiBaseUrl = Pick(source.JmsApiBaseUrl, JmsApiBaseUrl);
            InternetCheckUrl = Pick(source.InternetCheckUrl, InternetCheckUrl);
            UpdateXmlUrl = Pick(source.UpdateXmlUrl, UpdateXmlUrl);
            Normalize();
        }

        public string BuildJmsUrl(string relativePath) => CombineUrl(JmsBaseUrl, relativePath);
        public string BuildJmsApiUrl(string relativePath) => CombineUrl(JmsApiBaseUrl, relativePath);

        public static string ExtractSpreadsheetId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string trimmed = value.Trim();
            Match match = Regex.Match(trimmed, @"/spreadsheets/d/([^/?#]+)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
            return trimmed;
        }

        private static string Pick(string? candidate, string current) => string.IsNullOrWhiteSpace(candidate) ? current : candidate.Trim();
        private static string NormalizeBaseUrl(string value) => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().TrimEnd('/') + "/";
        private static string CombineUrl(string baseUrl, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return relativePath;
            return baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }
    }

    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public static class AppConfig
    {
        private static readonly string SecureConfigPath = AppPaths.SecureFile;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static JsonSerializerOptions CreateJsonOptions() => new(JsonOptions);
        private static readonly object Sync = new();
        private static AppRuntimeConfig _current = new AppRuntimeConfig();
        private static string _lastSecret = "";

        public static AppRuntimeConfig Current => _current;

        public static void LoadBootstrap(string machineKey)
        {
            lock (Sync)
            {
                string secret = ResolveSecret(machineKey);
                var config = new AppRuntimeConfig();

                if (File.Exists(SecureConfigPath))
                {
                    try
                    {
                        string protectedJson = File.ReadAllText(SecureConfigPath);
                        string json = SecureConfigCrypto.UnprotectString(protectedJson, secret);
                        config = JsonSerializer.Deserialize<AppRuntimeConfig>(json, JsonOptions) ?? new AppRuntimeConfig();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warning($"Không đọc được AutoJMS.secure, dùng mặc định. {ex.Message}");
                    }
                }

                ApplyEnvironment(config);
                config.Normalize();
                _current = config;
                _lastSecret = secret;

                if (!File.Exists(SecureConfigPath) && HasBootstrapEnvironment())
                    SaveCurrent();
            }
        }

        public static bool HasFirebaseConfig() => !string.IsNullOrWhiteSpace(Current.FirebaseUrl);

        public static void ApplyLicenseConfig(AppRuntimeConfig? licenseConfig)
        {
            _current.MergeFrom(licenseConfig, includeFirebase: false);
            SaveCurrent();
        }

        public static void SaveCurrent()
        {
            lock (Sync)
            {
                if (string.IsNullOrWhiteSpace(_lastSecret))
                    _lastSecret = ResolveSecret(Environment.MachineName);

                string json = JsonSerializer.Serialize(_current, JsonOptions);
                string protectedJson = SecureConfigCrypto.ProtectString(json, _lastSecret);
                AtomicWriteAllText(SecureConfigPath, protectedJson);
            }
        }

        private static void AtomicWriteAllText(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        private static string ResolveSecret(string machineKey)
        {
            string? configuredKey = Environment.GetEnvironmentVariable("AUTOJMS_CONFIG_KEY");
            if (!string.IsNullOrWhiteSpace(configuredKey)) return configuredKey;
            return $"{Environment.MachineName}|{Environment.UserName}|{machineKey}|AutoJMS";
        }

        private static bool HasBootstrapEnvironment() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTOJMS_FIREBASE_URL"));

        private static void ApplyEnvironment(AppRuntimeConfig config)
        {
            Apply("AUTOJMS_FIREBASE_URL", value => config.FirebaseUrl = value);
            Apply("AUTOJMS_FIREBASE_SECRET", value => config.FirebaseDatabaseSecret = value);
            Apply("AUTOJMS_DATA_SPREADSHEET_ID", value => config.DataSpreadsheetId = value);
            Apply("AUTOJMS_GOOGLE_SHEET_URL", value => config.GoogleSheetUrl = value);
            Apply("AUTOJMS_LICENSE_SPREADSHEET_ID", value => config.LicenseSpreadsheetId = value);
            Apply("AUTOJMS_APPS_SCRIPT_URL", value => config.AppsScriptUrl = value);
            Apply("AUTOJMS_GOOGLE_SERVICE_ACCOUNT_JSON", value => config.GoogleServiceAccountJson = value);
            Apply("AUTOJMS_GOOGLE_CREDENTIAL_PATH", value => config.GoogleCredentialPath = value);
            Apply("AUTOJMS_JMS_BASE_URL", value => config.JmsBaseUrl = value);
            Apply("AUTOJMS_JMS_API_BASE_URL", value => config.JmsApiBaseUrl = value);
            Apply("AUTOJMS_INTERNET_CHECK_URL", value => config.InternetCheckUrl = value);
            Apply("AUTOJMS_UPDATE_XML_URL", value => config.UpdateXmlUrl = value);
        }

        private static void Apply(string name, Action<string> setter)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) setter(value.Trim());
        }
    }
}
