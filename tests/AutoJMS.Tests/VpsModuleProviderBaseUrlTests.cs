using System.Threading.Tasks;
using AutoJMS.ModuleSystem;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// Khoá lại lỗi đã giết cả luồng Background module sync: VpsModuleProvider chỉ đọc env var
/// để lấy base URL, trong khi URL thật của DataHub đến từ phản hồi license
/// (Program.cs:345 → DataHubClient.Configure). Trên máy trạm hai env var đó không được đặt,
/// nên _storageBase rỗng, request thành URI tương đối "/manifest/app-manifest.json" và
/// HttpClient ném "An invalid request URI was provided...".
/// </summary>
public class VpsModuleProviderBaseUrlTests
{
    [Fact]
    public void Base_url_lay_tu_DataHub_khi_khong_co_env_var()
    {
        // Đây là cấu hình của một máy trạm thật: không env var nào, URL do license cấp.
        var resolved = VpsModuleProvider.ResolveStorageBase(
            modulesOverride: null,
            dataHubBaseUrl: "https://dev.jmsauto.online",
            apiBaseUrl: null);

        Assert.Equal("https://dev.jmsauto.online", resolved);
    }

    [Fact]
    public void Env_var_modules_van_thang_de_tro_sang_CDN_rieng()
    {
        var resolved = VpsModuleProvider.ResolveStorageBase(
            modulesOverride: "https://modules.example/",
            dataHubBaseUrl: "https://dev.jmsauto.online",
            apiBaseUrl: null);

        Assert.Equal("https://modules.example", resolved);
    }

    [Fact]
    public void Env_var_api_la_chot_cuoi_khi_DataHub_chua_cau_hinh()
    {
        var resolved = VpsModuleProvider.ResolveStorageBase(
            modulesOverride: null,
            dataHubBaseUrl: null,
            apiBaseUrl: "https://api.example");

        Assert.Equal("https://api.example", resolved);
    }

    [Fact]
    public void Khong_co_nguon_nao_thi_tra_rong_chu_khong_nem()
    {
        var resolved = VpsModuleProvider.ResolveStorageBase(null, null, null);

        Assert.Equal("", resolved);
    }

    /// <summary>
    /// App chạy offline hoàn toàn: không được ném, không được chặn khởi động.
    /// Trước đây catch filter `when (attempt < MaxRetries)` để lần thử cuối thoát ra ngoài,
    /// nên một fetch hỏng làm sập nguyên SyncAsync.
    /// </summary>
    [Fact]
    public async Task Sync_khong_nem_khi_chua_co_base_url()
    {
        var provider = new VpsModuleProvider(storageBase: "");

        // Trước bản vá, chính lời gọi này ném InvalidOperationException từ HttpClient.
        var manifest = await provider.FetchAppManifestAsync();
        Assert.NotNull(manifest);

        var modules = await provider.FetchModulesManifestAsync();
        Assert.True(modules?.Modules == null || modules.Modules.Count == 0);

        Assert.False(await provider.DownloadFileAsync("demo", "modules/demo.dll", sha256: null));

        await provider.SyncAsync();   // không được ném
    }

    /// <summary>
    /// Base URL hợp lệ nhưng không kết nối được (app-manifest.json chưa có seed trên VPS là
    /// đúng trường hợp này). Lần thử thứ 3 phải bị bắt như hai lần đầu; catch filter cũ
    /// `when (attempt &lt; MaxRetries)` để nó thoát ra và giết luôn SyncAsync.
    /// </summary>
    [Fact]
    public async Task Lan_thu_cuoi_that_bai_cung_khong_duoc_thoat_ra_ngoai()
    {
        // Cổng 1 không có gì lắng nghe: URI tuyệt đối hợp lệ, kết nối bị từ chối ngay.
        var provider = new VpsModuleProvider(storageBase: "http://127.0.0.1:1");

        var manifest = await provider.FetchAppManifestAsync();

        Assert.NotNull(manifest);
    }
}
