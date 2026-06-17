using AutoJMS.FullStack.Models;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public interface IJourneyAttachmentService
    {
        Task OpenAttachmentAsync(
            string waybillNo,
            JourneyEventViewModel eventRow,
            CancellationToken cancellationToken);
    }
}
