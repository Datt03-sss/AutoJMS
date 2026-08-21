using AutoJMS.DataHub.Api.Configuration;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public interface IDataHubDatabaseProbe
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public sealed class PostgresDataSource : IAsyncDisposable
{
    private readonly NpgsqlDataSource? _dataSource;

    public PostgresDataSource(DataHubRuntimeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) return;

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                MaxPoolSize = Math.Clamp(options.MaximumPoolSize, 1, 100),
                MinPoolSize = 0,
                ApplicationName = "AutoJMS.DataHub.Api"
            };
            _dataSource = NpgsqlDataSource.Create(builder.ConnectionString);
        }
        catch (ArgumentException)
        {
            // Readiness reports a malformed connection string as unhealthy; the
            // process should still expose liveness for orchestrator diagnostics.
            _dataSource = null;
        }
    }

    public bool IsConfigured => _dataSource is not null;

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        if (_dataSource is null) return false;
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
    }

    public ValueTask DisposeAsync()
        => _dataSource is null ? ValueTask.CompletedTask : _dataSource.DisposeAsync();
}

public sealed class NpgsqlDatabaseProbe(PostgresDataSource dataSource) : IDataHubDatabaseProbe
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
        => dataSource.CanConnectAsync(cancellationToken);
}
