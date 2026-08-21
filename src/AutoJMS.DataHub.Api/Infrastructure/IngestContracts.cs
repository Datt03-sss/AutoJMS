using System.Text.Json;
using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed class IngestRequest
{
    public List<JmsObservation> Items { get; init; } = [];
}

public sealed record IngestResponse(
    Guid SiteId,
    int AcceptedItems,
    int DuplicateItems,
    int ChangedProjections,
    bool Replayed,
    long? FirstChangeSeq,
    long? LastChangeSeq);

public sealed record ChangeDoorbell(Guid SiteId, long ChangeSeq, string EntityType, string EntityKey);

public sealed record IngestOperationResult(
    bool Succeeded,
    int StatusCode,
    string? ProblemCode,
    string? Detail,
    IngestResponse? Response,
    IReadOnlyList<ChangeDoorbell> Doorbells)
{
    public static IngestOperationResult Success(IngestResponse response, IReadOnlyList<ChangeDoorbell> doorbells)
        => new(true, StatusCodes.Status200OK, null, null, response, doorbells);

    public static IngestOperationResult Failure(int statusCode, string code, string detail)
        => new(false, statusCode, code, detail, null, []);
}
