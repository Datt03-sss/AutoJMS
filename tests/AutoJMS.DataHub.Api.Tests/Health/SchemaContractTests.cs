using System.Text.RegularExpressions;
using AutoJMS.DataHub.Api.Infrastructure;

namespace AutoJMS.DataHub.Api.Tests.Health;

/// <summary>
/// Keeps the readiness probe's idea of the schema equal to the migrations on disk.
///
/// The probe cannot read the SQL files — the API image does not ship them — so
/// <see cref="PostgresDataSource.RequiredMigrations"/> is a hand-written list, and a
/// hand-written list of files is a list that goes stale. It already had: it stopped at
/// 005 while 006 existed, so a host that had never applied 006 answered
/// <c>/health/ready</c> with 200 and Caddy put traffic on it.
///
/// The failure mode matters more than the drift. Readiness is what
/// <c>docker-compose.yml</c> gates the API container on, so a probe that accepts too
/// much does not report a problem — it silently admits a host with a missing table and
/// turns a deploy-order mistake into 500s on the first request that touches it. These
/// tests move that discovery to the build.
/// </summary>
public sealed class SchemaContractTests
{
    [Fact]
    public void Required_migrations_are_exactly_the_migration_files_in_order()
    {
        // Every file records its own stem as its schema_migrations version — asserted
        // below — so the stems ARE the version strings, and the numeric prefix makes
        // the sorted file order the apply order.
        var onDisk = Directory
            .GetFiles(MigrationRoot(), "*.sql")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(stem => stem, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(onDisk);
        Assert.Equal(onDisk, PostgresDataSource.RequiredMigrations);
    }

    [Fact]
    public void Every_migration_records_its_own_file_name_as_its_version()
    {
        // The premise of the test above. If a file inserted a version that differed
        // from its stem, the stem list would compare equal to the files and still be
        // wrong against the database.
        foreach (var file in Directory.GetFiles(MigrationRoot(), "*.sql"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            var sql = File.ReadAllText(file);

            var recorded = Regex.Matches(sql, @"INSERT INTO schema_migrations\s*\(version\)\s*VALUES\s*\(\s*'([^']+)'")
                .Select(match => match.Groups[1].Value)
                .ToArray();

            Assert.Single(recorded);
            Assert.Equal(stem, recorded[0]);
        }
    }

    [Fact]
    public void Required_tables_are_exactly_the_tables_the_migrations_create()
    {
        var created = Directory
            .GetFiles(MigrationRoot(), "*.sql")
            .SelectMany(file => Regex.Matches(
                    File.ReadAllText(file),
                    @"CREATE TABLE\s+(?:IF NOT EXISTS\s+)?([a-z0-9_]+)",
                    RegexOptions.IgnoreCase)
                .Select(match => match.Groups[1].Value))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(created);
        // Set comparison, because the probe's list is grouped for reading rather than
        // sorted, and its order carries no meaning.
        Assert.Equal(
            created,
            PostgresDataSource.RequiredTables.OrderBy(name => name, StringComparer.Ordinal));

        // No duplicates on either side — a repeated name would let the probe's
        // count(*) = Length comparison pass with one real table missing.
        Assert.Equal(
            PostgresDataSource.RequiredTables.Length,
            PostgresDataSource.RequiredTables.Distinct().Count());
    }

    [Fact]
    public void Readiness_identifiers_are_bare_identifiers()
    {
        // The probe builds its SQL by interpolating these names as literals, so this is
        // the property that makes that safe. It is enforced at type initialisation too;
        // this test says so out loud, and fails at build time rather than at startup.
        foreach (var identifier in PostgresDataSource.RequiredTables.Concat(PostgresDataSource.RequiredMigrations))
            Assert.Matches("^[a-z0-9_]+$", identifier);
    }

    [Fact]
    public void The_probe_query_names_every_required_migration_and_table()
    {
        var sql = PostgresDataSource.ReadinessSql;

        foreach (var table in PostgresDataSource.RequiredTables)
            Assert.Contains($"'{table}'", sql);

        foreach (var version in PostgresDataSource.RequiredMigrations)
            Assert.Contains($"'{version}'", sql);

        // The counts are the whole check: a list of names with no `= Length` beside it
        // is satisfied by finding one of them.
        Assert.Contains($")) = {PostgresDataSource.RequiredTables.Length}", sql);
        Assert.Contains($"version IN (", sql);
        Assert.Contains($") = {PostgresDataSource.RequiredMigrations.Length}", sql);

        // Balanced parentheses, because the query is assembled rather than written and
        // an unbalanced one fails only against a live server.
        Assert.Equal(sql.Count(ch => ch == '('), sql.Count(ch => ch == ')'));
    }

    private static string MigrationRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AutoJMS.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var root = Path.Combine(directory!.FullName, "backend", "datahub", "migrations");
        Assert.True(Directory.Exists(root), $"The migration directory is missing: {root}");
        return root;
    }
}
