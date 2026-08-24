using System.IO;
using System.Linq;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Bảo vệ hai thứ quanh <c>tier-definitions.json</c>:
///
/// <list type="number">
/// <item>File thật được ship cùng app phải parse được và phải khai đúng
/// BASE/ULTRA. File này đọc bằng <see cref="TierDefinitions.LoadFromFile"/> lúc
/// chạy, không ai kiểm tra lúc build — một dấu phẩy sai là ULTRA rơi về
/// <c>DefaultBase</c> trong im lặng.</item>
///
/// <item><c>MergeWithParent</c> phải mang MỌI field của <c>TierConfig</c> sang.
/// Bản cũ chỉ copy Tabs/Forms/Modules, nên <c>displayName</c> và
/// <c>description</c> của ULTRA (tier có <c>inherits</c>) bị xoá sạch dù JSON
/// khai đầy đủ.</item>
/// </list>
///
/// Lưu ý: tier-definitions.json KHÔNG phải thẩm quyền tier. Nó là catalogue +
/// fallback; capability thật nằm ở runtime-policy và chỉ được THU HẸP quyền.
/// Xem <see cref="TierEntitlementTests"/>.
/// </summary>
public sealed class TierDefinitionsSchemaTests
{
    // ------------------------------------------------------------ merge

    private const string InheritingJson = """
    {
      "schemaVersion": 2,
      "tiers": {
        "BASE": {
          "displayName": "Base name",
          "description": "Base description",
          "tabs": ["HOME", "ABOUT"],
          "forms": []
        },
        "ULTRA": {
          "inherits": "BASE",
          "displayName": "Ultra name",
          "description": "Ultra description",
          "tabs": ["HOME", "DKCH", "ABOUT"],
          "forms": [ { "name": "FULLSTACK_OPERATION" } ]
        }
      }
    }
    """;

    [Fact]
    public void Tier_ke_thua_giu_duoc_displayName_va_description_cua_chinh_no()
    {
        var defs = TierDefinitions.FromJson(InheritingJson);

        var ultra = defs.GetTier("ULTRA");

        Assert.Equal("Ultra name", ultra.DisplayName);
        Assert.Equal("Ultra description", ultra.Description);
        Assert.Null(ultra.Inherits);
    }

    [Fact]
    public void Tier_ke_thua_lay_field_cua_cha_khi_chinh_no_khong_khai()
    {
        const string json = """
        {
          "tiers": {
            "BASE":  { "displayName": "Base name", "tabs": ["HOME"], "forms": [] },
            "ULTRA": { "inherits": "BASE" }
          }
        }
        """;

        var ultra = TierDefinitions.FromJson(json).GetTier("ULTRA");

        Assert.Equal("Base name", ultra.DisplayName);
        Assert.Equal(new[] { "HOME" }, ultra.Tabs);
    }

    [Fact]
    public void Tier_ke_thua_uu_tien_tabs_va_forms_cua_chinh_no()
    {
        var ultra = TierDefinitions.FromJson(InheritingJson).GetTier("ULTRA");

        Assert.Equal(new[] { "HOME", "DKCH", "ABOUT" }, ultra.Tabs);
        Assert.Single(ultra.Forms);
        Assert.Equal("FULLSTACK_OPERATION", ultra.Forms[0].Name);
    }

    [Fact]
    public void Tier_la_khong_biet_thi_roi_ve_BASE()
    {
        var defs = TierDefinitions.FromJson(InheritingJson);

        var unknown = defs.GetTier("PLATINUM");

        Assert.Equal("Base name", unknown.DisplayName);
        Assert.Empty(unknown.Forms);
    }

    [Fact]
    public void Json_rong_hoac_hong_roi_ve_DefaultBase_thay_vi_nem_loi()
    {
        foreach (var json in new[] { null, "", "   ", "{ this is not json" })
        {
            var tier = TierDefinitions.FromJson(json).GetTier("ULTRA");

            Assert.Empty(tier.Forms);
            Assert.Contains("ABOUT", tier.Tabs);
        }
    }

    // ------------------------------------------------- file thật được ship

    private static TierDefinitions ShippedDefinitions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "tier-definitions.json");
        Assert.True(File.Exists(path), $"tier-definitions.json phải được copy ra output: {path}");
        return TierDefinitions.FromJson(File.ReadAllText(path));
    }

    [Fact]
    public void File_duoc_ship_khai_dung_hai_tier_BASE_va_ULTRA()
    {
        var defs = ShippedDefinitions();

        Assert.Equal(new[] { "BASE", "ULTRA" }, defs.Tiers.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void File_duoc_ship_khong_cho_BASE_chay_form_nen_nao()
    {
        var basePlan = ShippedDefinitions().GetTier("BASE");

        Assert.Empty(basePlan.Forms);
        Assert.False(string.IsNullOrWhiteSpace(basePlan.DisplayName));
    }

    [Fact]
    public void File_duoc_ship_cho_ULTRA_chay_FullStackOperation()
    {
        var defs = ShippedDefinitions();

        Assert.True(defs.HasForm("ULTRA", "FULLSTACK_OPERATION"));
        Assert.False(defs.HasForm("BASE", "FULLSTACK_OPERATION"));

        var form = defs.GetForms("ULTRA").Single();
        Assert.Equal("VISIBLE_FORM", form.Type);
        Assert.Equal("AFTER_MAINFORM_SHOWN", form.Launch);
    }

    [Fact]
    public void File_duoc_ship_giu_ABOUT_o_cuoi_moi_tier()
    {
        var defs = ShippedDefinitions();

        foreach (var tier in new[] { "BASE", "ULTRA" })
        {
            var tabs = defs.GetTier(tier).Tabs;
            Assert.Equal("ABOUT", tabs[^1]);
        }
    }
}
