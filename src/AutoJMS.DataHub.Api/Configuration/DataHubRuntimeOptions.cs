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
    /// <summary>PEM of the license issuer's RSA PUBLIC key (\n escapes accepted). Enables production enrollment.</summary>
    public string LicenseAssertionPublicKeyPem { get; set; } = "";
    /// <summary>File holding that PEM; takes precedence over the inline value (Docker/systemd secrets).</summary>
    public string LicenseAssertionPublicKeyPath { get; set; } = "";
    public string StagingTestSigningKey { get; set; } = "";
    public bool AllowStagingTestIssuer { get; set; }

    /// <summary>
    /// The public hostname this deployment answers on, e.g. <c>datahub-stg.jmsauto.online</c>.
    /// Used only to build the absolute <c>type</c> URI of RFC 7807 problem responses.
    ///
    /// Read from DATAHUB_PUBLIC_HOST, the same variable Caddy already uses for its site
    /// address, so there is exactly one place where "what host are we" is written down.
    /// Compose previously passed it to the caddy service only; it now reaches the api
    /// service too. Never used for routing or trust decisions — only for a display string.
    /// </summary>
    public string PublicHost { get; set; } = "";

    /// <summary>
    /// Operator token for PUT /api/v1/admin/manifests/**, published by
    /// release/build-release.ps1 as DATAHUB_ADMIN_TOKEN. Empty means administrative
    /// publishing is closed, not open.
    /// </summary>
    public string ManifestAdminToken { get; set; } = "";

    /// <summary>Directory the control-plane objects are served from; a named volume in Compose.</summary>
    public string ManifestRoot { get; set; } = DefaultManifestRoot;

    public int MaximumPoolSize { get; set; } = 20;
    public TimeSpan DeviceTokenLifetime { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromMinutes(15);
    public int RetentionBatchSize { get; set; } = 1000;

    /// <summary>
    /// CIDR ranges whose X-Forwarded-* headers are honoured. Only the reverse proxy may be
    /// trusted here: an empty trust list makes ForwardedHeadersMiddleware accept the header
    /// from any caller, which lets a client forge its own client IP and evade the per-IP
    /// rate limits. Default covers the Docker/Compose and LAN ranges Caddy runs in.
    /// </summary>
    public const string DefaultTrustedProxyNetworks = "127.0.0.1/32,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16";

    /// <summary>Comma-separated CIDR list; override with DATAHUB_TRUSTED_PROXY_NETWORKS.</summary>
    public string TrustedProxyNetworks { get; set; } = DefaultTrustedProxyNetworks;

    /// <summary>
    /// Container mount point for the manifest volume. Absolute so it does not depend
    /// on the working directory, and outside the app directory so a redeploy that
    /// replaces the image cannot take the published objects with it.
    /// </summary>
    public const string DefaultManifestRoot = "/manifests";

    /// <summary>
    /// The identity variables that decide which tokens this host will accept. Every one of
    /// them has a built-in default below, so "is it empty" is a question that can never be
    /// answered no — the only observable fact is whether the operator set it or inherited
    /// the default, which is what <see cref="DefaultedIdentityVariables"/> records.
    /// </summary>
    public static readonly string[] IdentityVariableNames =
    [
        "DATAHUB_DEVICE_TOKEN_ISSUER",
        "DATAHUB_DEVICE_TOKEN_AUDIENCE",
        "DATAHUB_LICENSE_ASSERTION_ISSUER",
        "DATAHUB_LICENSE_ASSERTION_AUDIENCE"
    ];

    /// <summary>
    /// Which of <see cref="IdentityVariableNames"/> fell back to their defaults. Populated by
    /// <see cref="FromConfiguration"/> only; a hand-built instance reports nothing defaulted.
    /// </summary>
    public IReadOnlyList<string> DefaultedIdentityVariables { get; set; } = [];

    /// <summary>
    /// The configured proxy CIDR entries that do not parse, and are therefore silently
    /// dropped from the trust list at startup. Empty when the list is sound.
    /// </summary>
    public static IReadOnlyList<string> MalformedTrustedProxyNetworks(string? configured)
        => (configured ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !System.Net.IPNetwork.TryParse(entry, out _))
            .ToArray();

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
            LicenseAssertionPublicKeyPem = FirstNonEmpty(configuration["DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY"]),
            LicenseAssertionPublicKeyPath = FirstNonEmpty(configuration["DATAHUB_LICENSE_ASSERTION_PUBLIC_KEY_PATH"]),
            StagingTestSigningKey = FirstNonEmpty(configuration["DATAHUB_STAGING_TEST_SIGNING_KEY"]),
            AllowStagingTestIssuer = ParseBoolean(configuration["DATAHUB_ALLOW_STAGING_TEST_ISSUER"]),
            ManifestAdminToken = FirstNonEmpty(configuration["DATAHUB_ADMIN_TOKEN"]),
            ManifestRoot = FirstNonEmpty(configuration["DATAHUB_MANIFEST_ROOT"], DefaultManifestRoot),
            MaximumPoolSize = ParseBoundedInt(configuration["DATAHUB_DB_MAX_POOL_SIZE"], 20, 1, 100),
            DeviceTokenLifetime = TimeSpan.FromSeconds(ParseBoundedInt(configuration["DATAHUB_DEVICE_TOKEN_LIFETIME_SECONDS"], 86400, 300, 2592000)),
            RetentionInterval = TimeSpan.FromSeconds(ParseBoundedInt(configuration["DATAHUB_RETENTION_INTERVAL_SECONDS"], 900, 60, 86400)),
            RetentionBatchSize = ParseBoundedInt(configuration["DATAHUB_RETENTION_BATCH_SIZE"], 1000, 100, 5000),
            TrustedProxyNetworks = FirstNonEmpty(configuration["DATAHUB_TRUSTED_PROXY_NETWORKS"], DefaultTrustedProxyNetworks),
            PublicHost = NormalizePublicHost(configuration["DATAHUB_PUBLIC_HOST"])
        };

        // Recorded here and nowhere else: this is the only place that can still see the
        // difference between "the operator chose autojms-license" and "nobody set it".
        // FirstNonEmpty has collapsed the two by the time anything else reads the value.
        options.DefaultedIdentityVariables = IdentityVariableNames
            .Where(name => string.IsNullOrWhiteSpace(configuration[name]))
            .ToArray();

        return options;
    }

    /// <summary>
    /// Strips a scheme/path if someone pasted a whole URL, and discards the RFC 2606
    /// reserved names.
    ///
    /// The reserved-name filter is the point of this method. Both env templates ship
    /// <c>DATAHUB_PUBLIC_HOST=datahub.example.com</c> as a fill-me-in marker, so a host
    /// that never got edited would otherwise put a domain nobody owns into the
    /// <c>type</c> field of every error response — which is precisely the bug being
    /// fixed, just relocated from a C# literal into a config file. An empty result makes
    /// the writer emit a relative <c>/problems/...</c> reference instead, which RFC 7807
    /// §3.1 allows and which resolves against whatever host the client actually reached.
    /// </summary>
    private static string NormalizePublicHost(string? value)
    {
        var host = (value ?? "").Trim();
        if (host.Length == 0) return "";

        // Only a literal scheme prefix is stripped. Uri.TryCreate is deliberately not
        // used: it reads "example.com:8080" as scheme "example.com" and hands back an
        // empty Host, turning a mildly wrong value into a silently empty one.
        foreach (var scheme in new[] { "https://", "http://" })
        {
            if (host.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                host = host[scheme.Length..];
                break;
            }
        }

        host = host.Split('/')[0].Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return "";

        foreach (var reserved in ReservedHostSuffixes)
        {
            if (host == reserved.TrimStart('.') || host.EndsWith(reserved, StringComparison.Ordinal))
                return "";
        }

        return host;
    }

    private static readonly string[] ReservedHostSuffixes =
    [
        ".example", ".example.com", ".example.net", ".example.org",
        ".invalid", ".test", ".localhost"
    ];

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static bool ParseBoolean(string? value)
        => bool.TryParse(value, out var result) && result;

    private static int ParseBoundedInt(string? value, int fallback, int minimum, int maximum)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? Math.Clamp(result, minimum, maximum)
            : fallback;
}
