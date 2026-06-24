using Checkpoint.NET.Manager;
using Checkpoint.NET.Stores.Postgres;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Checkpoint.NET.Tests.Integration.Postgres;

[Collection("NonParallel")]
public class PostgresTests : IntegrationTestsBase
{
    private readonly PostgreSqlContainer? _container;
    private readonly string _connectionString = string.Empty;
    private readonly bool _useTestcontainers;

    public PostgresTests()
    {
        _useTestcontainers = Environment.GetEnvironmentVariable("USE_TESTCONTAINERS") == "true";

        if (_useTestcontainers)
        {
            _container = new PostgreSqlBuilder("postgres:18-alpine")
                .WithDatabase("testdb")
                .WithUsername("testuser")
                .WithPassword("testpassword")
                .WithCleanUp(true)
                .Build();
        }
        else
        {
            var host = Environment.GetEnvironmentVariable("PG_HOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("PG_PORT") ?? "5432";
            var db = Environment.GetEnvironmentVariable("PG_DATABASE") ?? "postgres";
            var user = Environment.GetEnvironmentVariable("PG_USER") ?? "postgres";
            var password = Environment.GetEnvironmentVariable("PG_PASSWORD") ?? "postgres";

            _connectionString = $"Host={host};Port={port};Database={db};Username={user};Password={password}";
        }
    }

    protected override async Task InitializeStoresAsync()
    {
        string finalConnectionString;

        if (_useTestcontainers)
        {
            await _container!.StartAsync();
            finalConnectionString = _container.GetConnectionString();
        }
        else
        {
            await EnsureLocalDatabaseExistsAsync();
            finalConnectionString = _connectionString;
        }

        var modelStore = new PostgresModelStore(finalConnectionString);
        var sessionStore = new PostgresSessionStore(finalConnectionString);

        await modelStore.EnsureSchemaAsync();
        await sessionStore.EnsureSchemaAsync();

        CheckpointManager = new CheckpointManager(modelStore);
        SessionManager = new SessionManager(sessionStore);
    }

    private async Task EnsureLocalDatabaseExistsAsync()
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString);
            var testDbName = "checkpoint_integration";
            builder.Database = "postgres";
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            var createDbQuery = $"CREATE DATABASE {testDbName};";
            await using var command = new NpgsqlCommand(createDbQuery, connection);
            try
            {
                await command.ExecuteNonQueryAsync();
            }
            catch (PostgresException ex) when (ex.SqlState == "42P04")
            {
                // Database already exists
            }

            // Update connection string to point to the test database.
            // We'll just use the test DB name in the final connection string.
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not connectionect to local PostgreSQL. Ensure it is running.", ex);
        }
    }

    protected override async Task CleanupStoresAsync()
    {
        if (_useTestcontainers && _container != null)
        {
            await _container.DisposeAsync();
        }
    }

    [Fact]
    public async Task Checkpoint_RoundTrip_ShouldWork() => await RoundTrip_Checkpoint_ShouldSaveLoadDeleteList();

    [Fact]
    public async Task Session_RoundTrip_ShouldWork() => await RoundTrip_Session_ShouldSaveLoadDeleteList();
}