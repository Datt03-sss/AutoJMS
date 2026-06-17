using System;

namespace AutoJMS
{
    /// <summary>
    /// Single place that decides what background work a license tier is allowed
    /// to run. Instead of scattering <c>if (tier == "ULTRA")</c> checks across
    /// the codebase, every startup/background entry point asks this policy.
    ///
    /// Rules (per product spec):
    ///   BASE  : manual tracking + manual print only. No auto inventory sync,
    ///           no auto database tracking, no background auto-sync timer,
    ///           no FullStackOperation form.
    ///   ULTRA : everything BASE can do, PLUS background realtime / inventory /
    ///           database tracking and the FullStackOperation form.
    ///
    /// BASE still keeps a fully working TRACKING tab and PRINT tab — those are
    /// driven by explicit user actions, not by this policy.
    /// </summary>
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class TierRuntimePolicy
    {
        public string Tier { get; }

        // Background capabilities (auto, no user action).
        public bool EnableStartupInventorySync { get; }
        public bool EnableStartupDatabaseTracking { get; }
        public bool EnableBackgroundAutoSync { get; }
        public bool EnableFullStackOperation { get; }

        // Manual capabilities (always allowed; user-initiated).
        public bool AllowManualTracking { get; }
        public bool AllowManualPrint { get; }

        private TierRuntimePolicy(
            string tier,
            bool inventorySync,
            bool databaseTracking,
            bool backgroundAutoSync,
            bool fullStack,
            bool manualTracking,
            bool manualPrint)
        {
            Tier = tier;
            EnableStartupInventorySync = inventorySync;
            EnableStartupDatabaseTracking = databaseTracking;
            EnableBackgroundAutoSync = backgroundAutoSync;
            EnableFullStackOperation = fullStack;
            AllowManualTracking = manualTracking;
            AllowManualPrint = manualPrint;
        }

        public bool IsUltra => string.Equals(Tier, "ULTRA", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolve the runtime policy for a tier. ULTRA is identified by the
        /// presence of the FULLSTACK_OPERATION form in tier-definitions.json,
        /// keeping a single source of truth for "what is ULTRA".
        /// </summary>
        public static TierRuntimePolicy Resolve(string tier, TierDefinitions definitions = null)
        {
            string normalized = (tier ?? "BASE").Trim().ToUpperInvariant();

            definitions ??= TierDefinitions.LoadFromFile();
            bool hasFullStack = definitions.HasForm(normalized, "FULLSTACK_OPERATION");

            // ULTRA = explicitly named ULTRA OR granted the FullStack form.
            bool isUltra = hasFullStack || normalized == "ULTRA";

            var policy = isUltra
                ? new TierRuntimePolicy("ULTRA",
                    inventorySync: true,
                    databaseTracking: true,
                    backgroundAutoSync: true,
                    fullStack: true,
                    manualTracking: true,
                    manualPrint: true)
                : new TierRuntimePolicy("BASE",
                    inventorySync: false,
                    databaseTracking: false,
                    backgroundAutoSync: false,
                    fullStack: false,
                    manualTracking: true,
                    manualPrint: true);

            AppLogger.Info(
                $"Tier policy resolved: {policy.Tier} " +
                $"(inventorySync={policy.EnableStartupInventorySync}, " +
                $"databaseTracking={policy.EnableStartupDatabaseTracking}, " +
                $"backgroundAutoSync={policy.EnableBackgroundAutoSync}, " +
                $"fullStack={policy.EnableFullStackOperation}, " +
                $"manualTracking={policy.AllowManualTracking}, " +
                $"manualPrint={policy.AllowManualPrint})");

            return policy;
        }

        public static TierRuntimePolicy Resolve(RuntimePolicyDocument policy, string fallbackTier = "BASE")
        {
            if (policy == null)
                return Resolve(fallbackTier);

            bool fullStack = policy.FullStack.Enabled ??
                             policy.GetFeatureBool("forms.fullStackOperation", false);
            bool backgroundSync = policy.FullStack.BackgroundSync ??
                                  policy.GetFeatureBool("fullStack.backgroundSync", false);
            bool inventorySync = policy.GetFeatureBool("fullStack.inventorySync", backgroundSync);
            bool databaseTracking = policy.GetFeatureBool("fullStack.databaseTracking", backgroundSync);
            bool manualTracking = policy.GetFeatureBool("tabs.tracking", true);
            bool manualPrint = policy.GetFeatureBool("tabs.print", true);

            string effectiveTier = (fullStack || backgroundSync || inventorySync || databaseTracking)
                ? "ULTRA"
                : "BASE";

            var resolved = new TierRuntimePolicy(
                effectiveTier,
                inventorySync: inventorySync,
                databaseTracking: databaseTracking,
                backgroundAutoSync: backgroundSync,
                fullStack: fullStack,
                manualTracking: manualTracking,
                manualPrint: manualPrint);

            AppLogger.Info(
                $"Tier policy resolved from runtime policy: {resolved.Tier} source={policy.Source} " +
                $"(inventorySync={resolved.EnableStartupInventorySync}, " +
                $"databaseTracking={resolved.EnableStartupDatabaseTracking}, " +
                $"backgroundAutoSync={resolved.EnableBackgroundAutoSync}, " +
                $"fullStack={resolved.EnableFullStackOperation}, " +
                $"manualTracking={resolved.AllowManualTracking}, " +
                $"manualPrint={resolved.AllowManualPrint})");

            return resolved;
        }
    }
}
