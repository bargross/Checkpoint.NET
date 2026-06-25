using StateCheckpoint.NET.Stores;
using StateCheckpoint.NET.Stores.Mysql;
using Microsoft.Data.SqlClient;

namespace StateCheckpoint.NET.Tests.Stores.SqlServer;

public abstract class SqlServerTestBase : IAsyncLifetime
{
    protected SqlServerModelStore ModelStore { get; private set; } = null!;
    protected SqlServerSessionStore SessionStore { get; private set; } = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var connectionString = await SqlServerTestHarness.GetConnectionStringAsync();

        _connectionString = connectionString;

        await ClearTablesAsync();

        ModelStore = new SqlServerModelStore(connectionString);
        await ModelStore.EnsureSchemaAsync();

        SessionStore = new SqlServerSessionStore(connectionString);
        await SessionStore.EnsureSchemaAsync();
    }

    private async Task ClearTablesAsync()
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);

            await connection.OpenAsync();

            // SQL Server truncate order: child tables first to avoid FK violations.
            // ModelBlobs references ModelManifests, so truncate it first.
            await using var command = new SqlCommand(
                "TRUNCATE TABLE ModelBlobs; TRUNCATE TABLE ModelManifests; TRUNCATE TABLE InferenceSessions;",
                connection);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // Ignore if tables don't exist yet.
        }
    }


    public async Task DisposeAsync()
    {
        // ✅ Truncate all tables to clean up after each test.
        try
        {
            await using var connection = new SqlConnection(_connectionString);

            await connection.OpenAsync();

            await using var command = new SqlCommand(
                "TRUNCATE TABLE ModelManifests, ModelBlobs, InferenceSessions;",
                connection);

            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // Ignore if tables don't exist.
        }
    }
}