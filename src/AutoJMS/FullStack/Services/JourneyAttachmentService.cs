using AutoJMS.FullStack.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public sealed class JourneyAttachmentService : IJourneyAttachmentService
    {
        public Task OpenAttachmentAsync(
            string waybillNo,
            JourneyEventViewModel eventRow,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppLogger.Info($"[FullStackJourneyAttachment] open requested waybill={waybillNo}, imgType={eventRow?.ImgType?.ToString() ?? "--"}; endpoint=NEED_DEVTOOLS_CAPTURE");
            throw new InvalidOperationException("Chưa cấu hình API xem ảnh. Hãy capture request ảnh bằng DevTools.");
        }
    }
}
