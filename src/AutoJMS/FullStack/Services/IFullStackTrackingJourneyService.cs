using AutoJMS.FullStack.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public interface IFullStackTrackingJourneyService
    {
        Task<FullStackTrackingJourneyResult> FetchJourneyAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken);
    }
}
