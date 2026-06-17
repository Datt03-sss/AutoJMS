using AutoJMS.FullStack.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Repositories
{
    public interface IWaybillJourneyDetailsRepository
    {
        Task<WaybillJourneyViewModel> GetLocalJourneyAsync(
            string waybillNo,
            CancellationToken cancellationToken);

        Task<bool> HasLocalJourneyAsync(
            string waybillNo,
            CancellationToken cancellationToken);

        Task<WaybillJourneyRawCache> GetLatestRawJsonAsync(
            string waybillNo,
            CancellationToken cancellationToken);

        Task SaveRawJsonAsync(
            string waybillNo,
            string rawJson,
            int? responseCode,
            string responseMessage,
            int eventCount,
            DateTime fetchedAt,
            DateTime expiresAt,
            string lastError,
            CancellationToken cancellationToken);

        Task ReplaceJourneyEventsAsync(
            string waybillNo,
            IReadOnlyList<WaybillJourneyRow> rows,
            DateTime fetchedAt,
            CancellationToken cancellationToken);

        Task SaveJourneySnapshotAsync(
            string waybillNo,
            string rawJson,
            int? responseCode,
            string responseMessage,
            IReadOnlyList<WaybillJourneyRow> rows,
            DateTime fetchedAt,
            DateTime expiresAt,
            string lastError,
            CancellationToken cancellationToken);
    }
}
