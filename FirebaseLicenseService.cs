#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoJMS
{
    public class FirebaseLicenseService
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public class LicenseData
        {
            public string? hwid { get; set; }
            public string? status { get; set; }
            public string? googleSheetUrl { get; set; }
            public string? dataSpreadsheetId { get; set; }
            public string? licenseSpreadsheetId { get; set; }
            public string? licenseSheetName { get; set; }
            public string? appsScriptUrl { get; set; }
            public string? googleServiceAccountJson { get; set; }
            public string? googleCredentialPath { get; set; }
            public string? jmsBaseUrl { get; set; }
            public string? jmsApiBaseUrl { get; set; }
            public string? internetCheckUrl { get; set; }
            public string? updateXmlUrl { get; set; }
            public AppRuntimeConfig? config { get; set; }
            public AppRuntimeConfig? settings { get; set; }

            public AppRuntimeConfig ToRuntimeConfig()
            {
                var runtimeConfig = new AppRuntimeConfig();
                runtimeConfig.MergeFrom(config, includeFirebase: true);
                runtimeConfig.MergeFrom(settings, includeFirebase: true);
                runtimeConfig.MergeFrom(new AppRuntimeConfig
                {
                    DataSpreadsheetId = dataSpreadsheetId ?? "",
                    GoogleSheetUrl = googleSheetUrl ?? "",
                    LicenseSpreadsheetId = licenseSpreadsheetId ?? "",
                    LicenseSheetName = licenseSheetName ?? "",
                    AppsScriptUrl = appsScriptUrl ?? "",
                    GoogleServiceAccountJson = googleServiceAccountJson ?? "",
                    GoogleCredentialPath = googleCredentialPath ?? "",
                    JmsBaseUrl = jmsBaseUrl ?? "",
                    JmsApiBaseUrl = jmsApiBaseUrl ?? "",
                    InternetCheckUrl = internetCheckUrl ?? "",
                    UpdateXmlUrl = updateXmlUrl ?? ""
                }, includeFirebase: true);
                runtimeConfig.Normalize();
                return runtimeConfig;
            }
        }

        public static async Task<(bool success, string message, AppRuntimeConfig? config)> CheckLicenseAsync(string key, string currentHwid)
        {
            try
            {
                if (!RuntimeConfigManager.HasFirebaseConfig())
                    return (false, "Thiếu cấu hình Firebase. Hãy tạo AutoJMS.secure hoặc thiết lập AUTOJMS_FIREBASE_URL.", null);

                string url = BuildFirebaseUrl($"Licenses/{Uri.EscapeDataString(key)}");

                HttpResponseMessage response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return (false, "Lỗi kết nối máy chủ", null);

                string jsonResponse = await response.Content.ReadAsStringAsync();

                if (jsonResponse == "null")
                    return (false, "Key không tồn tại!", null);

                var license = JsonSerializer.Deserialize<LicenseData>(jsonResponse, JsonOptions);
                if (license == null)
                    return (false, "Dữ liệu license không hợp lệ!", null);

                if (license.status != "active")
                    return (false, "Key không xác định!", null);

                if (string.IsNullOrEmpty(license.hwid))
                {
                    await PatchLicenseFieldsAsync(key, new { hwid = currentHwid });
                    return (true, "Kích hoạt thành công!", license.ToRuntimeConfig());
                }

                if (license.hwid == currentHwid)
                    return (true, "Xác thực bản quyền thành công!", license.ToRuntimeConfig());

                return (false, "Key này đã được kích hoạt cho một máy tính khác!", null);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi xử lý: {ex.Message}", null);
            }
        }

        public static async Task<(bool success, string message)> UpdateGoogleSheetAsync(string key, string googleSheetUrl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(key))
                    return (false, "License key rỗng.");

                string spreadsheetId = AppRuntimeConfig.ExtractSpreadsheetId(googleSheetUrl);
                if (string.IsNullOrWhiteSpace(spreadsheetId))
                    return (false, "Link Google Sheet không hợp lệ.");

                await PatchLicenseFieldsAsync(key, new
                {
                    googleSheetUrl = googleSheetUrl.Trim(),
                    dataSpreadsheetId = spreadsheetId
                });

                RuntimeConfigManager.ApplyLicenseConfig(new AppRuntimeConfig
                {
                    GoogleSheetUrl = googleSheetUrl.Trim(),
                    DataSpreadsheetId = spreadsheetId
                });

                return (true, "Đã cập nhật Google Sheet cho license.");
            }
            catch (Exception ex)
            {
                return (false, $"Không cập nhật được Google Sheet: {ex.Message}");
            }
        }

        private static async Task PatchLicenseFieldsAsync(string key, object patchData)
        {
            string url = BuildFirebaseUrl($"Licenses/{Uri.EscapeDataString(key)}");
            string json = JsonSerializer.Serialize(patchData);
            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            await client.SendAsync(request);
        }

        private static string BuildFirebaseUrl(string relativePath)
        {
            AppRuntimeConfig config = RuntimeConfigManager.Current;
            string url = config.FirebaseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/') + ".json";
            if (!string.IsNullOrWhiteSpace(config.FirebaseDatabaseSecret))
                url += "?auth=" + Uri.EscapeDataString(config.FirebaseDatabaseSecret);
            return url;
        }
    }
}
