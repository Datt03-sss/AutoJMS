using Xunit;

namespace AutoJMS.Tests;

// SiteContextProvider giữ cache static. Hai class test cùng đụng vào nó thì phải
// chạy tuần tự, nếu không class này invalidate cache giữa lúc class kia đang đọc.
[CollectionDefinition("SiteContext", DisableParallelization = true)]
public class SiteContextCollection { }

[Collection("SiteContext")]
public class HomeStationDetectionTests : IDisposable
{
    private readonly string _originalActionSiteCode;

    public HomeStationDetectionTests()
    {
        _originalActionSiteCode = AppConfig.Current.ActionSiteCode;
    }

    public void Dispose()
    {
        AppConfig.Current.ActionSiteCode = _originalActionSiteCode;
        SiteContextProvider.InvalidateCache();
    }

    // Tên trạm để rỗng ở mọi test: IsHomeStation chỉ ghi AutoJMS.json khi học được
    // TÊN mới, nên rỗng là cách giữ test không chạm cấu hình thật của máy.
    private static void WithHomeCode(string code)
    {
        AppConfig.Current.ActionSiteCode = code;
        SiteContextProvider.InvalidateCache();
    }

    [Fact]
    public void ReturnsFalse_WhenSiteCodeNotConfigured()
    {
        // Chưa biết trạm nhà thì không được cắt hành trình theo trạm của người khác.
        WithHomeCode("");
        Assert.False(SiteContextProvider.IsHomeStation("214A02", "(LCI)Kim Tân"));
    }

    [Fact]
    public void MatchesHomeCode_IgnoringCaseAndWhitespace()
    {
        WithHomeCode("ZZTEST1");
        Assert.True(SiteContextProvider.IsHomeStation("  zztest1 ", ""));
    }

    [Fact]
    public void DoesNotMatch_AnotherStationCode()
    {
        WithHomeCode("ZZTEST1");
        Assert.False(SiteContextProvider.IsHomeStation("ZZOTHER", ""));
    }

    [Fact]
    public void FollowsConfiguredCode_NotTheOld214A02Hardcode()
    {
        // Đây là hồi quy chính: trước đây "214A02" được so cứng trong DkchJourneyAnalyzer.
        WithHomeCode("214A02");
        Assert.True(SiteContextProvider.IsHomeStation("214A02", ""));

        WithHomeCode("999X01");
        Assert.False(SiteContextProvider.IsHomeStation("214A02", ""));
    }

    [Fact]
    public void DoesNotMatch_OnEmptyScanFields()
    {
        WithHomeCode("ZZTEST1");
        Assert.False(SiteContextProvider.IsHomeStation(null, null));
        Assert.False(SiteContextProvider.IsHomeStation("", ""));
    }
}
