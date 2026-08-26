using AutoJMS.DataHub.Api.Configuration;
using AutoJMS.DataHub.Api.Infrastructure;
using Npgsql;

namespace AutoJMS.DataHub.Api.Tests.Health;

/// <summary>
/// Covers the deadlines every pooled connection must carry. They are asserted on the built
/// connection string rather than against a live server because that string is the whole
/// mechanism: the timeouts are PostgreSQL startup options, so if they are absent here no
/// repository has a deadline, and no amount of integration testing would make one appear.
/// </summary>
public sealed class PostgresConnectionStringTests
{
    private static DataHubRuntimeOptions Options(int seconds = DataHubRuntimeOptions.DefaultStatementTimeoutSeconds, string extra = "")
        => new()
        {
            ConnectionString = "Host=postgres;Database=datahub;Username=datahub;Password=test" + extra,
            MaximumPoolSize = 20,
            DatabaseStatementTimeoutSeconds = seconds
        };

    private static NpgsqlConnectionStringBuilder Build(DataHubRuntimeOptions options)
        => new(PostgresDataSource.BuildConnectionString(options));

    [Fact]
    public void Every_connection_starts_with_a_statement_deadline()
    {
        var built = Build(Options());

        Assert.Contains("-c statement_timeout=30s", built.Options);
        // The second deadline covers what statement_timeout cannot: a transaction that finished
        // its statements and then lost its client keeps every FOR UPDATE row locked until TCP
        // notices. Two statement budgets, because a transaction legitimately runs several.
        Assert.Contains("-c idle_in_transaction_session_timeout=60s", built.Options);
    }

    [Fact]
    public void Client_timeout_sits_above_the_server_deadline()
    {
        // Equal values race, and whoever wins decides how the failure looks. With the server
        // first, a runaway query always ends as PostgreSQL 57014 with the backend gone and its
        // locks released — not Npgsql walking away from a statement that keeps running.
        var built = Build(Options());

        Assert.Equal(35, built.CommandTimeout);
    }

    [Fact]
    public void Operator_options_win_because_postgres_applies_them_last()
    {
        var built = Build(Options(extra: ";Options=-c statement_timeout=90s"));

        // PostgreSQL applies -c in order, so a deployment that needs a different budget
        // overrides the default without a code change. Both appear; the operator's is last.
        Assert.EndsWith("-c statement_timeout=90s", built.Options);
        Assert.StartsWith("-c statement_timeout=30s", built.Options);
    }

    [Theory]
    [InlineData(0, DataHubRuntimeOptions.MinimumStatementTimeoutSeconds)]
    [InlineData(-5, DataHubRuntimeOptions.MinimumStatementTimeoutSeconds)]
    [InlineData(99999, DataHubRuntimeOptions.MaximumStatementTimeoutSeconds)]
    public void An_out_of_range_budget_is_clamped_rather_than_honoured(int configured, int expected)
    {
        // A misconfigured 0 must not mean "no deadline", which is exactly what PostgreSQL would
        // read it as — the one value that turns this safeguard into its opposite.
        var built = Build(Options(configured));

        Assert.Contains($"-c statement_timeout={expected}s", built.Options);
    }

    [Fact]
    public void Pool_limits_and_application_name_survive_the_rewrite()
    {
        var built = Build(Options());

        Assert.Equal(20, built.MaxPoolSize);
        Assert.Equal(0, built.MinPoolSize);
        Assert.Equal("AutoJMS.DataHub.Api", built.ApplicationName);
        Assert.Equal("datahub", built.Database);
    }
}
