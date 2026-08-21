using AutoJMS.DataHub.Api.Domain;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

/// <summary>
/// Reads the versioned JMS policy rows from PostgreSQL at the same transaction
/// boundary as ingest. The fallback is only useful for unit tests and keeps the
/// API surface diagnosable before migrations are applied; production readiness
/// still requires the schema and seed migrations.
/// </summary>
public sealed class JmsEventPolicyRepository
{
    public async Task<JmsEventPolicyCatalog> LoadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT reducer_version, scan_type_code, event_kind
              FROM jms_event_policies
             ORDER BY reducer_version, scan_type_code;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var policies = new List<JmsEventPolicy>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var kind = reader.GetString(2) switch
            {
                "state_transition" => JmsEventKind.StateTransition,
                "activity" => JmsEventKind.Activity,
                "inventory" => JmsEventKind.Inventory,
                "communication" => JmsEventKind.Communication,
                _ => throw new InvalidOperationException("The database contains an unsupported JMS event kind.")
            };
            policies.Add(new JmsEventPolicy(reader.GetInt32(0), reader.GetInt32(1), kind));
        }

        // An empty catalog intentionally maps every code to activity. Readiness
        // requires the seed migration, so this is a safe diagnostic fallback,
        // never a hidden reactivation of hard-coded state semantics.
        return new JmsEventPolicyCatalog(policies);
    }
}
