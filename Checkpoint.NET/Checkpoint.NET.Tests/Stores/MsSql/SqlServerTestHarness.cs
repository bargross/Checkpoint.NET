using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Checkpoint.NET.Tests.Stores.SqlServer;

/// <summary>
/// Shared test harness for SQL Server tests.
/// Handles environment detection (Testcontainers vs LocalDB) and database creation.
/// </summary>
internal static class SqlServerTestHarness
{
    private static MsSqlContainer? _container;
    private static string? _connectionString;
    private static readonly object _lock = new();

    public static bool UseTestcontainers { get; } =
        Environment.GetEnvironmentVariable("USE_TESTCONTAINERS") == "true";

    private const string MasterConnectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=master;Integrated Security=True;";
    private const string TestDatabaseName = "CheckpointIntegration";

    public static async Task<string> GetConnectionStringAsync()
    {
        if (_connectionString != null)
            return _connectionString;

        lock (_lock)
        {
            if (_connectionString != null)
                return _connectionString;

            if (UseTestcontainers)
            {
                _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                    .WithPassword("Your_password123!")
                    .WithCleanUp(true)
                    .Build();
            }
        }

        if (UseTestcontainers)
        {
            await _container!.StartAsync();
            _connectionString = _container.GetConnectionString();
        }
        else
        {
            // Use LocalDB (fallback for local development without Docker)
            _connectionString = $"Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog={TestDatabaseName};Integrated Security=True;";
            await EnsureLocalDatabaseExistsAsync();
        }

        return _connectionString;
    }

    private static async Task EnsureLocalDatabaseExistsAsync()
    {
        try
        {
            using var masterConn = new SqlConnection(MasterConnectionString);
            await masterConn.OpenAsync();

            var createDbQuery = $@"
                IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = '{TestDatabaseName}')
                BEGIN
                    CREATE DATABASE [{TestDatabaseName}];
                END";
            using var command = new SqlCommand(createDbQuery, masterConn);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not connectionect to LocalDB. Ensure it is installed and running (e.g., via Visual Studio).",
                ex);
        }
    }

    public static async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }
}