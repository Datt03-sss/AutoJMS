using System.Text.Json;
using AutoJMS.DataHub.Api.Domain;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed class IngestRequest
{
    public List<JmsObservation> Items { get; init; } = [];
}

/// <summary>
/// <paramref name="TerminalLockedItems"/> counts events that were accepted into the event
/// log but did not move a projection, because the waybill is terminal or tombstoned. It is
/// last and defaulted because responses already stored in
/// <c>idempotency_records.response</c> were serialized without it, and a replay of one of
/// those requests must still deserialize — as 0, which is what those requests actually did.
/// </summary>
public sealed record IngestResponse(
    Guid SiteId,
    int AcceptedItems,
    int DuplicateItems,
    int ChangedProjections,
    bool Replayed,
    long? FirstChangeSeq,
    long? LastChangeSeq,
    int TerminalLockedItems = 0);

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
