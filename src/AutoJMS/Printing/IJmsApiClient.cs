using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS;

public interface IJmsApiClient
{
    Task<HttpResponseMessage> PostJsonAsync(
        string url,
        string jsonBody,
        string routeName = "trackingExpress",
        string routerNameList = null,
        string origin = "https://jms.jtexpress.vn",
        CancellationToken ct = default);

    Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default);
}
