namespace AutoJMS.DataHub.Api.Configuration;

public static class StagingTestIssuerPolicy
{
    public static bool IsEnabled(string? environmentName, bool allowFlag)
        => allowFlag && string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase);
}
