using AutoJMS.FullStack.LocalDb;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace AutoJMS.Tests;

// details.db nằm theo mã bưu cục (SiteContextProvider.Get) nên test đổi ActionSiteCode sang
// một mã tạm, rồi trả state cũ và xoá thư mục tạm trong Dispose.
[Collection("SiteContext")]
public sealed class WaybillJourneyDetailsDbInitializerTests : IDisposable
{
    private readonly string _originalActionSiteCode;
    private readonly string _siteCode = "T" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    public WaybillJourneyDetailsDbInitializerTests()
    {
        _originalActionSiteCode = AppConfig.Current.ActionSiteCode;
        AppConfig.Current.ActionSiteCode = _siteCode;
        SiteContextProvider.InvalidateCache();
    }

    public void Dispose()
    {
        var folder = Path.GetDirectoryName(new WaybillJourneyDetailsDbConnectionFactory().DatabasePath)!;
        AppConfig.Current.ActionSiteCode = _originalActionSiteCode;
        SiteContextProvider.InvalidateCache();
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch { /* temp files, best effort */ }
    }

    // Xác minh: init trên file mới phải thành công. Test fail nếu index (waybill_no, seq) chạy
    // trước khi EnsureColumnAsync thêm cột seq — lỗi "no such column: seq" làm rollback cả init.
    [Fact]
    public async Task InitializeAsync_FreshDatabase_CreatesSeqIndex()
    {
        var factory = new WaybillJourneyDetailsDbConnectionFactory();
        Assert.Contains(_siteCode, factory.DatabasePath);

        await new WaybillJourneyDetailsDbInitializer(factory).InitializeAsync();

        await using var connection = await factory.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_index_info('idx_journey_events_waybill') ORDER BY seqno;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("waybill_no", reader.GetString(0));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("seq", reader.GetString(0));
    }
}
