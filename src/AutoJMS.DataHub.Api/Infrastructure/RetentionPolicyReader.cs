using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public interface IRetentionPolicyReader
{
    Task<IReadOnlyList<RetentionDeletePolicy>> ReadEventDeletePoliciesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The one query <see cref="IngestHorizonInvariant"/>'s two callers need. Behind an
/// interface because <see cref="PostgresDataSource"/> is sealed and builds a real
/// <c>NpgsqlDataSource</c>, so this is the lowest point at which the health check can be
/// tested without a database.
/// </summary>
public sealed class RetentionPolicyReader(PostgresDataSource dataSource) : IRetentionPolicyReader
{
    public async Task<IReadOnlyList<RetentionDeletePolicy>> ReadEventDeletePoliciesAsync(
        CancellationToken cancellationToken)
    {
        // EXTRACT(EPOCH FROM ...) rather than reading the interval directly: Npgsql cannot
        // map an interval carrying months or years onto TimeSpan, and an operator is free
        // to write `interval '2 months'`. EXTRACT resolves that to seconds server-side
        // using PostgreSQL's own 30-day month, so no value in the column can throw here.
        const string sql = """
            SELECT site_id, EXTRACT(EPOCH FROM delete_after)
              FROM retention_policies
             WHERE table_name = 'waybill_scan_events';
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var policies = new List<RetentionDeletePolicy>();
        while (await reader.ReadAsync(cancellationToken))
        {
            policies.Add(new RetentionDeletePolicy(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.IsDBNull(1) ? null : TimeSpan.FromSeconds((double)reader.GetDecimal(1))));
        }

        return policies;
    }
}
