using AutoJMS.DataHub.Api.Auth;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record LeaseState(
    Guid SiteId,
    Guid? LeaderDeviceId,
    long LeaderTerm,
    DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? LastSeenAt,
    string Role)
{
    public int LeaseDurationSeconds => 120;
    public int RenewIntervalSeconds => 30;
};

public sealed record LeaseOperationResult(
    bool Succeeded,
    LeaseState? State,
    string? ProblemCode,
    string? Detail)
{
    public static LeaseOperationResult Success(LeaseState state) => new(true, state, null, null);
    public static LeaseOperationResult Failure(string code, string detail, LeaseState? state = null) => new(false, state, code, detail);
}

public sealed class LeaseRepository(PostgresDataSource dataSource)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(120);

    public async Task<LeaseOperationResult> AcquireAsync(Guid siteId, Guid deviceId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await ReadForUpdateAsync(connection, transaction, siteId, cancellationToken);
        if (current is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return LeaseOperationResult.Failure(ApiProblemCodes.NotFound, "The site lease has not been provisioned.");
        }

        var now = DateTimeOffset.UtcNow;
        if (current.LeaderDeviceId == deviceId && current.LeaseExpiresAt > now)
        {
            await transaction.CommitAsync(cancellationToken);
            return LeaseOperationResult.Success(current with { Role = "leader" });
        }

        if (current.LeaseExpiresAt is not null && current.LeaseExpiresAt >= now)
        {
            await transaction.CommitAsync(cancellationToken);
            return LeaseOperationResult.Failure(ApiProblemCodes.LeaseHeld, "Another device currently holds the site bulk-fetch lease.", current with { Role = "follower" });
        }

        var nextTerm = checked(current.LeaderTerm + 1);
        var updated = await UpdateLeaseAsync(
            connection,
            transaction,
            siteId,
            deviceId,
            nextTerm,
            now.Add(LeaseDuration),
            now,
            cancellationToken);
        await AuditRepository.AppendAsync(
            connection,
            transaction,
            siteId,
            $"device:{deviceId:D}",
            "lease.acquire",
            new { deviceId, leaderTerm = updated.LeaderTerm, leaseExpiresAt = updated.LeaseExpiresAt },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return LeaseOperationResult.Success(updated with { Role = "leader" });
    }

    public async Task<LeaseOperationResult> RenewAsync(Guid siteId, Guid deviceId, long term, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await ReadForUpdateAsync(connection, transaction, siteId, cancellationToken);
        if (current is null)
            return LeaseOperationResult.Failure(ApiProblemCodes.NotFound, "The site lease has not been provisioned.");

        var now = DateTimeOffset.UtcNow;
        if (current.LeaderDeviceId != deviceId || current.LeaderTerm != term || current.LeaseExpiresAt <= now)
        {
            await transaction.RollbackAsync(cancellationToken);
            return LeaseOperationResult.Failure(ApiProblemCodes.LeaderFenced, "The supplied lease term is stale or expired.", current with { Role = "follower" });
        }

        // How long the leader was silent before this renew, measured before the UPDATE
        // below overwrites last_seen_at. A normal renew lands at RenewIntervalSeconds.
        var silence = current.LastSeenAt is null ? (TimeSpan?)null : now - current.LastSeenAt.Value;

        var updated = await UpdateLeaseAsync(connection, transaction, siteId, deviceId, term, now.Add(LeaseDuration), now, cancellationToken);

        // A routine renew is no longer audited. The guard above already proved
        // current.LeaderTerm == term, so every row this used to write for a given term
        // was identical apart from leaseExpiresAt — and at RenewIntervalSeconds = 30
        // that is 2 rows per minute per site, ~2,880 a day, in a table the retention
        // job then has to scan across every site to prune.
        //
        // Nothing is lost that another record does not already hold: who leads and for
        // which term is written by lease.acquire and lease.release, and current
        // liveness is site_fetch_leases.last_seen_at, which UpdateLeaseAsync still
        // stamps on every renew. A silence long enough to matter — past the lease
        // duration — costs the leader the lease, and the takeover writes
        // lease.acquire.
        //
        // What the deleted rows did carry is the one gap that leaves no other trace: a
        // leader that missed renews but came back before the lease expired, so no
        // takeover happened and last_seen_at has since been overwritten. That is worth
        // a row, so it still gets one — and only it. A null last_seen_at is treated as
        // a gap: a lease with a matching leader always has one, so its absence is a
        // state this code did not write and should not silently pass over.
        var missedARenew = silence is null || silence.Value.TotalSeconds > current.RenewIntervalSeconds * 2;
        if (missedARenew)
        {
            await AuditRepository.AppendAsync(
                connection,
                transaction,
                siteId,
                $"device:{deviceId:D}",
                "lease.renew_after_gap",
                new
                {
                    deviceId,
                    leaderTerm = term,
                    leaseExpiresAt = updated.LeaseExpiresAt,
                    silentSeconds = silence?.TotalSeconds,
                    expectedIntervalSeconds = current.RenewIntervalSeconds
                },
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return LeaseOperationResult.Success(updated with { Role = "leader" });
    }

    public async Task<LeaseOperationResult> ReleaseAsync(Guid siteId, Guid deviceId, long term, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var current = await ReadForUpdateAsync(connection, transaction, siteId, cancellationToken);
        if (current is null)
            return LeaseOperationResult.Failure(ApiProblemCodes.NotFound, "The site lease has not been provisioned.");

        if (current.LeaderDeviceId != deviceId || current.LeaderTerm != term)
        {
            await transaction.RollbackAsync(cancellationToken);
            return LeaseOperationResult.Failure(ApiProblemCodes.LeaderFenced, "The supplied lease term is stale or is not owned by this device.", current with { Role = "follower" });
        }

        var nextTerm = checked(current.LeaderTerm + 1);
        const string sql = """
            UPDATE site_fetch_leases
               SET leader_device_id = NULL,
                   leader_term = @term,
                   lease_expires_at = '-infinity'::timestamptz,
                   last_seen_at = now()
             WHERE site_id = @site_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("site_id", siteId);
            command.Parameters.AddWithValue("term", nextTerm);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var released = current with
        {
            LeaderDeviceId = null,
            LeaderTerm = nextTerm,
            LeaseExpiresAt = null,
            LastSeenAt = DateTimeOffset.UtcNow,
            Role = "released"
        };
        await AuditRepository.AppendAsync(
            connection,
            transaction,
            siteId,
            $"device:{deviceId:D}",
            "lease.release",
            new { deviceId, oldLeaderTerm = term, newLeaderTerm = nextTerm },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return LeaseOperationResult.Success(released);
    }

    public async Task<LeaseState?> ReadAsync(Guid siteId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadForUpdateAsync(connection, null, siteId, cancellationToken);
    }

    private static async Task<LeaseState?> ReadForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT site_id, leader_device_id, leader_term,
                   lease_expires_at, last_seen_at
              FROM site_fetch_leases
             WHERE site_id = @site_id
            """;
        if (transaction is not null) sql += " FOR UPDATE;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var expires = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3);
        // PostgreSQL's -infinity maps to DateTimeOffset.MinValue with Npgsql's
        // default infinity conversion. Expose it as null at the HTTP boundary.
        if (expires <= DateTimeOffset.MinValue.AddDays(1)) expires = null;
        var lastSeen = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4);
        return new LeaseState(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetInt64(2),
            expires,
            lastSeen,
            "follower");
    }

    private static async Task<LeaseState> UpdateLeaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid siteId,
        Guid deviceId,
        long term,
        DateTimeOffset expiresAt,
        DateTimeOffset lastSeenAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE site_fetch_leases
               SET leader_device_id = @device_id,
                   leader_term = @term,
                   lease_expires_at = @expires_at,
                   last_seen_at = @last_seen_at
             WHERE site_id = @site_id
            RETURNING site_id, leader_device_id, leader_term,
                      lease_expires_at, last_seen_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("term", term);
        command.Parameters.AddWithValue("expires_at", expiresAt);
        command.Parameters.AddWithValue("last_seen_at", lastSeenAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The site lease disappeared during update.");
        return new LeaseState(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetInt64(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            "leader");
    }
}
