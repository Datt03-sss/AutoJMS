using AutoJMS.FullStack.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public interface IFullStackPodTrackingApiClient
    {
        Task<PodTrackingRawResponse> GetRawJourneyJsonAsync(
            string waybillNo,
            string authToken,
            CancellationToken cancellationToken);
    }
}
