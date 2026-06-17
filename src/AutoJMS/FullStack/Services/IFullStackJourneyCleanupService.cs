using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public interface IFullStackJourneyCleanupService
    {
        Task CleanupExpiredAsync(CancellationToken cancellationToken);
    }
}
