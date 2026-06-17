using AutoJMS.FullStack.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public interface IFullStackJourneyService
    {
        Task<WaybillJourneyViewModel> GetJourneyAsync(string waybillNo, CancellationToken cancellationToken);
        Task<WaybillJourneyViewModel> GetLocalJourneyAsync(string waybillNo, CancellationToken cancellationToken);
        Task<bool> HasLocalJourneyAsync(string waybillNo, CancellationToken cancellationToken);
        Task<WaybillJourneyRawCache> GetLatestRawJsonAsync(string waybillNo, CancellationToken cancellationToken);
        Task<WaybillJourneyViewModel> GetRemoteJourneyAsync(string waybillNo, string authToken, CancellationToken cancellationToken);

        Task<WaybillJourneyRefreshResult> FetchFreshJourneyAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken);

        Task SaveJourneyAsync(
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            string rawJson,
            CancellationToken cancellationToken);

        Task<WaybillJourneyRefreshResult> RefreshJourneyFromJmsAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken);

        Task CleanupExpiredAsync(CancellationToken cancellationToken);
    }
}
