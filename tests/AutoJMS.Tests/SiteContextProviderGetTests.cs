using Xunit;

namespace AutoJMS.Tests;

// Các test này chạm state static toàn cục (AppConfig.Current). xunit chạy tuần tự
// trong cùng một class nên không cần khoá thêm, nhưng phải trả state về cũ.
[Collection("SiteContext")]
public class SiteContextProviderGetTests : IDisposable
{
    private readonly string _originalActionSiteCode;

    public SiteContextProviderGetTests()
    {
        _originalActionSiteCode = AppConfig.Current.ActionSiteCode;
    }

    public void Dispose()
    {
        AppConfig.Current.ActionSiteCode = _originalActionSiteCode;
        SiteContextProvider.InvalidateCache();
    }

    private static string WithRuntimeCode(string code)
    {
        AppConfig.Current.ActionSiteCode = code;
        SiteContextProvider.InvalidateCache();
        return SiteContextProvider.Get();
    }

    [Fact]
    public void Get_NormalizesRuntimeCode()
    {
        Assert.Equal("214A99", WithRuntimeCode("  214a99 "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0000")]
    public void Get_TreatsUnsetSentinelsAsEmpty(string raw)
    {
        // "0000" là sentinel "chưa cấu hình" mà DataHubSyncService vẫn luôn lọc.
        // Luật đó giờ nằm ở Get() nên áp dụng cho mọi caller.
        AppConfig.Current.ActionSiteCode = raw;
        SiteContextProvider.InvalidateCache();
        Assert.NotEqual("0000", SiteContextProvider.Get());
    }

    [Fact]
    public void Get_CachesUntilInvalidated()
    {
        Assert.Equal("214A77", WithRuntimeCode("214A77"));

        AppConfig.Current.ActionSiteCode = "214A88";
        Assert.Equal("214A77", SiteContextProvider.Get());   // vẫn là giá trị cache

        SiteContextProvider.InvalidateCache();
        Assert.Equal("214A88", SiteContextProvider.Get());
    }
}
