namespace AutoJMS.DataHub.Api.Tests;

/// <summary>
/// Marks a test that needs a migrated DataHub database, named by
/// <c>DATAHUB_TEST_CONNECTION_STRING</c>. Without the variable the test skips, so these
/// stay a local and staging guard rather than a build gate: no CI workflow provisions
/// PostgreSQL, and a test that cannot pass there would only teach people to ignore red.
///
/// xunit 2.x decides skipping at discovery, so the reason is set in the constructor —
/// there is no <c>Assert.Skip</c> to call from the test body on this version.
/// </summary>
public sealed class RequiresDataHubDatabaseFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "DATAHUB_TEST_CONNECTION_STRING";

    public RequiresDataHubDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            Skip = $"{ConnectionStringVariable} is not set, so there is no database to enroll against.";
    }

    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);
}
