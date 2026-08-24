using System.Text.Json;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Kiểm chứng bất biến an ninh quan trọng nhất của phần license:
/// <b>license tier là thẩm quyền bất biến, runtime policy chỉ được THU HẸP quyền</b>.
///
/// Bug từng có: <c>TierRuntimePolicy.Resolve(RuntimePolicyDocument, ...)</c> suy ra tier
/// TỪ các cờ tính năng trong policy, nên chỉ cần một file JSON trên DataHub (hoặc một
/// file cache cũ của máy từng chạy ULTRA) khai <c>fullStack.enabled = true</c> là license
/// BASE mở được cửa sổ FullStackOperation mà không cần đổi license ở Firebase.
///
/// Các test dưới đây chạy trên logic thuần, không cần tier-definitions.json trên đĩa:
/// <c>Resolve("ULTRA")</c> nhận ra ULTRA qua chính tên tier khi không đọc được file.
/// </summary>
public sealed class TierEntitlementTests
{
    /// <summary>Policy rỗng: không khai cờ nào, tức là "không có hạn chế nào".</summary>
    private static RuntimePolicyDocument EmptyPolicy(string declaredTier = "")
        => new RuntimePolicyDocument { Tier = declaredTier, Source = "test" };

    private static RuntimePolicyDocument Policy(string declaredTier, bool fullStack)
    {
        var doc = EmptyPolicy(declaredTier);
        doc.FullStack.Enabled = fullStack;
        doc.FullStack.BackgroundSync = fullStack;
        doc.Features["forms.fullStackOperation"] = JsonSerializer.SerializeToElement(fullStack);
        doc.Features["fullStack.backgroundSync"] = JsonSerializer.SerializeToElement(fullStack);
        doc.Features["fullStack.inventorySync"] = JsonSerializer.SerializeToElement(fullStack);
        doc.Features["fullStack.databaseTracking"] = JsonSerializer.SerializeToElement(fullStack);
        return doc;
    }

    // ---------------------------------------------------------------- BASE

    [Fact]
    public void Base_license_khong_the_bat_fullstack_qua_policy()
    {
        var resolved = TierRuntimePolicy.Resolve(Policy("BASE", fullStack: true), "BASE");

        Assert.Equal("BASE", resolved.Tier);
        Assert.False(resolved.EnableFullStackOperation);
        Assert.False(resolved.EnableBackgroundAutoSync);
        Assert.False(resolved.EnableStartupInventorySync);
        Assert.False(resolved.EnableStartupDatabaseTracking);
    }

    [Fact]
    public void Base_license_khong_bi_nang_quyen_boi_policy_khai_tier_ULTRA()
    {
        // Đây chính là đường leo quyền cũ: document tự nhận là ULTRA.
        var resolved = TierRuntimePolicy.Resolve(Policy("ULTRA", fullStack: true), "BASE");

        Assert.Equal("BASE", resolved.Tier);
        Assert.False(resolved.EnableFullStackOperation);
    }

    [Fact]
    public void Base_license_voi_policy_null_thi_khong_co_fullstack()
    {
        var resolved = TierRuntimePolicy.Resolve(null, "BASE");

        Assert.Equal("BASE", resolved.Tier);
        Assert.False(resolved.EnableFullStackOperation);
    }

    [Fact]
    public void Base_license_van_giu_tracking_va_print_thu_cong()
    {
        var resolved = TierRuntimePolicy.Resolve(EmptyPolicy(), "BASE");

        Assert.True(resolved.AllowManualTracking);
        Assert.True(resolved.AllowManualPrint);
    }

    // --------------------------------------------------------------- ULTRA

    [Fact]
    public void Ultra_license_voi_policy_cho_phep_thi_bat_fullstack()
    {
        var resolved = TierRuntimePolicy.Resolve(Policy("ULTRA", fullStack: true), "ULTRA");

        Assert.Equal("ULTRA", resolved.Tier);
        Assert.True(resolved.EnableFullStackOperation);
        Assert.True(resolved.EnableBackgroundAutoSync);
        Assert.True(resolved.EnableStartupInventorySync);
        Assert.True(resolved.EnableStartupDatabaseTracking);
    }

    [Fact]
    public void Ultra_license_bi_policy_thu_hep_thi_tat_fullstack()
    {
        // Kill switch: policy tắt được tính năng của ULTRA (true -> false).
        var resolved = TierRuntimePolicy.Resolve(Policy("ULTRA", fullStack: false), "ULTRA");

        Assert.Equal("ULTRA", resolved.Tier);
        Assert.False(resolved.EnableFullStackOperation);
        Assert.False(resolved.EnableBackgroundAutoSync);
    }

    [Fact]
    public void Ultra_license_voi_policy_khong_khai_gi_thi_giu_nguyen_entitlement()
    {
        // Cờ khuyết nghĩa là "không có hạn chế", không phải "bị cấm".
        var resolved = TierRuntimePolicy.Resolve(EmptyPolicy(), "ULTRA");

        Assert.Equal("ULTRA", resolved.Tier);
        Assert.True(resolved.EnableFullStackOperation);
    }

    [Fact]
    public void Ultra_license_khong_bi_ha_cap_boi_policy_khai_tier_BASE()
    {
        var resolved = TierRuntimePolicy.Resolve(EmptyPolicy("BASE"), "ULTRA");

        Assert.Equal("ULTRA", resolved.Tier);
        Assert.True(resolved.EnableFullStackOperation);
    }

    // ------------------------------------------------------- SafeDefault

    [Fact]
    public void SafeDefault_luon_tat_fullstack_ke_ca_khi_license_la_ULTRA()
    {
        // Mất mạng, không có cache: SafeDefault tắt tường minh nên fail-closed.
        var resolved = TierRuntimePolicy.Resolve(
            RuntimePolicyDocument.SafeDefault("BASE", "safe-default"), "ULTRA");

        Assert.Equal("ULTRA", resolved.Tier);
        Assert.False(resolved.EnableFullStackOperation);
        Assert.True(resolved.AllowManualTracking);
        Assert.True(resolved.AllowManualPrint);
    }

    // ------------------------------------------------- thu hẹp tab thủ công

    [Fact]
    public void Policy_thu_hep_duoc_tab_tracking_va_print()
    {
        var doc = EmptyPolicy();
        doc.Features["tabs.tracking"] = JsonSerializer.SerializeToElement(false);
        doc.Features["tabs.print"] = JsonSerializer.SerializeToElement(false);

        var resolved = TierRuntimePolicy.Resolve(doc, "ULTRA");

        Assert.False(resolved.AllowManualTracking);
        Assert.False(resolved.AllowManualPrint);
    }

    // ------------------------------------------------------------- Current

    [Fact]
    public void Resolve_cong_bo_ket_qua_ra_Current()
    {
        var resolved = TierRuntimePolicy.Resolve(Policy("BASE", fullStack: true), "BASE");
        Assert.Same(resolved, TierRuntimePolicy.Current);
        Assert.False(TierRuntimePolicy.Current.EnableFullStackOperation);

        var ultra = TierRuntimePolicy.Resolve(Policy("ULTRA", fullStack: true), "ULTRA");
        Assert.Same(ultra, TierRuntimePolicy.Current);
        Assert.True(TierRuntimePolicy.Current.EnableFullStackOperation);
    }

    [Fact]
    public void Resolve_theo_ten_tier_cung_cong_bo_ra_Current()
    {
        var resolved = TierRuntimePolicy.Resolve("BASE");
        Assert.Same(resolved, TierRuntimePolicy.Current);
        Assert.False(resolved.EnableFullStackOperation);
    }
}
