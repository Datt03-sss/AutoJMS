using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AutoJMS
{
    public class UserSettingsService
    {
        private static readonly string SettingsPath = AppPaths.UserSettingsFile;
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private AppSettings _settings;
        private readonly object _lock = new();

        public AppSettings Current
        {
            get { lock (_lock) return _settings ?? (_settings = LoadFromDisk()); }
        }

        public UserSettingsService()
        {
            _settings = LoadFromDisk();
        }

        private AppSettings LoadFromDisk()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                    if (loaded != null) return Normalize(loaded);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"UserSettingsService: load failed, using defaults. {ex.Message}");
            }
            return Normalize(new AppSettings());
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    SettingsManager.Save(Normalize(_settings));
                }
                catch (Exception ex)
                {
                    AppLogger.Error("UserSettingsService: save failed", ex);
                }
            }
        }

        public void Save(AppSettings settings)
        {
            lock (_lock)
            {
                _settings = Normalize(settings);
            }
            Save();
        }

        private static AppSettings Normalize(AppSettings s)
        {
            if (string.IsNullOrWhiteSpace(s.Theme) || !(s.Theme == "Light" || s.Theme == "Red" || s.Theme == "Dark"))
                s.Theme = "Light";

            if (string.IsNullOrWhiteSpace(s.DefaultUrl))
                s.DefaultUrl = AppConfig.Current?.JmsBaseUrl?.TrimEnd('/') ?? "https://jms.jtexpress.vn";
            if (s.PaperWidth <= 0) s.PaperWidth = 762;
            if (s.PaperHeight <= 0) s.PaperHeight = 762;
            if (s.PrintPaperWidthInch <= 0) s.PrintPaperWidthInch = 3m;
            if (s.PrintPaperHeightInch <= 0) s.PrintPaperHeightInch = 3m;
            if (s.MaxAutoJmsReprintCount <= 0) s.MaxAutoJmsReprintCount = 5;
            s.PrinterPaperMode = string.IsNullOrWhiteSpace(s.PrinterPaperMode) ? "AutoJMS_3x3" : s.PrinterPaperMode.Trim();
            s.PrinterOriginalPaperName = (s.PrinterOriginalPaperName ?? "").Trim();
            s.PrinterOriginalSettingsBackup = (s.PrinterOriginalSettingsBackup ?? "").Trim();
            if (s.DefaultRowCount <= 0) s.DefaultRowCount = 1;
            if (s.ZoomFactor <= 0) s.ZoomFactor = 1.0;
            s.MiddleCode = (s.MiddleCode ?? "").Trim().ToUpperInvariant();
            s.MiddleCodeSegment2 = (s.MiddleCodeSegment2 ?? "").Trim().ToUpperInvariant();
            s.MiddleCodeAliases = (s.MiddleCodeAliases ?? new System.Collections.Generic.List<string>())
                .Append(s.MiddleCode)
                .Select(x => (x ?? "").Trim().ToUpperInvariant())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            s.LegacyServiceAccountPath = (s.LegacyServiceAccountPath ?? "").Trim();
            s.EncryptedServiceAccountPath = string.IsNullOrWhiteSpace(s.EncryptedServiceAccountPath)
                ? "appdata/secrets/service_account.sec"
                : s.EncryptedServiceAccountPath.Trim();
            if (s.GoogleSheetsTokenRefreshSkewMinutes <= 0)
                s.GoogleSheetsTokenRefreshSkewMinutes = 5;
            if (!s.AllowLegacyLocalServiceAccountFallback)
                s.AllowLegacyLocalServiceAccount = false;
            return s;
        }
    }
}
