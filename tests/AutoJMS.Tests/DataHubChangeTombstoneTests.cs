using AutoJMS.Data;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AutoJMS.Tests;

/// <summary>
/// The change feed carries an <c>operation</c> field, and until now the waybill pull ignored
/// it: every change was merged as an upsert. A <c>delete</c> tombstone has no row payload —
/// only its key — so merging one wrote a blank record over a good one and the deleted waybill
/// stayed in local SQLite forever. These tests pin the split, because a tombstone read as an
/// upsert produces a local row that looks perfectly valid and nothing downstream reports it.
/// </summary>
public class DataHubChangeTombstoneTests
{
    private static JObject Upsert(string waybillNo, long seq) => new()
    {
        ["changeSeq"] = seq,
        ["entityType"] = "waybill_projection",
        ["entityKey"] = waybillNo,
        ["operation"] = "upsert",
        ["changeAt"] = "2026-08-26T03:00:00Z",
        ["body"] = new JObject
        {
            ["waybillNo"] = waybillNo,
            ["status"] = "Đang giao",
            ["updatedAt"] = "2026-08-26T03:00:00Z"
        }
    };

    /// <summary>Shaped exactly as RetentionRepository emits it: the key, and nothing else.</summary>
    private static JObject Tombstone(string waybillNo, long seq) => new()
    {
        ["changeSeq"] = seq,
        ["entityType"] = "waybill_projection",
        ["entityKey"] = waybillNo,
        ["operation"] = "delete",
        ["changeAt"] = "2026-08-26T03:05:00Z",
        ["body"] = new JObject { ["waybill_no"] = waybillNo }
    };

    [Fact]
    public void A_tombstone_becomes_a_deletion_and_never_a_row()
    {
        var (rows, deleted) = DataHubClient.ProjectChangeItems(new[] { Tombstone("886000000001", 41) });

        Assert.Empty(rows);
        Assert.Equal(new[] { "886000000001" }, deleted);
    }

    [Fact]
    public void Upserts_and_tombstones_on_one_page_are_kept_apart()
    {
        var (rows, deleted) = DataHubClient.ProjectChangeItems(new[]
        {
            Upsert("886000000001", 40),
            Tombstone("886000000002", 41),
            Upsert("886000000003", 42)
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "886000000002" }, deleted);
        // The surviving rows keep their own keys — a page holding a deletion must not drop or
        // reorder the upserts around it, which is what a single shared list would risk.
        Assert.Equal(
            new[] { "886000000001", "886000000003" },
            rows.Select(row => row.Value<string>("waybill_no")).ToArray());
    }

    [Fact]
    public void A_deletion_key_is_normalized_the_same_way_a_row_key_is()
    {
        // fs_waybills.waybill_no is stored upper-cased and trimmed, so a DELETE built from a
        // raw entityKey would match nothing and silently leave the row in place.
        var (_, deleted) = DataHubClient.ProjectChangeItems(new[] { Tombstone("  886abc000004  ", 43) });

        Assert.Equal(new[] { "886ABC000004" }, deleted);
    }

    [Fact]
    public void An_unknown_operation_is_treated_as_an_upsert_rather_than_a_deletion()
    {
        // Only 'delete' removes data. A resync marker or any operation added server-side later
        // must not be able to clear local rows just because it is unrecognised here.
        var resync = Upsert("886000000005", 44);
        resync["operation"] = "resync";

        var (rows, deleted) = DataHubClient.ProjectChangeItems(new[] { resync });

        Assert.Single(rows);
        Assert.Empty(deleted);
    }

    [Fact]
    public void A_tombstone_for_another_entity_type_is_ignored_entirely()
    {
        // The entity-type gate comes first: a deletion published for something that does not
        // live in fs_waybills must not delete a waybill that happens to share the key.
        var foreign = Tombstone("886000000006", 45);
        foreign["entityType"] = "site_setting";

        var (rows, deleted) = DataHubClient.ProjectChangeItems(new[] { foreign });

        Assert.Empty(rows);
        Assert.Empty(deleted);
    }

    [Fact]
    public void A_tombstone_with_no_key_is_dropped_instead_of_deleting_something_else()
    {
        // An empty key would build "DELETE FROM fs_waybills WHERE waybill_no = ''". That
        // matches nothing today, but a malformed change must never be one schema change away
        // from a wildcard delete.
        var keyless = Tombstone("886000000007", 46);
        keyless["entityKey"] = "   ";

        var (rows, deleted) = DataHubClient.ProjectChangeItems(new[] { keyless });

        Assert.Empty(rows);
        Assert.Empty(deleted);
    }

    [Fact]
    public void A_page_carries_its_deletions_through_to_the_caller()
    {
        // The sync service reads DeletedWaybillNos, so an empty default matters: a page built
        // without deletions must hand back a list, not null, or every existing pull throws.
        var withoutDeletions = new DataHubChangePage(new List<JObject>(), 12, false, false);
        Assert.Empty(withoutDeletions.DeletedWaybillNos);

        var withDeletions = new DataHubChangePage(
            new List<JObject>(), 13, false, false, false, new List<string> { "886000000008" });
        Assert.Equal(new[] { "886000000008" }, withDeletions.DeletedWaybillNos);
    }

    [Fact]
    public void A_resynced_page_reports_no_deletions()
    {
        // A snapshot states what exists, not what was removed, and it can be truncated — so
        // absence from it is not evidence of deletion. Inferring deletions from a snapshot
        // would let a capped read wipe out the site's older waybills locally.
        var snapshot = new DataHubChangePage(new List<JObject> { Upsert("886000000009", 1) }, 99, false, true, true);

        Assert.True(snapshot.Resynced);
        Assert.True(snapshot.Truncated);
        Assert.Empty(snapshot.DeletedWaybillNos);
    }
}
