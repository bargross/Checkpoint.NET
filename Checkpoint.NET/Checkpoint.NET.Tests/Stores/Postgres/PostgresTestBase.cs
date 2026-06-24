using Checkpoint.NET.Stores.Postgres;
using Npgsql;

namespace Checkpoint.NET.Tests.Stores.Postgres;

public abstract class PostgresTestBase : IAsyncLifetime
{
    protected PostgresModelStore ModelStore { get; private set; } = null!;
    protected PostgresSessionStore SessionStore { get; private set; } = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var connectionString = await PostgresTestHarness.GetConnectionStringAsync();

        _connectionString = connectionString;

        await ClearTablesAsync();

        ModelStore = new PostgresModelStore(connectionString);
        await ModelStore.EnsureSchemaAsync();

        SessionStore = new PostgresSessionStore(connectionString);
        await SessionStore.EnsureSchemaAsync();
    }

    private async Task ClearTablesAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            // TRUNCATE CASCADE handles foreign keys automatically in PostgreSQL
            await using var command = new NpgsqlCommand(
                "TRUNCATE TABLE model_manifests, model_blobs, inference_sessions CASCADE;",
                connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // Ignore if tables don't exist yet (they will be created later in InitializeAsync)
        }
    }

    public async Task DisposeAsync()
    {
        // Truncate all tables to clean up after each test.
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);

            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(
                "TRUNCATE TABLE model_manifests, model_blobs, inference_sessions RESTART IDENTITY CASCADE;",
                connection);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // Ignore if tables don't exist – they will be created on the next test.
        }
    }
}