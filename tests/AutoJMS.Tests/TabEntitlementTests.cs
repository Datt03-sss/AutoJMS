using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// <see cref="TierRuntimePolicy.Current"/> là static toàn tiến trình, nên mọi test đụng
/// tới nó phải chạy tuần tự — cùng một collection.
/// </summary>
[CollectionDefinition("TierPolicy", DisableParallelization = true)]
public sealed class TierPolicyCollection { }

/// <summary>
/// Phân quyền tab theo tier.
///
/// Bug từng có: <c>TabManager.ApplyTier</c> đặt <c>TabPage.Visible = false</c>, mà trong
/// WinForms đó là no-op — tab vẫn nằm nguyên trong <c>TabControl</c>. Và hai cờ
/// <c>AllowManualTracking</c>/<c>AllowManualPrint</c> được tính ra rồi bỏ đó, không nơi
/// nào đọc, nên kill switch <c>tabs.tracking</c>/<c>tabs.print</c> của runtime policy
/// hoàn toàn vô tác dụng.
/// </summary>
[Collection("TierPolicy")]
public sealed class TabEntitlementTests
{
    private static readonly string[] TabNames = { "HOME", "DKCH", "TRACKING", "PRINT", "ABOUT" };

    private static TierDefinitions AllTabs(string tier)
        => TierDefinitions.FromJson(
            "{\"schemaVersion\":1,\"tiers\":{\"" + tier + "\":{" +
            "\"tabs\":[\"HOME\",\"DKCH\",\"TRACKING\",\"PRINT\",\"ABOUT\"],\"forms\":[]}}}");

    /// <summary>Dựng TabControl 5 tab đúng thứ tự thiết kế, đã đăng ký với TabManager.</summary>
    private static (TabControl control, TabManager manager) NewTabs()
    {
        var control = new TabControl();
        var manager = new TabManager(control);
        foreach (var name in TabNames)
        {
            var page = new TabPage(name) { Name = "tab" + name };
            control.TabPages.Add(page);
            manager.RegisterTab(name, page);
        }
        return (control, manager);
    }

    private static string[] VisibleTabs(TabControl control)
        => control.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray();

    /// <summary>Policy tắt tường minh các tab được liệt kê, các tab khác để nguyên.</summary>
    private static void ApplyPolicy(string licenseTier, params string[] disabledFeatures)
    {
        var doc = new RuntimePolicyDocument { Source = "test" };
        foreach (var key in disabledFeatures)
            doc.Features[key] = JsonSerializer.SerializeToElement(false);
        TierRuntimePolicy.Resolve(doc, licenseTier);
    }

    [Fact]
    public void Tab_bi_policy_tat_thi_bi_go_han_khoi_TabControl()
    {
        ApplyPolicy("ULTRA", "tabs.tracking");
        var (control, manager) = NewTabs();

        manager.ApplyTier("ULTRA", AllTabs("ULTRA"));

        Assert.Equal(new[] { "HOME", "DKCH", "PRINT", "ABOUT" }, VisibleTabs(control));
        Assert.False(manager.IsTabAllowed("TRACKING"));
    }

    [Fact]
    public void Khong_tat_gi_thi_giu_du_5_tab_dung_thu_tu()
    {
        ApplyPolicy("BASE");
        var (control, manager) = NewTabs();

        manager.ApplyTier("BASE", AllTabs("BASE"));

        Assert.Equal(TabNames, VisibleTabs(control));
    }

    [Fact]
    public void Tab_hien_lai_dung_vi_tri_thiet_ke_va_ABOUT_van_cuoi_cung()
    {
        var (control, manager) = NewTabs();

        ApplyPolicy("ULTRA", "tabs.tracking", "tabs.print");
        manager.ApplyTier("ULTRA", AllTabs("ULTRA"));
        Assert.Equal(new[] { "HOME", "DKCH", "ABOUT" }, VisibleTabs(control));

        ApplyPolicy("ULTRA");
        manager.ApplyTier("ULTRA", AllTabs("ULTRA"));

        Assert.Equal(TabNames, VisibleTabs(control));
        Assert.Equal("ABOUT", VisibleTabs(control).Last());
    }

    [Fact]
    public void Tier_definitions_khong_mo_duoc_tab_ma_entitlement_da_tat()
    {
        // File tier-definitions.json ghi được bởi người dùng: liệt kê đủ 5 tab nhưng
        // entitlement đã tắt PRINT thì tab PRINT vẫn phải biến mất.
        ApplyPolicy("ULTRA", "tabs.print");
        var (control, manager) = NewTabs();

        manager.ApplyTier("ULTRA", AllTabs("ULTRA"));

        Assert.DoesNotContain("PRINT", VisibleTabs(control));
    }

    [Fact]
    public void Tab_khong_co_trong_danh_sach_cua_tier_thi_bi_go()
    {
        ApplyPolicy("BASE");
        var (control, manager) = NewTabs();

        var trimmed = TierDefinitions.FromJson(
            "{\"schemaVersion\":1,\"tiers\":{\"BASE\":{" +
            "\"tabs\":[\"HOME\",\"ABOUT\"],\"forms\":[]}}}");
        manager.ApplyTier("BASE", trimmed);

        Assert.Equal(new[] { "HOME", "ABOUT" }, VisibleTabs(control));
    }
}
