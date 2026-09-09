namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// Which scan type codes end a waybill's life.
///
/// The set is a constructor argument rather than something read from
/// <c>jms_event_policies</c>, because that table cannot answer the question: its
/// <c>event_kind</c> is CHECK-constrained to state_transition, activity, inventory and
/// communication, and <see cref="Domain.JmsEventKind"/> mirrors those four. Terminal
/// classification has no carrier in the schema or the domain, and contract §19.3 forbids
/// adding one in P1.
///
/// So the set is empty by construction, not by configuration — which is what makes P1
/// fail-closed without a feature flag. <see cref="Empty"/> is what production gets; OD-1
/// defers the real set past P4, and until it is signed no code may be added here, to a
/// migration, or to JmsEventKind — including "just for a test". A test constructing its own
/// instance is not seeding: nothing outside that test's object graph sees it.
/// </summary>
public sealed class TerminalPolicy(IEnumerable<int> terminalScanTypeCodes)
{
    /// <summary>The production instance. Empty until OD-1 is signed.</summary>
    public static readonly TerminalPolicy Empty = new([]);

    private readonly HashSet<int> _codes = [.. terminalScanTypeCodes];

    public int Count => _codes.Count;

    public bool IsTerminal(int? scanTypeCode) => scanTypeCode is { } code && _codes.Contains(code);
}
