using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Services;

public sealed class IngestPipeline(IngestRepository repository)
{
    public Task<IngestOperationResult> ExecuteAsync(
        Guid siteId,
        Guid deviceId,
        long? leaderTerm,
        bool requireFence,
        string idempotencyKey,
        IngestRequest request,
        CancellationToken cancellationToken)
        => repository.IngestAsync(siteId, deviceId, leaderTerm, requireFence, idempotencyKey, request, cancellationToken);
}
