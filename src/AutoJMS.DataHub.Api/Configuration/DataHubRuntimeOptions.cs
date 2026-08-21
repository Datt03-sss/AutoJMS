using System.Globalization;

namespace AutoJMS.DataHub.Api.Configuration;

public sealed class DataHubRuntimeOptions
{
    public const string AllowedStagingChannel = "staging";
    public const string AllowedProductionChannel = "production";

    public string EnvironmentName { get; set; } = "";
    public string Channel { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string DeviceTokenIssuer { get; set; } = "autojms-datahub";
    public string DeviceTokenAudience { get; set; } = "autojms-device";
    public string DeviceTokenSigningKey { get; set; } = "";
    public string EnrollmentPepper { get; set; } = "";
    public string LicenseAssertionIssuer { get; set; } = "autojms-license";
    public string LicenseAssertionAudience { get; set; } = "autojms-datahub-enroll";
    public string LicenseAssertionValidationKey { get; set; } = "";
    public string StagingTestSigningKey { get; set; } = "";
    public bool AllowStagingTestIssuer { get; set; }
    public int MaximumPoolSize { get; set; } = 20;

    public bool HasValidChannel => string.Equals(Channel, AllowedStagingChannel, StringComparison.Ordinal)
        || string.Equals(Channel, AllowedProductionChannel, StringComparison.Ordinal);

    public static DataHubRuntimeOptions FromConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var options = new DataHubRuntimeOptions
        {
            EnvironmentName = environment.EnvironmentName,
            Channel = FirstNonEmpty(configuration["DATAHUB_CHANNEL"], configuration["DataHub:Channel"]),
            ConnectionString = FirstNonEmpty(configuration.GetConnectionString("DataHub"), configuration["ConnectionStrings:DataHub"], configuration["ConnectionStrings__DataHub"]),
            DeviceTokenIssuer = FirstNonEmpty(configuration["DATAHUB_DEVICE_TOKEN_ISSUER"], "autojms-datahub"),
            DeviceTokenAudience = FirstNonEmpty(configuration["DATAHUB_DEVICE_TOKEN_AUDIENCE"], "autojms-device"),
            DeviceTokenSigningKey = FirstNonEmpty(configuration["DATAHUB_DEVICE_TOKEN_SIGNING_KEY"]),
            EnrollmentPepper = FirstNonEmpty(configuration["DATAHUB_ENROLLMENT_PEPPER"]),
            LicenseAssertionIssuer = FirstNonEmpty(configuration["DATAHUB_LICENSE_ASSERTION_ISSUER"], "autojms-license"),
            LicenseAssertionAudience = FirstNonEmpty(configuration["DATAHUB_LICENSE_ASSERTION_AUDIENCE"], "autojms-datahub-enroll"),
            LicenseAssertionValidationKey = FirstNonEmpty(configuration["DATAHUB_LICENSE_ASSERTION_VALIDATION_KEY"]),
            StagingTestSigningKey = FirstNonEmpty(configuration["DATAHUB_STAGING_TEST_SIGNING_KEY"]),
            AllowStagingTestIssuer = ParseBoolean(configuration["DATAHUB_ALLOW_STAGING_TEST_ISSUER"]),
            MaximumPoolSize = ParsePositiveInt(configuration["DATAHUB_DB_MAX_POOL_SIZE"], 20)
        };

        return options;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static bool ParseBoolean(string? value)
        => bool.TryParse(value, out var result) && result;

    private static int ParsePositiveInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0
            ? Math.Min(result, 100)
            : fallback;
}
