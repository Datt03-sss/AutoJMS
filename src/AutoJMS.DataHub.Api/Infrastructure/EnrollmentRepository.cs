using System.Security.Cryptography;
using System.Text;
using AutoJMS.DataHub.Api.Auth;
using AutoJMS.DataHub.Api.Configuration;
using Npgsql;

namespace AutoJMS.DataHub.Api.Infrastructure;

public sealed record EnrollmentResult(
    bool Succeeded,
    int StatusCode,
    string? ProblemCode,
    string? Detail,
    Guid? DeviceId,
    Guid? SiteId,
    string? SiteCode,
    string? DeviceToken,
    int TokenVersion,
    DateTimeOffset? ExpiresAt);

public sealed class EnrollmentRepository(
    PostgresDataSource dataSource,
    IDeviceTokenService tokenService,
    DataHubRuntimeOptions options)
{
    public async Task<EnrollmentResult> EnrollAsync(
        string siteCode,
        string deviceName,
        string role,
        LicenseAssertionIdentity license,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(siteCode) || string.IsNullOrWhiteSpace(deviceName))
            return Failure(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "siteCode and deviceName are required.");
        if (!string.Equals(role, "operator", StringComparison.Ordinal))
            return Failure(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Only the operator role is available during phase 1.");
        if (string.IsNullOrWhiteSpace(options.EnrollmentPepper) || options.EnrollmentPepper.Length < 32)
            return Failure(StatusCodes.Status503ServiceUnavailable, ApiProblemCodes.ServiceUnavailable, "Enrollment secret is not configured.");

        // The same normaliser the assertion's own site codes went through, not a copy of
        // its body: the licence check below compares this string against that set, so the
        // two must stay one rule. Two identical-looking expressions would let a later
        // change to one of them open a gap between what the edge licenses and what this
        // method creates.
        var normalizedSiteCode = LicenseAssertionClaims.NormalizeSiteCode(siteCode);
        var normalizedDeviceName = deviceName.Trim();
        if (normalizedSiteCode.Length > 64 || normalizedDeviceName.Length > 128)
            return Failure(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "siteCode or deviceName exceeds the phase-1 length limit.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var site = await FindSiteAsync(connection, transaction, normalizedSiteCode, cancellationToken);
        if (site is null)
        {
            // A site code the RSA-signed assertion names is a site this caller is entitled
            // to, so there is nothing for an admin to decide and no reason to answer 404 and
            // make someone SSH in to run an INSERT. The assertion is the authority.
            //
            // Which is exactly why the licence scope is re-checked here rather than trusted
            // from the edge. EnrollmentEndpoints already refuses an unlicensed site code, but
            // this method used to be incapable of creating anything; now it populates `sites`,
            // and the rule that decides what may be created belongs next to the creation. A
            // caller reaching the repository by any other route gets the same answer the HTTP
            // edge gives.
            if (!license.SiteCodes.Contains(normalizedSiteCode))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failure(StatusCodes.Status403Forbidden, ApiProblemCodes.SiteNotLicensed, "The requested site is outside the signed license scope.");
            }
            site = await ProvisionSiteAsync(connection, transaction, normalizedSiteCode, normalizedDeviceName, cancellationToken);
        }
        var siteId = site.Value.Id;
        var canonicalSiteCode = site.Value.Code;

        const string existingDeviceSql = """
            SELECT id, status, token_version
              FROM devices
             WHERE site_id = @site_id AND name = @name
             FOR UPDATE;
            """;
        Guid? existingDeviceId = null;
        string? existingStatus = null;
        var existingTokenVersion = 0;
        await using (var existingCommand = new NpgsqlCommand(existingDeviceSql, connection, transaction))
        {
            existingCommand.Parameters.AddWithValue("site_id", siteId);
            existingCommand.Parameters.AddWithValue("name", normalizedDeviceName);
            await using var existingReader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (await existingReader.ReadAsync(cancellationToken))
            {
                existingDeviceId = existingReader.GetGuid(0);
                existingStatus = existingReader.GetString(1);
                existingTokenVersion = existingReader.GetInt32(2);
            }
        }
        if (existingDeviceId is not null && !string.Equals(existingStatus, "active", StringComparison.Ordinal))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(StatusCodes.Status409Conflict, "DEVICE_CONFLICT", "This device name is revoked or disabled and cannot be re-enrolled.");
        }

        var deviceId = existingDeviceId ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = new[] { now.Add(options.DeviceTokenLifetime), license.ExpiresAt }
            .Min();
        if (expiresAt <= now)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(StatusCodes.Status401Unauthorized, ApiProblemCodes.Unauthorized, "The signed license assertion is expired.");
        }
        var tokenVersion = existingDeviceId is null
            ? Math.Max(license.TokenVersion, 1)
            : Math.Max(existingTokenVersion + 1, Math.Max(license.TokenVersion, 1));

        // Locking the site row above serializes concurrent enrollments so they cannot exceed the signed
        // seat allowance. This is intentionally conservative for multi-site
        // licenses: each site gets at most the declared seat count.
        const string seatSql = """
            SELECT count(*)
              FROM devices
             WHERE site_id = @site_id AND status = 'active';
            """;
        await using (var seatCommand = new NpgsqlCommand(seatSql, connection, transaction))
        {
            seatCommand.Parameters.AddWithValue("site_id", siteId);
            var activeSeats = Convert.ToInt32(await seatCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            if (existingDeviceId is null && activeSeats >= Math.Max(license.Seats, 1))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Failure(StatusCodes.Status409Conflict, "SEAT_LIMIT_REACHED", "The signed license seat limit has been reached for this site.");
            }
        }
        var token = tokenService.Issue(new DeviceTokenDescriptor(deviceId, siteId, options.Channel, role, tokenVersion, expiresAt));
        var credentialHash = DeviceCredentialHash.Compute(options.EnrollmentPepper, token);

        const string deviceSql = """
            INSERT INTO devices (id, site_id, name, credential_hash, token_version, status, last_seen_at)
            VALUES (@id, @site_id, @name, @credential_hash, @token_version, 'active', now());
            """;
        try
        {
            if (existingDeviceId is null)
            {
                await using var deviceCommand = new NpgsqlCommand(deviceSql, connection, transaction);
                deviceCommand.Parameters.AddWithValue("id", deviceId);
                deviceCommand.Parameters.AddWithValue("site_id", siteId);
                deviceCommand.Parameters.AddWithValue("name", normalizedDeviceName);
                deviceCommand.Parameters.AddWithValue("credential_hash", credentialHash);
                deviceCommand.Parameters.AddWithValue("token_version", tokenVersion);
                await deviceCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                const string rotateSql = """
                    UPDATE devices
                       SET credential_hash = @credential_hash,
                           token_version = @token_version,
                           status = 'active',
                           last_seen_at = now(),
                           updated_at = now()
                     WHERE id = @id AND status = 'active';
                    """;
                await using var rotateCommand = new NpgsqlCommand(rotateSql, connection, transaction);
                rotateCommand.Parameters.AddWithValue("id", deviceId);
                rotateCommand.Parameters.AddWithValue("credential_hash", credentialHash);
                rotateCommand.Parameters.AddWithValue("token_version", tokenVersion);
                await rotateCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(StatusCodes.Status409Conflict, "DEVICE_CONFLICT", "A device with this name is already enrolled at the site.");
        }

        await AuditRepository.AppendAsync(
            connection,
            transaction,
            siteId,
            "license-enrollment",
            existingDeviceId is null ? "device.enroll" : "device.reenroll",
            new { deviceId, deviceName = normalizedDeviceName, role, tokenVersion },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EnrollmentResult(true, StatusCodes.Status201Created, null, null, deviceId, siteId, canonicalSiteCode, token, tokenVersion, expiresAt);
    }

    private const string SiteLookupSql =
        "SELECT id, site_code FROM sites WHERE upper(site_code) = upper(@site_code) FOR UPDATE;";

    /// <summary>
    /// Reads the site row and closes its reader before returning, which is the whole reason
    /// the read lives in here: Npgsql allows one command in progress per connector, so a
    /// reader still open at the next command — a rollback, the provisioning call, the seat
    /// count — throws <c>NpgsqlOperationInProgressException</c> instead of doing the thing.
    /// That defect shipped once already, as a 503 on the not-found path. Scoping the reader
    /// to this method makes it structural rather than a DisposeAsync each new branch has to
    /// remember.
    /// </summary>
    private static async Task<(Guid Id, string Code)?> FindSiteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string normalizedSiteCode,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SiteLookupSql, connection, transaction);
        command.Parameters.AddWithValue("site_code", normalizedSiteCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetGuid(0), reader.GetString(1))
            : null;
    }

    /// <summary>
    /// Creates the site row together with its fetch lease and change counter, inside the
    /// caller's transaction — so an enrollment that fails afterwards on seats or on a device
    /// conflict takes the half-built site down with it rather than leaving it behind.
    ///
    /// Two stations of a brand-new site can enroll in the same instant. Neither
    /// <c>SELECT ... FOR UPDATE</c> saw a row to lock, so both arrive here and the unique
    /// index on <c>sites.site_code</c> is what separates them: the loser's INSERT blocks
    /// until the winner commits, then raises 23505 — and the row it collided with is the
    /// exact row it wanted, so it re-reads it and carries on.
    ///
    /// The SAVEPOINT is not decoration. PostgreSQL aborts the entire transaction on any
    /// error, so without one that re-read comes back 25P02 "current transaction is aborted"
    /// and the loser fails the race it is supposed to survive.
    /// </summary>
    private static async Task<(Guid Id, string Code)> ProvisionSiteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string normalizedSiteCode,
        string normalizedDeviceName,
        CancellationToken cancellationToken)
    {
        const string savepoint = "site_provision";
        const string createSql = "SELECT create_datahub_site(@site_id, @site_code);";
        var newSiteId = Guid.NewGuid();
        await transaction.SaveAsync(savepoint, cancellationToken);
        try
        {
            await using (var command = new NpgsqlCommand(createSql, connection, transaction))
            {
                command.Parameters.AddWithValue("site_id", newSiteId);
                command.Parameters.AddWithValue("site_code", normalizedSiteCode);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            // Written here, not beside the device audit below, so only the station that
            // actually created the site records it — the loser of the race above returns
            // through the catch and never reaches this line. Nothing else in the system
            // notes that a site appeared without anyone provisioning it, and "when did
            // this site show up, and who brought it" is the first question an operator
            // asks about a row nobody remembers creating.
            await AuditRepository.AppendAsync(
                connection,
                transaction,
                newSiteId,
                "license-enrollment",
                "site.auto_provision",
                new { siteCode = normalizedSiteCode, deviceName = normalizedDeviceName },
                cancellationToken);

            // create_datahub_site stores upper(btrim(code)); the argument was already
            // trimmed and upper-cased, so what is in the row is this string.
            return (newSiteId, normalizedSiteCode);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(savepoint, cancellationToken);
            var winner = await FindSiteAsync(connection, transaction, normalizedSiteCode, cancellationToken);
            if (winner is not null) return winner.Value;

            // Only site_code can realistically collide: the id is freshly generated and the
            // lease and counter rows are keyed on it. An absent row means some other
            // constraint fired, and the original exception names it — letting it out keeps
            // that name in the log instead of trading it for a status that would be a guess.
            throw;
        }
    }

    private static EnrollmentResult Failure(int status, string code, string detail)
        => new(false, status, code, detail, null, null, null, null, 0, null);
}
