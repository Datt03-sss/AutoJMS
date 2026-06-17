using System;

namespace AutoJMS;

public static class RuntimePolicyApplier
{
    public static void ApplyToSettings(RuntimePolicyDocument policy, AppSettings settings)
    {
        if (policy == null || settings == null)
            return;

        ApplyGoogleSheets(policy, settings);
        ApplyPrint(policy, settings);
        ApplyDebugCapture(policy, settings);

        AppLogger.Info(
            $"[Policy] applied settings source={policy.Source} tier={policy.Tier} " +
            $"googleProvider={settings.GoogleSheetsAccessMode} preflight={settings.EnablePrinterPreflight} " +
            $"autoPrint={settings.PrintDefaultAutoPrint} maxReprint={settings.MaxAutoJmsReprintCount}");
    }

    private static void ApplyGoogleSheets(RuntimePolicyDocument policy, AppSettings settings)
    {
        bool enabled = policy.GoogleSheets.Enabled ??
                       policy.GetFeatureBool("googleSheets.enabled", true);
        if (!enabled)
        {
            settings.GoogleSheetsAccessMode = GoogleSheetsAccessMode.Disabled;
            return;
        }

        string provider = policy.GoogleSheets.Provider;
        if (string.IsNullOrWhiteSpace(provider))
            provider = policy.GetFeatureString("googleSheets.provider", "TokenBroker");

        settings.GoogleSheetsAccessMode = NormalizeGoogleSheetsMode(provider);
        settings.PreferGoogleSheetsTokenBroker =
            settings.GoogleSheetsAccessMode == GoogleSheetsAccessMode.TokenBroker ||
            settings.GoogleSheetsAccessMode == GoogleSheetsAccessMode.Auto;
        settings.AllowLegacyLocalServiceAccountFallback =
            policy.GoogleSheets.AllowLegacyLocalFallback ?? settings.AllowLegacyLocalServiceAccountFallback;
        settings.AllowLegacyLocalServiceAccount = settings.AllowLegacyLocalServiceAccountFallback;
        settings.ArchiveLegacyServiceAccountAfterBrokerSuccess =
            policy.GoogleSheets.ArchiveLocalSecretAfterSuccess ?? settings.ArchiveLegacyServiceAccountAfterBrokerSuccess;
        settings.DeleteLegacyServiceAccountAfterBrokerSuccess =
            policy.GoogleSheets.DeleteLocalSecretAfterBrokerSuccess ?? settings.DeleteLegacyServiceAccountAfterBrokerSuccess;
        settings.GoogleSheetsTokenRefreshSkewMinutes =
            Math.Max(1, policy.GoogleSheets.TokenRefreshSkewMinutes ?? settings.GoogleSheetsTokenRefreshSkewMinutes);
    }

    private static GoogleSheetsAccessMode NormalizeGoogleSheetsMode(string provider)
    {
        string value = (provider ?? "").Trim();
        if (value.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            return GoogleSheetsAccessMode.Disabled;
        if (value.Equals("LegacyLocalOnly", StringComparison.OrdinalIgnoreCase))
            return GoogleSheetsAccessMode.LegacyLocalOnly;
        if (value.Equals("ServerOnly", StringComparison.OrdinalIgnoreCase))
            return GoogleSheetsAccessMode.ServerOnly;
        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return GoogleSheetsAccessMode.Auto;

        // SupabaseTokenBroker is the target architecture name; current client
        // implementation uses the token broker provider class.
        return GoogleSheetsAccessMode.TokenBroker;
    }

    private static void ApplyPrint(RuntimePolicyDocument policy, AppSettings settings)
    {
        settings.PrintDefaultAutoPrint =
            policy.Print.DefaultAutoPrint ??
            policy.GetFeatureBool("print.defaultAutoPrint", settings.PrintDefaultAutoPrint);
        settings.EnablePrinterPreflight =
            policy.Print.EnablePrinterPreflight ??
            policy.GetFeatureBool("print.enablePrinterPreflight", settings.EnablePrinterPreflight);
        settings.MaxAutoJmsReprintCount =
            Math.Max(0, policy.Print.MaxReprintCount ?? settings.MaxAutoJmsReprintCount);

        if (policy.Print.PaperWidthInch.HasValue && policy.Print.PaperWidthInch.Value > 0)
            settings.PrintPaperWidthInch = policy.Print.PaperWidthInch.Value;
        if (policy.Print.PaperHeightInch.HasValue && policy.Print.PaperHeightInch.Value > 0)
            settings.PrintPaperHeightInch = policy.Print.PaperHeightInch.Value;

        settings.BlockWhenQueueHasErrorJob =
            policy.Print.BlockWhenQueueHasErrorJob ?? settings.BlockWhenQueueHasErrorJob;
        settings.BlockWhenPrinterPaused =
            policy.Print.BlockWhenPrinterPaused ?? settings.BlockWhenPrinterPaused;
        settings.BlockWhenPrinterOffline =
            policy.Print.BlockWhenPrinterOffline ?? settings.BlockWhenPrinterOffline;
    }

    private static void ApplyDebugCapture(RuntimePolicyDocument policy, AppSettings settings)
    {
        settings.DebugCaptureEnabled =
            policy.DebugCapture.Enabled ??
            policy.GetFeatureBool("debugCapture.enabled", settings.DebugCaptureEnabled);

        if (policy.DebugCapture.SlowApiThresholdMs.HasValue && policy.DebugCapture.SlowApiThresholdMs.Value > 0)
            settings.DebugCaptureSlowApiThresholdMs = policy.DebugCapture.SlowApiThresholdMs.Value;
    }
}
