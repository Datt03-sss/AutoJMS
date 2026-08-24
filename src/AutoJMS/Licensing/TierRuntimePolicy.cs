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
        /// Policy đang có hiệu lực của tiến trình này. Mặc định fail-closed (BASE)
        /// cho tới khi Resolve() chạy, nên mọi code hỏi TRƯỚC khi license được xác
        /// thực đều chỉ nhận quyền BASE.
        ///
        /// Dùng cho gate ở tầng service (ví dụ FullStackOperation tự kiểm tra):
        /// gate ở UI chỉ bảo vệ trải nghiệm, không bảo vệ an ninh.
        /// </summary>
        public static TierRuntimePolicy Current { get; private set; } =
            new TierRuntimePolicy("BASE",
                inventorySync: false,
                databaseTracking: false,
                backgroundAutoSync: false,
                fullStack: false,
                manualTracking: true,
                manualPrint: true);

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

            Current = policy;
            return policy;
        }

        /// <summary>
        /// Kết hợp entitlement của license với runtime policy lấy từ DataHub.
        ///
        /// Nguyên tắc bảo mật: <b>license tier là thẩm quyền bất biến</b>. Runtime
        /// policy chỉ được phép THU HẸP quyền (true -&gt; false) — kill switch, bảo trì,
        /// hạ cấp tính năng tạm thời. Nó KHÔNG BAO GIỜ được nâng quyền (false -&gt; true),
        /// vì như vậy một file JSON trên DataHub sẽ biến license BASE thành ULTRA mà
        /// không cần đổi license.
        ///
        /// Bảng hệ quả cho FullStack:
        ///   BASE  + policy true  =&gt; false
        ///   BASE  + policy false =&gt; false
        ///   ULTRA + policy true  =&gt; true
        ///   ULTRA + policy false =&gt; false
        /// </summary>
        /// <param name="policy">Runtime policy từ DataHub. Chỉ mang tính hạn chế.</param>
        /// <param name="licenseTier">Tier do license server cấp. Đây là thẩm quyền, không phải gợi ý.</param>
        public static TierRuntimePolicy Resolve(RuntimePolicyDocument policy, string licenseTier = "BASE")
        {
            // Trần quyền mà license này được hưởng.
            var entitlement = Resolve(licenseTier);
            if (policy == null)
                return entitlement;

            // Mỗi cờ là phép AND với entitlement. Cờ KHUYẾT trong policy nghĩa là
            // "không có hạn chế" nên default = true (giữ nguyên entitlement); chỉ giá
            // trị false tường minh mới thu hẹp được quyền.
            bool fullStack = entitlement.EnableFullStackOperation
                             && (policy.FullStack.Enabled ??
                                 policy.GetFeatureBool("forms.fullStackOperation", true));
            bool backgroundSync = entitlement.EnableBackgroundAutoSync
                                  && (policy.FullStack.BackgroundSync ??
                                      policy.GetFeatureBool("fullStack.backgroundSync", true));
            bool inventorySync = entitlement.EnableStartupInventorySync
                                 && policy.GetFeatureBool("fullStack.inventorySync", true);
            bool databaseTracking = entitlement.EnableStartupDatabaseTracking
                                    && policy.GetFeatureBool("fullStack.databaseTracking", true);
            bool manualTracking = entitlement.AllowManualTracking
                                  && policy.GetFeatureBool("tabs.tracking", true);
            bool manualPrint = entitlement.AllowManualPrint
                               && policy.GetFeatureBool("tabs.print", true);

            if (!string.Equals(policy.Tier, entitlement.Tier, StringComparison.OrdinalIgnoreCase))
                AppLogger.Warning(
                    $"[Tier] policy tier={policy.Tier} khác license tier={entitlement.Tier}; " +
                    $"chỉ license tier được dùng làm thẩm quyền (source={policy.Source}).");

            // Tier KHÔNG suy diễn từ các cờ tính năng — nó là tier của license.
            var resolved = new TierRuntimePolicy(
                entitlement.Tier,
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

            Current = resolved;
            return resolved;
        }
    }
}
