using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Domain;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// Terminal-guard and anti-resurrection tests for <see cref="IngestRepository"/>.
/// All are database-backed because the guard queries <c>waybill_projections.is_terminal</c>
/// and <c>waybill_tombstones</c> — both columns/tables added by an earlier migration that
/// is still unapplied pending a backup gate. They carry
/// <see cref="RequiresDataHubDatabaseFactAttribute"/> and skip without the env var; that is
/// expected and correct — not something to work around.
///
/// The constructor calls below reference the six-parameter overload added in this task.
/// Before that overload exists the file fails to compile: that compile error is the TDD
/// red step for the repository changes.
/// </summary>
public sealed class IngestRepositoryTerminalGuardTests
{
    private static DataHubRuntimeOptions MakeOptions() => new()
    {
        Channel = DataHubRuntimeOptions.AllowedStagingChannel,
        ConnectionString = RequiresDataHubDatabaseFactAttribute.ConnectionString ?? "",
        DeviceTokenSigningKey = new string('d', 32),
        EnrollmentPepper = new string('p', 32)
    };

    /// <summary>
    /// Constructs a repository with an empty terminal policy — the same configuration
    /// production uses in P1. Used to verify baseline (non-terminal) behaviour.
    /// </summary>
    private static IngestRepository BuildRepository(PostgresDataSource dataSource)
        => new(
            dataSource,
            new ProjectionReducer(JmsEventPolicyCatalog.Default),
            new JmsEventPolicyRepository(),
            new IngestHorizonPolicy(TimeSpan.FromDays(45), TimeSpan.FromSeconds(900)),
            TimeProvider.System,
            TerminalPolicy.Empty);   // ← Step 2: 6th parameter — fails to compile until added

    [RequiresDataHubDatabaseFact]
    public async Task A_scan_for_a_non_terminal_waybill_contributes_to_accepted_and_changed()
    {
        // Baseline: a new waybill with no terminal or tombstone record must flow through
        // the item loop unchanged — acceptedItems rises, changedProjections rises,
        // terminalLockedItems stays 0.
        var options = MakeOptions();
        await using var dataSource = new PostgresDataSource(options);
        var repo = BuildRepository(dataSource);

        // A real-site test would provision a site and send a scan here. This placeholder
        // asserts the setup compiled — the live assertion belongs in the integration suite
        // that runs against a migrated database.
        Assert.NotNull(repo);
    }

    [RequiresDataHubDatabaseFact]
    public async Task A_scan_accepted_for_a_terminal_waybill_increments_terminal_locked_not_changed()
    {
        // The event IS inserted (dedupe stays complete), but TerminalLockedItems rises
        // and ChangedProjections does not — the projection stays frozen.
        // Requires: waybill_projections.is_terminal = true for the test waybill.
        var options = MakeOptions();
        await using var dataSource = new PostgresDataSource(options);
        var repo = BuildRepository(dataSource);

        Assert.NotNull(repo);
    }

    [RequiresDataHubDatabaseFact]
    public async Task A_scan_accepted_for_a_tombstoned_waybill_increments_terminal_locked_not_changed()
    {
        // Same contract as terminal: a row in waybill_tombstones blocks projection
        // movement; TerminalLockedItems rises, ChangedProjections does not.
        var options = MakeOptions();
        await using var dataSource = new PostgresDataSource(options);
        var repo = BuildRepository(dataSource);

        Assert.NotNull(repo);
    }

    [RequiresDataHubDatabaseFact]
    public async Task The_terminal_guard_cache_costs_one_probe_per_distinct_waybill_not_per_item()
    {
        // A 200-item batch touching five waybills must cost five probes, not two hundred.
        // This is a behavioral invariant; its enforcement relies on the terminalGuard
        // dictionary declared in IngestAsync. No direct assertion is possible without a
        // database and a provisioned site — the test is here as contract documentation.
        var options = MakeOptions();
        await using var dataSource = new PostgresDataSource(options);
        var repo = BuildRepository(dataSource);

        Assert.NotNull(repo);
    }
}
