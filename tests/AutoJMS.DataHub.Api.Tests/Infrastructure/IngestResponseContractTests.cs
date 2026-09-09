using System.Text.Json;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// Verifies the contract invariants for <see cref="IngestResponse.TerminalLockedItems"/>
/// without a database. The field is last and defaulted because responses already stored
/// in <c>idempotency_records.response</c> were serialized without it, and a replay of
/// one of those requests must still deserialize as 0.
/// </summary>
public sealed class IngestResponseContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TerminalLockedItems_defaults_to_zero_when_not_supplied()
    {
        // All existing call sites omit the final argument; the default must be 0 so
        // that IngestResponse(siteId, n, m, k, false, first, last) still compiles and
        // produces the correct value for requests that never touched a terminal waybill.
        var response = new IngestResponse(
            Guid.Empty,
            AcceptedItems: 5,
            DuplicateItems: 0,
            ChangedProjections: 3,
            Replayed: false,
            FirstChangeSeq: 1L,
            LastChangeSeq: 3L);

        Assert.Equal(0, response.TerminalLockedItems);
    }

    [Fact]
    public void Old_JSON_without_the_field_deserializes_to_zero()
    {
        // idempotency_records.response rows stored before this field was added must still
        // deserialize on replay: a missing JSON member for a defaulted constructor
        // parameter uses the default (0), which is what those requests actually did.
        const string oldJson =
            """{"siteId":"00000000-0000-0000-0000-000000000001","acceptedItems":5,"duplicateItems":0,"changedProjections":3,"replayed":false,"firstChangeSeq":1,"lastChangeSeq":3}""";

        var response = JsonSerializer.Deserialize<IngestResponse>(oldJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal(0, response!.TerminalLockedItems);
    }

    [Fact]
    public void TerminalLockedItems_round_trips_through_JSON_when_nonzero()
    {
        var original = new IngestResponse(
            Guid.NewGuid(), 10, 2, 5, false, 1L, 5L, TerminalLockedItems: 3);

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<IngestResponse>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized!.TerminalLockedItems);
        // Pin the camelCase wire name: stored idempotency_records.response rows and all
        // clients depend on this exact spelling. A rename would silently break replay.
        Assert.Contains("\"terminalLockedItems\"", json);
    }

    /// <summary>
    /// Pins the one-way terminal semantics in the upsert SQL without a database.
    /// The SQL is a compile-time constant, so this test can never flake and costs nothing.
    ///
    /// WHY THIS MATTERS: if the OR / COALESCE guards are removed from the ON CONFLICT
    /// DO UPDATE clause, an ordinary later upsert clears is_terminal and re-opens a
    /// waybill that was terminal — the anti-resurrection invariant collapses silently
    /// because no runtime error is raised when a boolean flag is overwritten with false.
    /// </summary>
    [Fact]
    public void UpsertProjectionSql_does_not_clear_a_terminal_mark_on_conflict()
    {
        var sql = IngestRepository.UpsertProjectionSql;

        // is_terminal ORs the stored value with the incoming value — a false incoming
        // flag can never clear a true that was already persisted.
        Assert.Contains(
            "is_terminal = waybill_projections.is_terminal OR EXCLUDED.is_terminal",
            sql,
            StringComparison.Ordinal);

        // terminal_at and terminal_state_code COALESCE — once written, they are never
        // overwritten by a later upsert that sends NULLs for those columns.
        Assert.Contains(
            "COALESCE(waybill_projections.terminal_at, EXCLUDED.terminal_at)",
            sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "COALESCE(waybill_projections.terminal_state_code, EXCLUDED.terminal_state_code)",
            sql,
            StringComparison.Ordinal);
    }
}
