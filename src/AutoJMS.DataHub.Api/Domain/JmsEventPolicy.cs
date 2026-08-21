namespace AutoJMS.DataHub.Api.Domain;

public enum JmsEventKind
{
    StateTransition,
    Activity,
    Inventory,
    Communication
}

public static class JmsEventKindExtensions
{
    public static string ToWireValue(this JmsEventKind kind) => kind switch
    {
        JmsEventKind.StateTransition => "state_transition",
        JmsEventKind.Activity => "activity",
        JmsEventKind.Inventory => "inventory",
        JmsEventKind.Communication => "communication",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public sealed record JmsEventPolicy(
    int ReducerVersion,
    int ScanTypeCode,
    JmsEventKind EventKind);

public sealed class JmsEventPolicyCatalog
{
    private readonly IReadOnlyDictionary<(int Version, int Code), JmsEventPolicy> _policies;

    public int DefaultVersion { get; }

    public static JmsEventPolicyCatalog Default { get; } = new(
    [
        new JmsEventPolicy(1, 98, JmsEventKind.Inventory),
        new JmsEventPolicy(1, 110, JmsEventKind.StateTransition)
    ]);

    public JmsEventPolicyCatalog(IEnumerable<JmsEventPolicy> policies)
    {
        ArgumentNullException.ThrowIfNull(policies);
        var map = new Dictionary<(int Version, int Code), JmsEventPolicy>();
        foreach (var policy in policies)
        {
            if (policy.ReducerVersion < 1) throw new ArgumentOutOfRangeException(nameof(policies));
            map[(policy.ReducerVersion, policy.ScanTypeCode)] = policy;
        }
        _policies = map;
        DefaultVersion = map.Count == 0 ? 1 : map.Keys.Max(key => key.Version);
    }

    public JmsEventKind Resolve(int? scanTypeCode, int reducerVersion, out JmsEventPolicy? policy)
    {
        if (scanTypeCode is not null && _policies.TryGetValue((reducerVersion, scanTypeCode.Value), out var match))
        {
            policy = match;
            return match.EventKind;
        }

        policy = null;
        return JmsEventKind.Activity;
    }

    public JmsEventKind Resolve(int? scanTypeCode, int reducerVersion = 1)
        => Resolve(scanTypeCode, reducerVersion, out _);
}
