using System;
using System.Threading.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Checkpoint.NET.Tests.Stores.Postgres;

/// <summary>
/// Shared test harness for PostgreSQL tests.
/// Handles environment detection (Testcontainers vs Local) and database creation.
/// </summary>
internal static class PostgresTestHarness
{
    private static PostgreSqlContainer? _container;
    private static string? _connectionString;
    private static readonly object _lock = new();

    public static bool UseTestcontainers { get; } =
        Environment.GetEnvironmentVariable("USE_TESTCONTAINERS") == "true";

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
                // Create and start the container
                _container = new PostgreSqlBuilder("postgres:16-alpine")
                    .WithDatabase("testdb")
                    .WithUsername("testuser")
                    .WithPassword("testpassword")
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
            _connectionString = "Host=localhost;Database=postgres;Username=postgres;";

            // Ensure the test database exists
            await EnsureLocalDatabaseExistsAsync(_connectionString);
        }

        return _connectionString;
    }

    private static async Task EnsureLocalDatabaseExistsAsync(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var testDbName = builder.Database;

        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();

        var createDbQuery = $"CREATE DATABASE {testDbName};";

        await using var command = new NpgsqlCommand(createDbQuery, connection);

        try { await command.ExecuteNonQueryAsync(); } catch { /* Ignore if exists */ }

        builder.Database = testDbName;

        await using var testConn = new NpgsqlConnection(builder.ConnectionString);

        await testConn.OpenAsync();

        await using var extCmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS lo;", testConn);

        await extCmd.ExecuteNonQueryAsync();
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