using AutoJMS.FullStack.LocalDb;
using Microsoft.Data.Sqlite;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoJMS.FullStack.Services
{
    public sealed class FullStackJourneyCleanupService : IFullStackJourneyCleanupService
    {
        private readonly WaybillJourneyDetailsDbConnectionFactory _connectionFactory;
        private readonly WaybillJourneyDetailsDbInitializer _initializer;

        public FullStackJourneyCleanupService()
            : this(new WaybillJourneyDetailsDbConnectionFactory())
        {
        }

        public FullStackJourneyCleanupService(WaybillJourneyDetailsDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
            _initializer = new WaybillJourneyDetailsDbInitializer(_connectionFactory);
        }

        public async Task CleanupExpiredAsync(CancellationToken cancellationToken)
        {
            await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.Now;
            var eventCutoff = DateTime.Now.AddDays(-30);
            await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM waybill_journey_json_cache WHERE expires_at < $now;",
                cancellationToken,
                ("$now", now.ToString("O"))).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM waybill_journey_cache WHERE expires_at < $now;",
                cancellationToken,
                ("$now", now.ToString("O"))).ConfigureAwait(false);

            await ExecuteAsync(
                connection,
                transaction,
                "DELETE FROM waybill_journey_events WHERE COALESCE(created_at, fetched_at) < $cutoff;",
                cancellationToken,
                ("$cutoff", eventCutoff.ToString("O"))).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task ExecuteAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sql,
            CancellationToken cancellationToken,
            params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
