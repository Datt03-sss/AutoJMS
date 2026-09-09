using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Infrastructure;

/// <summary>
/// The codes below are the test's own and exist nowhere else — not in a migration, not in
/// <c>JmsEventKind</c>, not in production DI. OD-1 defers the real terminal set past P4 and
/// forbids seeding one anywhere, including for a test; taking the set as a constructor
/// argument is what makes the branch testable without breaking that.
/// </summary>
public sealed class TerminalPolicyTests
{
    [Fact]
    public void The_production_policy_is_empty()
    {
        // This is the fail-closed guarantee, asserted rather than assumed. If a later change
        // seeds a terminal code into the shared instance, this is the test that says so.
        Assert.Equal(0, TerminalPolicy.Empty.Count);
    }

    [Fact]
    public void An_empty_policy_treats_every_code_as_non_terminal()
    {
        Assert.False(TerminalPolicy.Empty.IsTerminal(98));
        Assert.False(TerminalPolicy.Empty.IsTerminal(110));
        Assert.False(TerminalPolicy.Empty.IsTerminal(int.MaxValue));
    }

    [Fact]
    public void A_configured_code_is_terminal()
    {
        var policy = new TerminalPolicy([7001]);

        Assert.True(policy.IsTerminal(7001));
    }

    [Fact]
    public void A_code_outside_the_set_is_not_terminal()
    {
        var policy = new TerminalPolicy([7001]);

        Assert.False(policy.IsTerminal(7002));
    }

    [Fact]
    public void A_null_code_is_never_terminal()
    {
        // JmsObservation.Code is nullable and a slot can carry no code at all. Null must
        // read as "not terminal", never as a match against a set that happens to be empty.
        Assert.False(new TerminalPolicy([7001]).IsTerminal(null));
        Assert.False(TerminalPolicy.Empty.IsTerminal(null));
    }

    [Fact]
    public void Duplicate_codes_collapse()
    {
        var policy = new TerminalPolicy([7001, 7001, 7002]);

        Assert.Equal(2, policy.Count);
        Assert.True(policy.IsTerminal(7002));
    }
}
