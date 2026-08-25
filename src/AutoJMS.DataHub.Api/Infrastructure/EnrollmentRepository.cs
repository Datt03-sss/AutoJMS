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

        var normalizedSiteCode = siteCode.Trim().ToUpperInvariant();
        var normalizedDeviceName = deviceName.Trim();
        if (normalizedSiteCode.Length > 64 || normalizedDeviceName.Length > 128)
            return Failure(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "siteCode or deviceName exceeds the phase-1 length limit.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string siteSql = "SELECT id, site_code FROM sites WHERE upper(site_code) = upper(@site_code) FOR UPDATE;";
        await using var siteCommand = new NpgsqlCommand(siteSql, connection, transaction);
        siteCommand.Parameters.AddWithValue("site_code", normalizedSiteCode);
        await using var siteReader = await siteCommand.ExecuteReaderAsync(cancellationToken);
        if (!await siteReader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, "The requested site has not been provisioned.");
        }
        var siteId = siteReader.GetGuid(0);
        var canonicalSiteCode = siteReader.GetString(1);
        await siteReader.DisposeAsync();

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

    private static EnrollmentResult Failure(int status, string code, string detail)
        => new(false, status, code, detail, null, null, null, null, 0, null);
}
