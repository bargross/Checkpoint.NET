using StateCheckpoint.NET.Manager;
using StateCheckpoint.NET.Stores;
using StateCheckpoint.NET.Stores.Mysql;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace StateCheckpoint.NET.Tests.Integration.SqlServer;

[Collection("NonParallel")]
public class SqlServerTests : IntegrationTestsBase
{
    private readonly MsSqlContainer? _container = null;
    private readonly string _connectionString = string.Empty;
    private readonly bool _useTestcontainers;

    public SqlServerTests()
    {
        _useTestcontainers = Environment.GetEnvironmentVariable("USE_TESTCONTAINERS") == "true";

        if (_useTestcontainers)
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .WithPassword("Your_password123!")
                .WithCleanUp(true)
                .Build();
        }
        else
        {
            _connectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=CheckpointIntegration;Integrated Security=True;";
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
            await EnsureLocalDbDatabaseExistsAsync();
            finalConnectionString = _connectionString;
        }

        var modelStore = new SqlServerModelStore(finalConnectionString);
        var sessionStore = new SqlServerSessionStore(finalConnectionString);

        await modelStore.EnsureSchemaAsync();
        await sessionStore.EnsureSchemaAsync();

        CheckpointManager = new CheckpointManager(modelStore);
        SessionManager = new SessionManager(sessionStore);
    }

    private static async Task EnsureLocalDbDatabaseExistsAsync()
    {
        const string masterConnectionString = "Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=master;Integrated Security=True;";
        using var connection = new SqlConnection(masterConnectionString);
        await connection.OpenAsync();

        var createDbQuery = @"
            IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'CheckpointIntegration')
            BEGIN
                CREATE DATABASE [CheckpointIntegration];
            END";
        using var command = new SqlCommand(createDbQuery, connection);
        await command.ExecuteNonQueryAsync();
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