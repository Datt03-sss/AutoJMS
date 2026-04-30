using Google; // Bắt buộc phải có để bắt lỗi GoogleApiException
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms; // Thêm thư viện này để dùng MessageBox

namespace AutoJMS
{
    public static class GoogleSheetService
    {
        public static string DATA_SPREADSHEET_ID => RuntimeConfigManager.Current.DataSpreadsheetId;
        public static string LICENSE_SPREADSHEET_ID => RuntimeConfigManager.Current.LicenseSpreadsheetId;

        private static SheetsService Service;

        public static void InitService()
        {
            if (Service != null) return;

            try
            {
                GoogleCredential credential;
                string serviceAccountJson = RuntimeConfigManager.Current.GoogleServiceAccountJson;

                if (!string.IsNullOrWhiteSpace(serviceAccountJson))
                {
                    credential = GoogleCredential.FromJson(serviceAccountJson)
                        .CreateScoped(SheetsService.Scope.Spreadsheets);
                }
                else
                {
                    string credentialPath = ResolveCredentialPath();
                    if (!File.Exists(credentialPath))
                        throw new FileNotFoundException($"Không tìm thấy tệp xác thực Google Sheet tại đường dẫn: {credentialPath}");

                    using var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read);
                    credential = GoogleCredential.FromStream(stream)
                        .CreateScoped(SheetsService.Scope.Spreadsheets);
                }

                Service = new SheetsService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "AutoJMS"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khởi tạo xác thực Google:\n{ex.Message}", "Lỗi InitService", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw; // Văng lỗi ra ngoài để app dừng lại
            }
        }

        public static void ResetService()
        {
            if (Service is IDisposable disposable)
                disposable.Dispose();
            Service = null;
        }

        private static string ResolveCredentialPath()
        {
            string configuredPath = RuntimeConfigManager.Current.GoogleCredentialPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
                configuredPath = "service_account.json"; // Đảm bảo tên file này khớp với máy bạn

            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath);
        }

        public static bool IsConfigValid()
        {
            string spreadId = DATA_SPREADSHEET_ID;

            // In thử ra màn hình xem ID đang là cái gì
            MessageBox.Show($"ID Sheet đang là: '{spreadId}'");

            bool hasCredential = !string.IsNullOrWhiteSpace(RuntimeConfigManager.Current.GoogleServiceAccountJson)
                || File.Exists(ResolveCredentialPath());

            return hasCredential && !string.IsNullOrWhiteSpace(spreadId);
        }

        public static List<string> ReadColumnBySpreadsheetId(string spreadsheetId, string sheetName, int columnIndex)
        {
            string spreadId = DATA_SPREADSHEET_ID;
            try
            {
                if (Service == null) InitService();

                string colLetter = GetColumnLetter(columnIndex);
                string range = $"{sheetName}!{colLetter}2:{colLetter}";

                var request = Service.Spreadsheets.Values.Get(spreadsheetId, range);
                var response = request.Execute();
                var values = response.Values;

                List<string> result = new List<string>();
                if (values != null)
                {
                    foreach (var row in values)
                    {
                        if (row.Count > 0 && !string.IsNullOrWhiteSpace(row[0]?.ToString()))
                            result.Add(row[0].ToString().Trim());
                    }
                }
                return result;
            }
            
            catch (GoogleApiException ex)
            {
                MessageBox.Show($"Google từ chối yêu cầu Đọc Cột '({spreadId}'):\n\nMã lỗi: {ex.HttpStatusCode}\nChi tiết: {ex.Error?.Message ?? ex.Message}", "Lỗi Google API", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new List<string>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi hệ thống (ReadColumn):\n{ex.Message}", "Lỗi Code", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<string>();
            }
        }

        // --- Bổ sung vào class GoogleSheetService ---
        public static void ClearSheet(string spreadsheetId, string range)
        {
            try
            {
                if (Service == null) InitService();
                var clearRequest = Service.Spreadsheets.Values.Clear(new ClearValuesRequest(), spreadsheetId, range);
                clearRequest.Execute();
            }
            catch (GoogleApiException ex)
            {
                MessageBox.Show($"Google từ chối xóa sheet (ClearSheet):\n\nMã lỗi: {ex.HttpStatusCode}\nChi tiết: {ex.Error?.Message ?? ex.Message}", "Lỗi Google API", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Lỗi ClearSheet"); }
        }

        // 1. Ghi dữ liệu vào sheet BUMP
        public static void UpdateBumpSheet(IList<IList<object>> values, string spreadsheetId, string range)
        {
            try
            {
                if (Service == null) InitService();
                var valueRange = new ValueRange { Values = values };
                var updateRequest = Service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                updateRequest.Execute();
            }
            catch (GoogleApiException ex)
            {
                MessageBox.Show($"Google từ chối ghi dữ liệu (UpdateBumpSheet):\n\nMã lỗi: {ex.HttpStatusCode}\nChi tiết: {ex.Error?.Message ?? ex.Message}", "Lỗi Google API", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Lỗi UpdateBumpSheet"); }
        }

        // 2. Đọc dữ liệu từ sheet COMMAND_QUEUE
        public static IList<IList<object>> ReadRange(string spreadsheetId, string range)
        {
            try
            {
                if (Service == null) InitService();
                var request = Service.Spreadsheets.Values.Get(spreadsheetId, range);
                var response = request.Execute();
                return response.Values;
            }
            catch (GoogleApiException ex)
            {
                MessageBox.Show($"Google từ chối đọc vùng dữ liệu (ReadRange):\n\nMã lỗi: {ex.HttpStatusCode}\nChi tiết: {ex.Error?.Message ?? ex.Message}", "Lỗi Google API", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Lỗi ReadRange");
                return null;
            }
        }

        // 3. Cập nhật trạng thái từng ô trong COMMAND_QUEUE (VD: PENDING -> DONE)
        public static void UpdateCell(string spreadsheetId, string range, string value)
        {
            try
            {
                if (Service == null) InitService();
                var oblist = new List<object>() { value };
                var valueRange = new ValueRange { Values = new List<IList<object>> { oblist } };
                var updateRequest = Service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                updateRequest.Execute();
            }
            catch (GoogleApiException ex)
            {
                MessageBox.Show($"Google từ chối cập nhật ô (UpdateCell):\n\nMã lỗi: {ex.HttpStatusCode}\nChi tiết: {ex.Error?.Message ?? ex.Message}", "Lỗi Google API", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Lỗi UpdateCell"); }
        }

        public static List<string> ReadColumn(string sheetName, int colIndex)
        {
            return ReadColumnBySpreadsheetId(DATA_SPREADSHEET_ID, sheetName, colIndex);
        }

        private static string GetColumnLetter(int col)
        {
            int dividend = col;
            string columnName = String.Empty;
            int modulo;
            while (dividend > 0)
            {
                modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo).ToString() + columnName;
                dividend = (int)((dividend - modulo) / 26);
            }
            return columnName;
        }
    }
}